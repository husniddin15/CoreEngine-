using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.UI
{
    /// <summary>
    /// The custom code editor (docs/04 §code editor, docs/13 Q6): a virtualised ListView of rich-text lines with
    /// line numbers, syntax colours and auto-indent, edited as in any code editor:
    /// <list type="bullet">
    /// <item>click to put the caret anywhere, drag or Shift+click to select, double-click a word, triple-click a
    /// line; Shift with the arrows, Home, End and Page keys selects, Ctrl with the arrows jumps by words;</item>
    /// <item>Ctrl+A, Ctrl+C, Ctrl+X, Ctrl+V with Windows' clipboard (a copy or cut with nothing selected takes the
    /// line), Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z), and the same on a right-click menu;</item>
    /// <item>typing replaces the selection, Tab and Shift+Tab indent and unindent the selected lines,
    /// Ctrl+Backspace and Ctrl+Delete remove a word.</item>
    /// </list>
    /// Only visible lines exist as elements, so a long sketch scrolls as cheaply as a short one.
    /// Not yet: find, bracket matching, horizontal scrolling.
    /// </summary>
    public sealed class CodeEditor : VisualElement
    {
        public const float LineHeight = 18f;
        const float TextLeft = 54f; // gutter width 48 + code padding 6, see UiSpike.uss
        const int Indent = 2;

        /// <summary>True while an editor has keyboard focus, so game shortcuts stay quiet.</summary>
        public static bool HasTypingFocus { get; private set; }

        /// <summary>A place in the text: a line and a column in it (0-based).</summary>
        readonly struct Pos : IComparable<Pos>
        {
            public readonly int Line, Col;

            public Pos(int line, int col)
            {
                Line = line;
                Col = col;
            }

            public int CompareTo(Pos other) => Line != other.Line ? Line.CompareTo(other.Line) : Col.CompareTo(other.Col);

            public static bool operator ==(Pos a, Pos b) => a.Line == b.Line && a.Col == b.Col;
            public static bool operator !=(Pos a, Pos b) => !(a == b);
            public override bool Equals(object? obj) => obj is Pos p && p == this;
            public override int GetHashCode() => Line * 7919 + Col;
        }

        /// <summary>The text and the selection as they were before an edit, for undo and redo.</summary>
        sealed class Snapshot
        {
            public string Text = "";
            public Pos Caret, Anchor;
        }

        readonly List<string> lines = new List<string> { "" };
        readonly List<string?> rich = new List<string?> { null };
        readonly List<bool> commentBefore = new List<bool> { false };
        readonly StringBuilder builder = new StringBuilder();
        readonly ListView list;
        readonly VisualElement caret, selectionLayer;
        readonly Label measure;
        readonly List<Snapshot> undo = new List<Snapshot>(), redo = new List<Snapshot>();
        FontDefinition? font;
        float charWidth = 7.7f;
        Pos caretPos, anchor;
        int wantedColumn = -1;  // the column the Up and Down keys keep to across short lines
        bool focused;
        bool blinkOn = true;
        bool selecting;         // the left button is down and drags the selection
        int clickKind;          // 1 characters, 2 words, 3 lines: what a drag after the click selects by
        Pos wordStart, wordEnd; // the word or line a double or triple click took
        string typingRun = "";  // typed characters since the last undo step, so a word undoes as one
        float typedAt;
        VisualElement? menu;

        public event Action? CaretMoved;

        public int LineCount => lines.Count;
        /// <summary>Lines re-coloured by the last edit (the edited line plus any whose comment state changed).</summary>
        public int LastRecolourCount { get; private set; }
        public int CaretLine => caretPos.Line;
        public int CaretColumn => caretPos.Col;
        public ScrollView ScrollView => list.Q<ScrollView>();
        public bool HasSelection => caretPos != anchor;
        public string SelectedText => HasSelection ? TextBetween(Min(caretPos, anchor), Max(caretPos, anchor)) : "";

        public CodeEditor()
        {
            AddToClassList("code-editor");
            focusable = true;

            // The selection is drawn under the lines, whose rows are see-through.
            selectionLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            selectionLayer.AddToClassList("code-selection");
            selectionLayer.generateVisualContent += DrawSelection;
            Add(selectionLayer);

            list = new ListView(lines, LineHeight, MakeRow, BindRow)
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                focusable = false,
            };
            list.AddToClassList("code-lines");
            Add(list);

            caret = new VisualElement { pickingMode = PickingMode.Ignore };
            caret.AddToClassList("code-caret");
            Add(caret);

            measure = new Label("MMMMMMMMMMMMMMMMMMMM") { pickingMode = PickingMode.Ignore };
            measure.AddToClassList("code-text");
            measure.AddToClassList("code-measure");
            Add(measure);

            ScrollView.verticalScroller.valueChanged += _ =>
            {
                UpdateCaret();
                selectionLayer.MarkDirtyRepaint();
            };
            RegisterCallback<GeometryChangedEvent>(_ => { MeasureCharWidth(); UpdateCaret(); selectionLayer.MarkDirtyRepaint(); });
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            // Arrow keys, Tab and Enter must edit the text instead of moving focus to another control.
            RegisterCallback<NavigationMoveEvent>(e => { e.StopPropagation(); focusController?.IgnoreEvent(e); });
            RegisterCallback<NavigationSubmitEvent>(e => { e.StopPropagation(); focusController?.IgnoreEvent(e); });
            RegisterCallback<FocusInEvent>(_ => { focused = true; HasTypingFocus = true; UpdateCaret(); });
            RegisterCallback<FocusOutEvent>(_ => { focused = false; HasTypingFocus = false; UpdateCaret(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { CloseMenu(); if (focused) HasTypingFocus = false; });
            schedule.Execute(() => { blinkOn = !blinkOn; UpdateCaret(); }).Every(530);
            // Held at the top or bottom edge, a drag selection scrolls on.
            schedule.Execute(ScrollWhileSelecting).Every(40);
        }

        public void SetFont(FontDefinition value)
        {
            font = value;
            measure.style.unityFontDefinition = value;
            list.Rebuild();
            MeasureCharWidth();
        }

        public string Text => string.Join("\n", lines);

        public void SetText(string text)
        {
            LoadLines(text);
            caretPos = anchor = new Pos(0, 0);
            undo.Clear();
            redo.Clear();
            typingRun = "";
            list.RefreshItems();
            list.ScrollToItem(0);
            UpdateCaret();
            selectionLayer.MarkDirtyRepaint();
        }

        void LoadLines(string text)
        {
            lines.Clear();
            rich.Clear();
            commentBefore.Clear();
            foreach (string line in Clean(text).Split('\n'))
            {
                lines.Add(line);
                rich.Add(null);
                commentBefore.Add(false);
            }
            Recolour(0, all: true);
        }

        /// <summary>Line ends as \n and tabs as the editor's indent, for typed, pasted and loaded text alike.</summary>
        static string Clean(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", new string(' ', Indent));

        public void MoveCaret(int line, int column) => MoveTo(ClampPos(line, column), extend: false);

        /// <summary>Selects from one place to another (the caret goes to the second), for the benchmark and tests.</summary>
        public void Select(int fromLine, int fromColumn, int toLine, int toColumn)
        {
            anchor = ClampPos(fromLine, fromColumn);
            caretPos = ClampPos(toLine, toColumn);
            wantedColumn = -1;
            RevealCaret();
        }

        public void SelectAll()
        {
            anchor = new Pos(0, 0);
            caretPos = new Pos(lines.Count - 1, lines[lines.Count - 1].Length);
            RevealCaret();
        }

        Pos ClampPos(int line, int column)
        {
            line = Mathf.Clamp(line, 0, lines.Count - 1);
            return new Pos(line, Mathf.Clamp(column, 0, lines[line].Length));
        }

        void MoveTo(Pos to, bool extend, bool keepColumn = false)
        {
            caretPos = to;
            if (!extend) anchor = to;
            if (!keepColumn) wantedColumn = -1;
            typingRun = "";
            RevealCaret();
        }

        // ------------------------------------------------------------------ editing

        /// <summary>Inserts typed text at the caret, over the selection (no newlines; Enter goes through <see cref="NewLine"/>).</summary>
        public void Insert(string text)
        {
            bool typing = text.Length == 1 && !HasSelection && Time.unscaledTime - typedAt < 1.5f && typingRun.Length < 40 && typingRun.Length > 0
                          && char.IsLetterOrDigit(text[0]) == char.IsLetterOrDigit(typingRun[typingRun.Length - 1]);
            if (!typing) Remember();
            DeleteSelection();
            string line = lines[caretPos.Line];
            int column = caretPos.Col;
            // Typing '}' on a line of only spaces removes one indent level, like most code editors.
            if (text == "}" && column >= Indent && line.Substring(0, column).Trim().Length == 0)
            {
                lines[caretPos.Line] = line = line.Remove(column - Indent, Indent);
                column -= Indent;
            }
            lines[caretPos.Line] = line.Insert(column, text);
            caretPos = anchor = new Pos(caretPos.Line, column + text.Length);
            typingRun = text.Length == 1 ? typingRun + text : "";
            typedAt = Time.unscaledTime;
            wantedColumn = -1;
            Edited(caretPos.Line, linesChanged: false);
        }

        public void NewLine()
        {
            Remember();
            DeleteSelection();
            string line = lines[caretPos.Line];
            string head = line.Substring(0, caretPos.Col);
            string tail = line.Substring(caretPos.Col).TrimStart(' ');
            string indent = new string(' ', head.Length - head.TrimStart(' ').Length);
            if (head.TrimEnd().EndsWith("{", StringComparison.Ordinal)) indent += new string(' ', Indent);
            lines[caretPos.Line] = head;
            lines.Insert(caretPos.Line + 1, indent + tail);
            rich.Insert(caretPos.Line + 1, null);
            commentBefore.Insert(caretPos.Line + 1, false);
            caretPos = anchor = new Pos(caretPos.Line + 1, indent.Length);
            wantedColumn = -1;
            Edited(caretPos.Line - 1, linesChanged: true);
        }

        public void Backspace() => Erase(forward: false, word: false);

        public void DeleteForward() => Erase(forward: true, word: false);

        void Erase(bool forward, bool word)
        {
            if (HasSelection)
            {
                Remember();
                int first = Min(caretPos, anchor).Line;
                bool many = caretPos.Line != anchor.Line;
                DeleteSelection();
                Edited(first, linesChanged: many);
                return;
            }
            var from = caretPos;
            var to = forward ? (word ? WordRight(caretPos) : Right(caretPos)) : (word ? WordLeft(caretPos) : Left(caretPos));
            if (to == from) return;
            Remember();
            var start = Min(from, to);
            bool linesChanged = from.Line != to.Line;
            RemoveRange(start, Max(from, to));
            caretPos = anchor = start;
            wantedColumn = -1;
            Edited(start.Line, linesChanged);
        }

        /// <summary>Puts text in at the caret, over the selection: pasted text keeps its lines, with tabs as spaces.</summary>
        public void Paste(string text)
        {
            text = Clean(text);
            if (text.Length == 0) return;
            Remember();
            DeleteSelection();
            var start = caretPos;
            caretPos = anchor = InsertRange(start, text);
            wantedColumn = -1;
            Edited(start.Line, linesChanged: text.IndexOf('\n') >= 0);
        }

        public void Copy()
        {
            GUIUtility.systemCopyBuffer = HasSelection ? SelectedText : lines[caretPos.Line] + "\n";
        }

        public void Cut()
        {
            if (!HasSelection)
            {
                // Like most code editors: with nothing selected, the whole line.
                GUIUtility.systemCopyBuffer = lines[caretPos.Line] + "\n";
                Remember();
                if (lines.Count == 1)
                {
                    lines[0] = "";
                    caretPos = anchor = new Pos(0, 0);
                    Edited(0, linesChanged: false);
                    return;
                }
                int line = caretPos.Line;
                RemoveLine(line);
                line = Math.Min(line, lines.Count - 1);
                caretPos = anchor = new Pos(line, Math.Min(caretPos.Col, lines[line].Length));
                Edited(line, linesChanged: true);
                return;
            }
            GUIUtility.systemCopyBuffer = SelectedText;
            Erase(forward: false, word: false);
        }

        /// <summary>Tab and Shift+Tab over a selection of lines: each line in or out by one indent.</summary>
        void IndentLines(bool outward)
        {
            Remember();
            var first = Min(caretPos, anchor);
            var last = Max(caretPos, anchor);
            int end = last.Col == 0 && last.Line > first.Line ? last.Line - 1 : last.Line;
            int caretShift = 0, anchorShift = 0;
            for (int i = first.Line; i <= end; i++)
            {
                int change;
                if (outward)
                {
                    int spaces = lines[i].Length - lines[i].TrimStart(' ').Length;
                    change = -Math.Min(Indent, spaces);
                    lines[i] = lines[i].Substring(-change);
                }
                else
                {
                    change = lines[i].Length == 0 ? 0 : Indent;
                    lines[i] = new string(' ', change) + lines[i];
                }
                if (i == caretPos.Line) caretShift = change;
                if (i == anchor.Line) anchorShift = change;
            }
            caretPos = new Pos(caretPos.Line, Math.Max(0, caretPos.Col + caretShift));
            anchor = new Pos(anchor.Line, Math.Max(0, anchor.Col + anchorShift));
            for (int i = first.Line; i <= end; i++) rich[i] = null;
            Edited(first.Line, linesChanged: true);
        }

        public void Undo() => Step(undo, redo);

        public void Redo() => Step(redo, undo);

        void Step(List<Snapshot> from, List<Snapshot> to)
        {
            if (from.Count == 0) return;
            to.Add(Take());
            var s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            LoadLines(s.Text);
            caretPos = s.Caret;
            anchor = s.Anchor;
            typingRun = "";
            wantedColumn = -1;
            list.RefreshItems();
            RevealCaret();
        }

        /// <summary>Keeps the text as it is before a change; a new change forgets what could be redone.</summary>
        void Remember()
        {
            undo.Add(Take());
            if (undo.Count > 300) undo.RemoveAt(0);
            redo.Clear();
            typingRun = "";
        }

        Snapshot Take() => new Snapshot { Text = Text, Caret = caretPos, Anchor = anchor };

        void DeleteSelection()
        {
            if (!HasSelection) return;
            var start = Min(caretPos, anchor);
            RemoveRange(start, Max(caretPos, anchor));
            caretPos = anchor = start;
        }

        void RemoveRange(Pos start, Pos end)
        {
            string head = lines[start.Line].Substring(0, start.Col);
            string tail = lines[end.Line].Substring(end.Col);
            for (int i = end.Line; i > start.Line; i--) RemoveLine(i);
            lines[start.Line] = head + tail;
        }

        /// <summary>Puts text (with \n line ends) in at a place and returns where it ends.</summary>
        Pos InsertRange(Pos at, string text)
        {
            var parts = text.Split('\n');
            string head = lines[at.Line].Substring(0, at.Col);
            string tail = lines[at.Line].Substring(at.Col);
            if (parts.Length == 1)
            {
                lines[at.Line] = head + text + tail;
                return new Pos(at.Line, at.Col + text.Length);
            }
            lines[at.Line] = head + parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                lines.Insert(at.Line + i, parts[i] + (i == parts.Length - 1 ? tail : ""));
                rich.Insert(at.Line + i, null);
                commentBefore.Insert(at.Line + i, false);
            }
            return new Pos(at.Line + parts.Length - 1, parts[parts.Length - 1].Length);
        }

        string TextBetween(Pos start, Pos end)
        {
            if (start.Line == end.Line) return lines[start.Line].Substring(start.Col, end.Col - start.Col);
            var text = new StringBuilder(lines[start.Line].Substring(start.Col));
            for (int i = start.Line + 1; i < end.Line; i++) text.Append('\n').Append(lines[i]);
            text.Append('\n').Append(lines[end.Line].Substring(0, end.Col));
            return text.ToString();
        }

        void RemoveLine(int index)
        {
            lines.RemoveAt(index);
            rich.RemoveAt(index);
            commentBefore.RemoveAt(index);
        }

        void Edited(int firstChanged, bool linesChanged)
        {
            firstChanged = Mathf.Clamp(firstChanged, 0, lines.Count - 1);
            rich[firstChanged] = null;
            Recolour(firstChanged, all: false);
            if (linesChanged) list.RefreshItems();
            else list.RefreshItem(firstChanged);
            RevealCaret();
        }

        /// <summary>Re-colours from <paramref name="start"/> until the comment state stops changing.</summary>
        void Recolour(int start, bool all)
        {
            bool state = start > 0 ? CommentAfter(start - 1) : false;
            LastRecolourCount = 0;
            for (int i = start; i < lines.Count; i++)
            {
                if (!all && i > start && commentBefore[i] == state && rich[i] != null) break;
                LastRecolourCount++;
                commentBefore[i] = state;
                rich[i] = CppHighlighter.Highlight(lines[i], ref state, builder);
                if (!all && i > start) list.RefreshItem(i); // an opened or closed /* changed this line too
            }
        }

        bool CommentAfter(int index)
        {
            bool state = commentBefore[index];
            CppHighlighter.Highlight(lines[index], ref state, builder);
            return state;
        }

        // ------------------------------------------------------------------ places in the text

        static Pos Min(Pos a, Pos b) => a.CompareTo(b) <= 0 ? a : b;
        static Pos Max(Pos a, Pos b) => a.CompareTo(b) >= 0 ? a : b;

        Pos Left(Pos p) => p.Col > 0 ? new Pos(p.Line, p.Col - 1) : p.Line > 0 ? new Pos(p.Line - 1, lines[p.Line - 1].Length) : p;

        Pos Right(Pos p) => p.Col < lines[p.Line].Length ? new Pos(p.Line, p.Col + 1) : p.Line < lines.Count - 1 ? new Pos(p.Line + 1, 0) : p;

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Back to the start of the word before the caret (over spaces first), or over a line end.</summary>
        Pos WordLeft(Pos p)
        {
            if (p.Col == 0) return Left(p);
            string line = lines[p.Line];
            int i = p.Col;
            while (i > 0 && line[i - 1] == ' ') i--;
            if (i > 0 && IsWord(line[i - 1])) while (i > 0 && IsWord(line[i - 1])) i--;
            else if (i > 0) i--;
            return new Pos(p.Line, i);
        }

        /// <summary>On to the end of the word after the caret (over spaces after it), or over a line end.</summary>
        Pos WordRight(Pos p)
        {
            string line = lines[p.Line];
            if (p.Col >= line.Length) return Right(p);
            int i = p.Col;
            if (IsWord(line[i])) while (i < line.Length && IsWord(line[i])) i++;
            else i++;
            while (i < line.Length && line[i] == ' ') i++;
            return new Pos(p.Line, i);
        }

        /// <summary>The word (or run of spaces, or single sign) round a place, for a double-click.</summary>
        (Pos start, Pos end) WordAt(Pos p)
        {
            string line = lines[p.Line];
            if (line.Length == 0) return (p, p);
            int i = Mathf.Clamp(p.Col, 0, line.Length - 1);
            if (p.Col == line.Length && i > 0) i--;
            Func<char, bool> same;
            if (IsWord(line[i])) same = IsWord;
            else if (line[i] == ' ') same = c => c == ' ';
            else same = c => false;
            int start = i, end = i + 1;
            if (same(line[i]))
            {
                while (start > 0 && same(line[start - 1])) start--;
                while (end < line.Length && same(line[end])) end++;
            }
            return (new Pos(p.Line, start), new Pos(p.Line, end));
        }

        // ------------------------------------------------------------------ drawing

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("code-row");
            var number = new Label { pickingMode = PickingMode.Ignore };
            number.AddToClassList("code-gutter");
            var text = new Label { enableRichText = true, pickingMode = PickingMode.Ignore };
            text.AddToClassList("code-text");
            if (font.HasValue)
            {
                number.style.unityFontDefinition = font.Value;
                text.style.unityFontDefinition = font.Value;
            }
            row.Add(number);
            row.Add(text);
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            ((Label)row[0]).text = (index + 1).ToString();
            ((Label)row[1]).text = rich[index] ?? lines[index];
        }

        void MeasureCharWidth()
        {
            if (measure.panel == null) return;
            float width = measure.MeasureTextSize(measure.text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
            if (width > 0) charWidth = width / measure.text.Length;
        }

        int VisibleLines => Mathf.Max(1, (int)(list.resolvedStyle.height / LineHeight) - 1);

        void RevealCaret()
        {
            float top = caretPos.Line * LineHeight;
            float scroll = ScrollView.scrollOffset.y;
            float height = list.resolvedStyle.height;
            if (top < scroll) ScrollView.scrollOffset = new Vector2(0, top);
            else if (top + LineHeight > scroll + height) ScrollView.scrollOffset = new Vector2(0, top + LineHeight - height);
            blinkOn = true;
            UpdateCaret();
            selectionLayer.MarkDirtyRepaint();
            CaretMoved?.Invoke();
        }

        void UpdateCaret()
        {
            float y = caretPos.Line * LineHeight - ScrollView.scrollOffset.y;
            caret.style.left = TextLeft + caretPos.Col * charWidth;
            caret.style.top = y;
            caret.visible = focused && blinkOn && y > -LineHeight && y < list.resolvedStyle.height;
        }

        /// <summary>The selection, a band per visible line; a selected line end shows as a little more.</summary>
        void DrawSelection(MeshGenerationContext context)
        {
            if (!HasSelection) return;
            var start = Min(caretPos, anchor);
            var end = Max(caretPos, anchor);
            float scroll = ScrollView.scrollOffset.y, height = list.resolvedStyle.height;
            int first = Mathf.Max(start.Line, (int)(scroll / LineHeight));
            int last = Mathf.Min(end.Line, (int)((scroll + height) / LineHeight) + 1);
            var p = context.painter2D;
            p.fillColor = focused ? new Color(0.15f, 0.31f, 0.47f) : new Color(0.23f, 0.24f, 0.27f);
            for (int line = first; line <= last; line++)
            {
                int from = line == start.Line ? start.Col : 0;
                float to = line == end.Line ? end.Col : lines[line].Length + 0.6f;
                float x0 = TextLeft + from * charWidth, x1 = TextLeft + to * charWidth, y = line * LineHeight - scroll;
                if (x1 - x0 < 1) continue;
                p.BeginPath();
                p.MoveTo(new Vector2(x0, y));
                p.LineTo(new Vector2(x1, y));
                p.LineTo(new Vector2(x1, y + LineHeight));
                p.LineTo(new Vector2(x0, y + LineHeight));
                p.ClosePath();
                p.Fill();
            }
        }

        // ------------------------------------------------------------------ mouse

        Pos PosAt(Vector2 local)
        {
            int line = Mathf.FloorToInt((local.y + ScrollView.scrollOffset.y) / LineHeight);
            int column = Mathf.RoundToInt((local.x - TextLeft) / charWidth);
            return ClampPos(line, column);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.target is VisualElement target && target.GetFirstAncestorOfType<Scroller>() != null) return; // scrollbar
            if (menu != null && e.target is VisualElement t && (t == menu || menu.Contains(t))) return;
            CloseMenu();
            Focus();
            var at = PosAt(this.WorldToLocal(e.position));
            if (e.button == 1)
            {
                // A right-click inside the selection keeps it for the menu; elsewhere it moves the caret first.
                if (!HasSelection || at.CompareTo(Min(caretPos, anchor)) < 0 || at.CompareTo(Max(caretPos, anchor)) > 0) MoveTo(at, extend: false);
                OpenMenu(e.position);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            clickKind = Mathf.Clamp(e.clickCount, 1, 3);
            if (clickKind == 1)
            {
                MoveTo(at, extend: e.shiftKey);
            }
            else
            {
                (wordStart, wordEnd) = clickKind == 2 ? WordAt(at) : LineAt(at.Line);
                anchor = wordStart;
                caretPos = wordEnd;
                wantedColumn = -1;
                RevealCaret();
            }
            selecting = true;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        (Pos, Pos) LineAt(int line) => (new Pos(line, 0), line < lines.Count - 1 ? new Pos(line + 1, 0) : new Pos(line, lines[line].Length));

        Vector2 lastPointer;

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!selecting) return;
            lastPointer = this.WorldToLocal(e.position);
            ExtendTo(PosAt(lastPointer));
        }

        /// <summary>A drag after a click selects by characters; after a double or triple click by words or lines.</summary>
        void ExtendTo(Pos at)
        {
            if (clickKind == 1)
            {
                caretPos = at;
            }
            else
            {
                var (start, end) = clickKind == 2 ? WordAt(at) : LineAt(at.Line);
                bool before = at.CompareTo(wordStart) < 0;
                anchor = before ? wordEnd : wordStart;
                caretPos = before ? start : Max(end, wordEnd);
            }
            wantedColumn = -1;
            RevealCaret();
        }

        void ScrollWhileSelecting()
        {
            if (!selecting) return;
            float height = list.resolvedStyle.height;
            if (lastPointer.y >= 0 && lastPointer.y <= height) return;
            float step = lastPointer.y < 0 ? -LineHeight : LineHeight;
            ScrollView.scrollOffset = new Vector2(0, Mathf.Max(0, ScrollView.scrollOffset.y + step));
            ExtendTo(PosAt(lastPointer));
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!selecting) return;
            selecting = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        // ------------------------------------------------------------------ right-click menu

        /// <summary>Opens the right-click menu at a panel position, as a right-click there does (for the benchmark).</summary>
        public void ShowMenu(Vector2 at) => OpenMenu(at);

        public void HideMenu() => CloseMenu();

        void OpenMenu(Vector2 at)
        {
            // On the highest element that carries the screen's style sheets (the Garage's or the arena's root, not
            // the panel's own root, which has only the theme), so it is above everything and styled.
            VisualElement? top = null;
            for (var e = parent; e != null && e.parent != null; e = e.parent)
                if (e.styleSheets.count > 0) top = e;
            if (top == null) return;
            menu = new VisualElement();
            menu.AddToClassList("code-menu");
            void Item(string key, string keys, bool enabled, Action action)
            {
                var item = new Button(() =>
                {
                    CloseMenu();
                    Focus();
                    action();
                }) { focusable = false };
                item.AddToClassList("code-menu-item");
                item.SetEnabled(enabled);
                item.Add(new Label(SpikeStrings.Get(key)) { pickingMode = PickingMode.Ignore });
                var shortcut = new Label(keys) { pickingMode = PickingMode.Ignore };
                shortcut.AddToClassList("code-menu-keys");
                item.Add(shortcut);
                menu.Add(item);
            }
            Item("editor.undo", "Ctrl+Z", undo.Count > 0, Undo);
            Item("editor.redo", "Ctrl+Y", redo.Count > 0, Redo);
            Item("editor.cut", "Ctrl+X", true, Cut);
            Item("editor.copy", "Ctrl+C", true, Copy);
            Item("editor.paste", "Ctrl+V", GUIUtility.systemCopyBuffer.Length > 0, () => Paste(GUIUtility.systemCopyBuffer));
            Item("editor.selectAll", "Ctrl+A", true, SelectAll);
            var local = top.WorldToLocal(at);
            menu.style.left = Mathf.Min(local.x, top.layout.width - 190);
            menu.style.top = Mathf.Min(local.y, top.layout.height - 170);
            top.Add(menu);
            // A click anywhere else closes it.
            top.RegisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);
        }

        void OnPanelPointerDown(PointerDownEvent e)
        {
            if (menu != null && e.target is VisualElement t && (t == menu || menu.Contains(t))) return;
            CloseMenu();
        }

        void CloseMenu()
        {
            if (menu == null) return;
            menu.parent?.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);
            menu.RemoveFromHierarchy();
            menu = null;
        }

        // ------------------------------------------------------------------ keys

        void OnKeyDown(KeyDownEvent e)
        {
            bool handled = true;
            bool shift = e.shiftKey, ctrl = e.ctrlKey || e.commandKey;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    if (HasSelection && !shift) MoveTo(Min(caretPos, anchor), false);
                    else MoveTo(ctrl ? WordLeft(caretPos) : Left(caretPos), shift);
                    break;
                case KeyCode.RightArrow:
                    if (HasSelection && !shift) MoveTo(Max(caretPos, anchor), false);
                    else MoveTo(ctrl ? WordRight(caretPos) : Right(caretPos), shift);
                    break;
                case KeyCode.UpArrow: Vertical(-1, shift); break;
                case KeyCode.DownArrow: Vertical(1, shift); break;
                case KeyCode.PageUp: Vertical(-VisibleLines, shift); break;
                case KeyCode.PageDown: Vertical(VisibleLines, shift); break;
                case KeyCode.Home:
                    if (ctrl) MoveTo(new Pos(0, 0), shift);
                    else
                    {
                        // The first press goes to the line's code, the next to its very start.
                        int code = lines[caretPos.Line].Length - lines[caretPos.Line].TrimStart(' ').Length;
                        MoveTo(new Pos(caretPos.Line, caretPos.Col == code ? 0 : code), shift);
                    }
                    break;
                case KeyCode.End:
                    MoveTo(ctrl ? new Pos(lines.Count - 1, lines[lines.Count - 1].Length) : new Pos(caretPos.Line, lines[caretPos.Line].Length), shift);
                    break;
                case KeyCode.Backspace: Erase(forward: false, word: ctrl); break;
                case KeyCode.Delete:
                    if (shift && !ctrl) Cut();
                    else Erase(forward: true, word: ctrl);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: NewLine(); break;
                case KeyCode.Tab:
                    if (shift || caretPos.Line != anchor.Line) IndentLines(outward: shift);
                    else Insert(new string(' ', Indent));
                    break;
                case KeyCode.Insert:
                    if (ctrl) Copy();
                    else if (shift) Paste(GUIUtility.systemCopyBuffer);
                    else handled = false;
                    break;
                case KeyCode.Escape:
                    handled = false; // the window's Escape closes it
                    CloseMenu();
                    break;
                default:
                    handled = ctrl && !e.altKey && Shortcut(e.keyCode, shift);
                    break;
            }

            // Typed characters arrive as a second KeyDownEvent with keyCode None. Ctrl+Alt is AltGr on many layouts.
            if (!handled && e.character >= ' ' && e.character != (char)127 && (!ctrl || e.altKey))
            {
                Insert(e.character.ToString());
                handled = true;
            }

            if (handled)
            {
                e.StopPropagation();
                focusController?.IgnoreEvent(e);
            }
        }

        bool Shortcut(KeyCode key, bool shift)
        {
            switch (key)
            {
                case KeyCode.A: SelectAll(); return true;
                case KeyCode.C: Copy(); return true;
                case KeyCode.X: Cut(); return true;
                case KeyCode.V: Paste(GUIUtility.systemCopyBuffer); return true;
                case KeyCode.Z:
                    if (shift) Redo();
                    else Undo();
                    return true;
                case KeyCode.Y: Redo(); return true;
                default: return false;
            }
        }

        /// <summary>Up or down by lines, keeping to the column the caret started from.</summary>
        void Vertical(int by, bool extend)
        {
            if (wantedColumn < 0) wantedColumn = caretPos.Col;
            int line = Mathf.Clamp(caretPos.Line + by, 0, lines.Count - 1);
            MoveTo(new Pos(line, Mathf.Min(wantedColumn, lines[line].Length)), extend, keepColumn: true);
        }
    }
}
