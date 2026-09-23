using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.UI
{
    /// <summary>
    /// Phase 0.6 spike of the custom code editor (docs/04 §code editor, docs/13 Q6): a virtualised
    /// ListView of rich-text lines with line numbers, a caret, typing, auto-indent and syntax colours.
    /// Only visible lines exist as elements, so a long sketch scrolls as cheaply as a short one.
    /// Not yet: selection, clipboard, undo, find, bracket matching, horizontal scrolling.
    /// </summary>
    public sealed class CodeEditor : VisualElement
    {
        public const float LineHeight = 18f;
        const float TextLeft = 54f; // gutter width 48 + code padding 6, see UiSpike.uss

        /// <summary>True while an editor has keyboard focus, so game shortcuts stay quiet.</summary>
        public static bool HasTypingFocus { get; private set; }

        readonly List<string> lines = new List<string> { "" };
        readonly List<string?> rich = new List<string?> { null };
        readonly List<bool> commentBefore = new List<bool> { false };
        readonly StringBuilder builder = new StringBuilder();
        readonly ListView list;
        readonly VisualElement caret;
        readonly Label measure;
        FontDefinition? font;
        float charWidth = 7.7f;
        int caretLine;
        int caretColumn;
        bool focused;
        bool blinkOn = true;

        public event Action? CaretMoved;

        public int LineCount => lines.Count;
        /// <summary>Lines re-coloured by the last edit (the edited line plus any whose comment state changed).</summary>
        public int LastRecolourCount { get; private set; }
        public int CaretLine => caretLine;
        public int CaretColumn => caretColumn;
        public ScrollView ScrollView => list.Q<ScrollView>();

        public CodeEditor()
        {
            AddToClassList("code-editor");
            focusable = true;

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

            ScrollView.verticalScroller.valueChanged += _ => UpdateCaret();
            RegisterCallback<GeometryChangedEvent>(_ => { MeasureCharWidth(); UpdateCaret(); });
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            // Arrow keys, Tab and Enter must edit the text instead of moving focus to another control.
            RegisterCallback<NavigationMoveEvent>(e => { e.StopPropagation(); focusController?.IgnoreEvent(e); });
            RegisterCallback<NavigationSubmitEvent>(e => { e.StopPropagation(); focusController?.IgnoreEvent(e); });
            RegisterCallback<FocusInEvent>(_ => { focused = true; HasTypingFocus = true; UpdateCaret(); });
            RegisterCallback<FocusOutEvent>(_ => { focused = false; HasTypingFocus = false; UpdateCaret(); });
            schedule.Execute(() => { blinkOn = !blinkOn; UpdateCaret(); }).Every(530);
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
            lines.Clear();
            rich.Clear();
            commentBefore.Clear();
            foreach (string line in text.Replace("\r\n", "\n").Replace("\t", "  ").Split('\n'))
            {
                lines.Add(line);
                rich.Add(null);
                commentBefore.Add(false);
            }
            Recolour(0, all: true);
            caretLine = caretColumn = 0;
            list.RefreshItems();
            list.ScrollToItem(0);
            UpdateCaret();
        }

        public void MoveCaret(int line, int column)
        {
            caretLine = Mathf.Clamp(line, 0, lines.Count - 1);
            caretColumn = Mathf.Clamp(column, 0, lines[caretLine].Length);
            RevealCaret();
        }

        /// <summary>Inserts typed text at the caret (no newlines; Enter goes through <see cref="NewLine"/>).</summary>
        public void Insert(string text)
        {
            string line = lines[caretLine];
            // Typing '}' on a line of only spaces removes one indent level, like most code editors.
            if (text == "}" && caretColumn >= 2 && line.Substring(0, caretColumn).Trim().Length == 0)
            {
                line = line.Remove(caretColumn - 2, 2);
                caretColumn -= 2;
            }
            lines[caretLine] = line.Insert(caretColumn, text);
            caretColumn += text.Length;
            Edited(caretLine, linesChanged: false);
        }

        public void NewLine()
        {
            string line = lines[caretLine];
            string head = line.Substring(0, caretColumn);
            string tail = line.Substring(caretColumn).TrimStart(' ');
            string indent = new string(' ', head.Length - head.TrimStart(' ').Length);
            if (head.TrimEnd().EndsWith("{", StringComparison.Ordinal)) indent += "  ";
            lines[caretLine] = head;
            lines.Insert(caretLine + 1, indent + tail);
            rich.Insert(caretLine + 1, null);
            commentBefore.Insert(caretLine + 1, false);
            caretLine++;
            caretColumn = indent.Length;
            Edited(caretLine - 1, linesChanged: true);
        }

        public void Backspace()
        {
            if (caretColumn > 0)
            {
                lines[caretLine] = lines[caretLine].Remove(caretColumn - 1, 1);
                caretColumn--;
                Edited(caretLine, linesChanged: false);
            }
            else if (caretLine > 0)
            {
                caretColumn = lines[caretLine - 1].Length;
                lines[caretLine - 1] += lines[caretLine];
                RemoveLine(caretLine);
                caretLine--;
                Edited(caretLine, linesChanged: true);
            }
        }

        public void DeleteForward()
        {
            if (caretColumn < lines[caretLine].Length)
            {
                lines[caretLine] = lines[caretLine].Remove(caretColumn, 1);
                Edited(caretLine, linesChanged: false);
            }
            else if (caretLine < lines.Count - 1)
            {
                lines[caretLine] += lines[caretLine + 1];
                RemoveLine(caretLine + 1);
                Edited(caretLine, linesChanged: true);
            }
        }

        void RemoveLine(int index)
        {
            lines.RemoveAt(index);
            rich.RemoveAt(index);
            commentBefore.RemoveAt(index);
        }

        void Edited(int firstChanged, bool linesChanged)
        {
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
            float top = caretLine * LineHeight;
            float scroll = ScrollView.scrollOffset.y;
            float height = list.resolvedStyle.height;
            if (top < scroll) ScrollView.scrollOffset = new Vector2(0, top);
            else if (top + LineHeight > scroll + height) ScrollView.scrollOffset = new Vector2(0, top + LineHeight - height);
            blinkOn = true;
            UpdateCaret();
            CaretMoved?.Invoke();
        }

        void UpdateCaret()
        {
            float y = caretLine * LineHeight - ScrollView.scrollOffset.y;
            caret.style.left = TextLeft + caretColumn * charWidth;
            caret.style.top = y;
            caret.visible = focused && blinkOn && y > -LineHeight && y < list.resolvedStyle.height;
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.target is VisualElement target && target.GetFirstAncestorOfType<Scroller>() != null) return; // scrollbar
            Focus();
            Vector2 local = this.WorldToLocal(e.position);
            int line = Mathf.FloorToInt((local.y + ScrollView.scrollOffset.y) / LineHeight);
            int column = Mathf.RoundToInt((local.x - TextLeft) / charWidth);
            MoveCaret(line, column);
        }

        void OnKeyDown(KeyDownEvent e)
        {
            bool handled = true;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    if (caretColumn > 0) caretColumn--;
                    else if (caretLine > 0) { caretLine--; caretColumn = lines[caretLine].Length; }
                    RevealCaret();
                    break;
                case KeyCode.RightArrow:
                    if (caretColumn < lines[caretLine].Length) caretColumn++;
                    else if (caretLine < lines.Count - 1) { caretLine++; caretColumn = 0; }
                    RevealCaret();
                    break;
                case KeyCode.UpArrow: MoveCaret(caretLine - 1, caretColumn); break;
                case KeyCode.DownArrow: MoveCaret(caretLine + 1, caretColumn); break;
                case KeyCode.PageUp: MoveCaret(caretLine - VisibleLines, caretColumn); break;
                case KeyCode.PageDown: MoveCaret(caretLine + VisibleLines, caretColumn); break;
                case KeyCode.Home:
                    if (e.ctrlKey) MoveCaret(0, 0);
                    else MoveCaret(caretLine, lines[caretLine].Length - lines[caretLine].TrimStart(' ').Length);
                    break;
                case KeyCode.End:
                    MoveCaret(e.ctrlKey ? lines.Count - 1 : caretLine, int.MaxValue);
                    break;
                case KeyCode.Backspace: Backspace(); break;
                case KeyCode.Delete: DeleteForward(); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: NewLine(); break;
                case KeyCode.Tab: Insert("  "); break;
                default: handled = false; break;
            }

            // Typed characters arrive as a second KeyDownEvent with keyCode None. Ctrl+Alt is AltGr on many layouts.
            if (!handled && e.character >= ' ' && e.character != (char)127 && (!e.ctrlKey || e.altKey))
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
    }
}
