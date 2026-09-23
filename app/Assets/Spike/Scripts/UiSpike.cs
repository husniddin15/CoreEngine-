using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CoreEngine.Spike.UI;
using UnityEngine;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace CoreEngine.Spike
{
    /// <summary>
    /// Phase 0.6 spike (docs/11-roadmap.md §3): UI Toolkit panels docked around the running 3D robot view,
    /// a custom code editor with a 500+ line sketch, and a live switch between English, Uzbek and Russian.
    /// Fonts come from Windows at run time (Segoe UI, Consolas), so nothing is redistributed yet.
    /// Throwaway prototype, not the Phase 1 UI.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiSpike : MonoBehaviour
    {
        public StyleSheet? styleSheet;
        public RobotSpike? robot;
        public string sketchFile = "ObstacleAvoider.ino";

        VisualElement root = null!;
        VisualElement viewport = null!;
        CodeEditor editor = null!;
        DockArea leftDock = null!, rightDock = null!, bottomDock = null!;
        DockPanel serialPanel = null!;
        Label fpsLabel = null!, codeStatus = null!, codeFile = null!;
        readonly List<(TextElement element, string key)> localized = new List<(TextElement, string)>();
        readonly List<Button> languageButtons = new List<Button>();
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        FontAsset? uiFont, codeFont;
        double fontPreloadMs;

        readonly List<string> serialLines = new List<string>();
        readonly List<string> pendingSerial = new List<string>();
        ListView serialView = null!;
        Toggle autoscroll = null!;
        readonly List<string> events = new List<string>();
        ListView eventView = null!;
        bool leftStalled, rightStalled;

        float fpsTimer, refreshTimer;
        int fpsFrames;

        public CodeEditor Editor => editor;

        public bool Visible
        {
            get => root.style.display != DisplayStyle.None;
            set
            {
                root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (robot != null) robot.ShowHud = !value;
                UpdateCameraRect();
            }
        }

        void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            root = new VisualElement();
            root.AddToClassList("spike-root");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            document.rootVisualElement.Add(root);

            BuildLayout();
            LoadFonts();
            LoadSketch();
            SpikeStrings.LanguageChanged += ApplyLanguage;
            ApplyLanguage();
            if (robot != null)
            {
                robot.SerialLine += OnSerialLine;
                robot.ShowHud = false;
            }
        }

        void OnDisable()
        {
            SpikeStrings.LanguageChanged -= ApplyLanguage;
            if (robot != null) robot.SerialLine -= OnSerialLine;
        }

        // ------------------------------------------------------------------ layout

        void BuildLayout()
        {
            var top = new VisualElement();
            top.AddToClassList("top-bar");
            var title = new Label("CoreEngine");
            title.AddToClassList("app-title");
            top.Add(title);
            foreach (string mode in new[] { "mode.build", "mode.wire", "mode.code", "mode.test" })
            {
                var tab = Localized(new Label(), mode);
                tab.AddToClassList("mode-tab");
                tab.EnableInClassList("mode-tab--active", mode == "mode.code");
                top.Add(tab);
            }
            top.Add(Spacer());
            var hint = Localized(new Label(), "ui.hint");
            hint.AddToClassList("hint");
            top.Add(hint);
            for (int i = 0; i < SpikeStrings.LanguageButtons.Length; i++)
            {
                int language = i;
                var button = new Button(() => SpikeStrings.SetLanguage(language)) { text = SpikeStrings.LanguageButtons[i], focusable = false };
                button.AddToClassList("lang-button");
                languageButtons.Add(button);
                top.Add(button);
            }
            fpsLabel = new Label();
            fpsLabel.AddToClassList("fps");
            top.Add(fpsLabel);
            root.Add(top);

            leftDock = new DockArea("left", SpikeStrings.Get);
            rightDock = new DockArea("right", SpikeStrings.Get);
            bottomDock = new DockArea("bottom", SpikeStrings.Get);

            viewport = new VisualElement { pickingMode = PickingMode.Ignore };
            viewport.AddToClassList("viewport");
            viewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateCameraRect());
            var centre = new TwoPaneSplitView(1, 190, TwoPaneSplitViewOrientation.Vertical);
            centre.Add(viewport);
            centre.Add(bottomDock);
            var rest = new TwoPaneSplitView(1, 290, TwoPaneSplitViewOrientation.Horizontal);
            rest.Add(centre);
            rest.Add(rightDock);
            var main = new TwoPaneSplitView(0, 540, TwoPaneSplitViewOrientation.Horizontal);
            main.AddToClassList("main-split");
            main.Add(leftDock);
            main.Add(rest);
            root.Add(main);

            leftDock.AddPanel(new DockPanel("panel.code", BuildCodePanel()));
            rightDock.AddPanel(new DockPanel("panel.inspector", BuildInspector()));
            var console = new DockPanel("panel.console", BuildConsole());
            serialPanel = new DockPanel("panel.serial", BuildSerialMonitor());
            bottomDock.AddPanel(console);
            bottomDock.AddPanel(serialPanel);
            bottomDock.AddPanel(new DockPanel("panel.events", BuildEventLog()));
            bottomDock.Select(serialPanel);
        }

        VisualElement BuildCodePanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList("code-panel");
            codeFile = new Label(sketchFile);
            var header = new VisualElement();
            header.AddToClassList("code-header");
            header.Add(codeFile);
            panel.Add(header);
            editor = new CodeEditor();
            editor.CaretMoved += UpdateCodeStatus;
            panel.Add(editor);
            codeStatus = new Label();
            codeStatus.AddToClassList("code-status");
            panel.Add(codeStatus);
            return panel;
        }

        VisualElement BuildInspector()
        {
            var panel = new ScrollView();
            panel.AddToClassList("inspector");
            Section(panel, "insp.sensor");
            Row(panel, "insp.distance");
            Row(panel, "insp.measurements");
            Section(panel, "insp.driver");
            Row(panel, "insp.supply");
            Row(panel, "insp.leftMotor");
            Row(panel, "insp.rightMotor");
            Row(panel, "insp.wheelSpeed");
            Section(panel, "insp.board");
            Row(panel, "insp.emulatedTime");
            Row(panel, "insp.emulatorLoad");
            return panel;
        }

        void Section(VisualElement parent, string key)
        {
            var label = Localized(new Label(), key);
            label.AddToClassList("insp-section");
            parent.Add(label);
        }

        void Row(VisualElement parent, string key)
        {
            var row = new VisualElement();
            row.AddToClassList("insp-row");
            var label = Localized(new Label(), key);
            label.AddToClassList("insp-label");
            var value = new Label("-");
            value.AddToClassList("insp-value");
            row.Add(label);
            row.Add(value);
            parent.Add(row);
            values[key] = value;
        }

        VisualElement BuildConsole()
        {
            var panel = new ScrollView();
            panel.AddToClassList("console");
            // Compiler output stays in English in every language (docs/10 §5).
            var ok = new Label("Sketch uses 2954 bytes (9%) of program storage space. Maximum is 32256 bytes.");
            ok.AddToClassList("console-line");
            panel.Add(ok);
            var example = Localized(new Label(), "console.example");
            example.AddToClassList("console-line");
            panel.Add(example);
            var error = new Label("ObstacleAvoider.ino:44:3: error: expected ';' before '}' token");
            error.AddToClassList("console-line");
            error.AddToClassList("console-error");
            panel.Add(error);
            var card = new VisualElement();
            card.AddToClassList("help-card");
            var cardTitle = Localized(new Label(), "console.helpTitle");
            cardTitle.AddToClassList("help-title");
            var cardText = Localized(new Label(), "console.helpText");
            cardText.AddToClassList("help-text");
            card.Add(cardTitle);
            card.Add(cardText);
            panel.Add(card);
            return panel;
        }

        VisualElement BuildSerialMonitor()
        {
            var panel = new VisualElement();
            var toolbar = new VisualElement();
            toolbar.AddToClassList("panel-toolbar");
            var baud = new Label();
            values["serial.baud"] = baud;
            toolbar.Add(baud);
            autoscroll = new Toggle { value = true, focusable = false };
            toolbar.Add(autoscroll);
            panel.Add(toolbar);
            serialView = MonoList(serialLines);
            panel.Add(serialView);
            return panel;
        }

        VisualElement BuildEventLog()
        {
            eventView = MonoList(events);
            return eventView;
        }

        ListView MonoList(List<string> source)
        {
            var list = new ListView(source, 16, () =>
            {
                var label = new Label();
                label.AddToClassList("mono-line");
                if (codeFont != null) label.style.unityFontDefinition = FontDefinition.FromSDFFont(codeFont);
                return label;
            }, (element, index) => ((Label)element).text = source[index])
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                focusable = false,
            };
            list.AddToClassList("mono-list");
            return list;
        }

        T Localized<T>(T element, string key) where T : TextElement
        {
            localized.Add((element, key));
            element.text = SpikeStrings.Get(key);
            return element;
        }

        static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            return spacer;
        }

        void ApplyLanguage()
        {
            foreach (var (element, key) in localized) element.text = SpikeStrings.Get(key);
            for (int i = 0; i < languageButtons.Count; i++)
                languageButtons[i].EnableInClassList("lang-button--active", i == SpikeStrings.Language);
            autoscroll.text = SpikeStrings.Get("serial.autoscroll");
            leftDock.RefreshTitles();
            rightDock.RefreshTitles();
            bottomDock.RefreshTitles();
            UpdateCodeStatus();
            RefreshValues();
        }

        /// <summary>The 3D camera renders only into the free centre area, so the robot stays centred between panels.</summary>
        void UpdateCameraRect()
        {
            var camera = Camera.main;
            if (camera == null) return;
            Rect screen = root.panel != null ? root.panel.visualTree.worldBound : Rect.zero;
            Rect view = viewport.worldBound;
            if (!Visible || screen.width <= 0 || screen.height <= 0 || view.width <= 0 || view.height <= 0)
            {
                camera.rect = new Rect(0, 0, 1, 1);
                return;
            }
            camera.rect = new Rect(view.xMin / screen.width, 1 - view.yMax / screen.height,
                                   view.width / screen.width, view.height / screen.height);
        }

        // ------------------------------------------------------------------ fonts and sketch

        void LoadFonts()
        {
            uiFont = CreateOsFont("Segoe UI");
            codeFont = CreateOsFont("Consolas") ?? CreateOsFont("Cascadia Mono");
            // Render every needed glyph now: a glyph first met while scrolling costs a visible hitch.
            var watch = Stopwatch.StartNew();
            string charset = Charset();
            uiFont?.TryAddCharacters(charset, false);
            codeFont?.TryAddCharacters(charset, false);
            fontPreloadMs = watch.Elapsed.TotalMilliseconds;
            if (uiFont != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(uiFont);
            if (codeFont != null)
            {
                editor.SetFont(FontDefinition.FromSDFFont(codeFont));
                serialView.Rebuild();
                eventView.Rebuild();
            }
        }

        /// <summary>Every character the UI and code need: ASCII, Russian, Uzbek Latin marks, and symbols.</summary>
        static string Charset()
        {
            var sb = new StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            for (char c = 'А'; c <= 'я'; c++) sb.Append(c);
            sb.Append("Ёёʻʼ‘’«»—–…№°±×→←≥≤µΩ•≈");
            return sb.ToString();
        }

        static FontAsset? CreateOsFont(string family)
        {
            try
            {
                var font = FontAsset.CreateFontAsset(family, "Regular", 90);
                if (font != null) font.name = family;
                return font;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UiSpike: font {family} is not available: {e.Message}");
                return null;
            }
        }

        void LoadSketch()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Sketches", sketchFile);
            string source = File.Exists(path) ? File.ReadAllText(path) : "// " + sketchFile + " not found\n";
            editor.SetText(LongSketch(source, 520));
        }

        /// <summary>The real sketch followed by generated helpers, so the editor holds 500+ lines.</summary>
        static string LongSketch(string source, int minLines)
        {
            var sb = new StringBuilder(source.Replace("\r\n", "\n").TrimEnd('\n')).Append('\n');
            sb.Append("\n// ---- Generated section for the Phase 0.6 editor test (not part of the golden sketch) ----\n");
            sb.Append("// Комментарий по-русски / Oʻzbekcha izoh: tezlik va gʻildiraklar\n");
            sb.Append("const int ENA = 3;   // PWM speed pin\n");
            int lineCount = sb.ToString().Split('\n').Length;
            for (int n = 1; lineCount < minLines; n++)
            {
                sb.Append('\n');
                sb.Append($"/* Helper {n}: ramps the left motor to the target speed,\n   {n % 7 + 2} ms per step of 5. */\n");
                sb.Append($"void rampLeft{n}(int target) {{\n");
                sb.Append("  for (int speed = 0; speed <= target; speed += 5) {\n");
                sb.Append("    analogWrite(ENA, speed);\n");
                sb.Append($"    delay({n % 7 + 2});\n");
                sb.Append("  }\n");
                sb.Append($"  Serial.println(\"ramp {n} done, id 0x{n * 37:X2}\");\n");
                sb.Append("}\n");
                lineCount += 10;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ live data

        void OnSerialLine(string line) => pendingSerial.Add(line);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1) && !CodeEditor.HasTypingFocus) Visible = !Visible;

            if (pendingSerial.Count > 0)
            {
                serialLines.AddRange(pendingSerial);
                pendingSerial.Clear();
                if (serialLines.Count > 500) serialLines.RemoveRange(0, serialLines.Count - 500);
                serialView.RefreshItems();
                if (autoscroll.value) serialView.ScrollToItem(serialLines.Count - 1);
            }

            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fpsLabel.text = $"{fpsFrames / fpsTimer:F0} fps";
                fpsFrames = 0;
                fpsTimer = 0;
            }
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= 0.1f)
            {
                refreshTimer = 0;
                RefreshValues();
                CheckStalls();
            }
        }

        void RefreshValues()
        {
            if (robot == null || robot.Mcu == null || values.Count == 0) return;
            string cm = SpikeStrings.Get("unit.cm"), v = SpikeStrings.Get("unit.V"), a = SpikeStrings.Get("unit.A");
            values["insp.distance"].text = double.IsNaN(robot.DistanceCm) ? SpikeStrings.Get("insp.noEcho") : $"{robot.DistanceCm:F1} {cm}";
            values["insp.measurements"].text = robot.SonarMeasurements.ToString();
            values["insp.supply"].text = $"{robot.SupplyVolts:F1} {v}";
            values["insp.leftMotor"].text = $"{Volts(robot.LeftVolts, v)}  {robot.LeftAmps:F2} {a}";
            values["insp.rightMotor"].text = $"{Volts(robot.RightVolts, v)}  {robot.RightAmps:F2} {a}";
            values["insp.wheelSpeed"].text = $"{robot.LeftWheelSpeed:F1} / {robot.RightWheelSpeed:F1} {SpikeStrings.Get("unit.rads")}";
            values["insp.emulatedTime"].text = $"{robot.Mcu.Seconds:F1} {SpikeStrings.Get("unit.s")}";
            values["insp.emulatorLoad"].text = SpikeStrings.Format("insp.perStep", robot.EmulatorMsPerFixedStep);
            values["serial.baud"].text = SpikeStrings.Format("serial.baud", 115200);
        }

        static string Volts(double volts, string unit) => double.IsNaN(volts) ? "-" : $"{volts:+0.0;-0.0;0.0} {unit}";

        void CheckStalls()
        {
            if (robot == null || robot.Mcu == null) return;
            leftStalled = Stall(leftStalled, robot.LeftAmps, robot.LeftWheelSpeed, "event.left");
            rightStalled = Stall(rightStalled, robot.RightAmps, robot.RightWheelSpeed, "event.right");
        }

        bool Stall(bool wasStalled, double amps, double speed, string sideKey)
        {
            bool stalled = Math.Abs(amps) > 0.9 && Math.Abs(speed) < 0.5;
            if (stalled && !wasStalled)
            {
                events.Add($"{robot!.Mcu.Seconds,7:F2} s  " + SpikeStrings.Format("event.stall", SpikeStrings.Get(sideKey), Math.Abs(amps)));
                eventView.RefreshItems();
                eventView.ScrollToItem(events.Count - 1);
            }
            return stalled;
        }

        void UpdateCodeStatus()
        {
            if (codeStatus == null || editor == null) return;
            codeStatus.text = SpikeStrings.Format("code.status", editor.CaretLine + 1, editor.CaretColumn + 1, editor.LineCount);
        }

        // ------------------------------------------------------------------ benchmark

        /// <summary>Font coverage, scrolling and typing costs, screenshots in all languages, and a dock move.</summary>
        public IEnumerator RunBenchmark(StringBuilder report, string shots, Func<string, IEnumerator> capture)
        {
            Visible = true;
            SpikeStrings.SetLanguage(0);
            for (int i = 0; i < 5; i++) yield return null;

            report.AppendLine("ui (UI Toolkit, panels over the running robot scene):");
            report.Append(FontReport());
            report.AppendLine($"  code editor: {editor.LineCount} lines ({sketchFile} plus generated helpers), virtualised ListView rows");
            for (int i = 0; i < 5; i++) yield return null; // FontReport's test fonts must not land in the first measured frame

            // 1. Scroll to the end and back at 24 px (1.3 lines) per frame, twice. Without the glyph
            //    pre-loading in LoadFonts, the first pass hitched (worst frame 201 ms) and the second did not.
            var scroll = editor.ScrollView;
            float max = scroll.verticalScroller.highValue;
            var frames = new List<double>();
            for (int pass = 1; pass <= 2; pass++)
            {
                frames.Clear();
                float y = 0;
                int direction = 1;
                int collections = GC.CollectionCount(0), frameCollections = 0;
                double worst = 0;
                bool worstHadGc = false;
                float worstAt = 0;
                while (true)
                {
                    y = Mathf.Clamp(y + direction * 24f, 0, max);
                    scroll.scrollOffset = new Vector2(0, y);
                    yield return null;
                    double ms = Time.unscaledDeltaTime * 1000.0;
                    int now = GC.CollectionCount(0);
                    bool gc = now != collections;
                    if (gc) frameCollections++;
                    collections = now;
                    frames.Add(ms);
                    if (ms > worst)
                    {
                        worst = ms;
                        worstHadGc = gc;
                        worstAt = y;
                    }
                    if (direction > 0 && y >= max) direction = -1;
                    else if (direction < 0 && y <= 0) break;
                }
                report.AppendLine($"  scrolling through all lines and back, pass {pass}: " + FrameStats(frames) +
                                  $"; worst frame near line {worstAt / CodeEditor.LineHeight:F0}, garbage collection in it: {(worstHadGc ? "yes" : "no")}; " +
                                  $"frames with a collection: {frameCollections}");
            }

            // 2. Typing: 200 keystrokes, one per frame, on a comment line near the top, including Enter and Backspace.
            editor.MoveCaret(1, int.MaxValue);
            var editMs = new List<double>();
            frames.Clear();
            for (int k = 0; k < 200; k++)
            {
                var watch = Stopwatch.StartNew();
                if (k % 50 == 49) editor.NewLine();
                else if (k % 10 == 9) editor.Backspace();
                else editor.Insert(((char)('a' + k % 26)).ToString());
                editMs.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000.0);
            }
            editMs.Sort();
            report.AppendLine($"  typing 200 keys: edit + recolour median {editMs[editMs.Count / 2]:F3} ms, max {editMs[editMs.Count - 1]:F3} ms; " + FrameStats(frames));

            // 3. Opening a block comment at the top recolours every line below it.
            editor.MoveCaret(2, 0);
            var block = Stopwatch.StartNew();
            editor.Insert("/");
            editor.Insert("*");
            double openMs = block.Elapsed.TotalMilliseconds;
            int recoloured = editor.LastRecolourCount;
            yield return null;
            block.Restart();
            editor.Backspace();
            editor.Backspace();
            double closeMs = block.Elapsed.TotalMilliseconds;
            report.AppendLine($"  typing /* at line 3 re-colours {recoloured} lines (up to the next */): {openMs:F2} ms; removing it: {closeMs:F2} ms");
            LoadSketch();

            // 4. The same screen in the three languages.
            for (int language = 0; language < SpikeStrings.LanguageCodes.Length; language++)
            {
                SpikeStrings.SetLanguage(language);
                for (int i = 0; i < 3; i++) yield return null;
                yield return capture($"{shots}-ui-{SpikeStrings.LanguageCodes[language]}.png");
            }

            // 5. Dock move: drag the Serial Monitor tab with simulated mouse events onto the right area.
            SpikeStrings.SetLanguage(0);
            for (int i = 0; i < 3; i++) yield return null;
            yield return DragTab(serialPanel, rightDock);
            for (int i = 0; i < 3; i++) yield return null;
            bool moved = serialPanel.Area == rightDock && rightDock.Panels.Count == 2 && bottomDock.Panels.Count == 2;
            report.AppendLine($"  dock move by dragging the Serial Monitor tab to the right area (simulated mouse): {(moved ? "OK" : "WRONG")}");
            yield return capture($"{shots}-ui-docked.png");
            report.AppendLine("  screenshots: -ui-en.png, -ui-uz.png, -ui-ru.png, -ui-docked.png");
        }

        /// <summary>Presses the mouse on a panel's tab, drags it in ten steps to the target area and releases it.</summary>
        IEnumerator DragTab(DockPanel panel, DockArea target)
        {
            if (panel.Tab == null || root.panel == null) yield break;
            Vector2 from = panel.Tab.worldBound.center;
            Vector2 to = target.worldBound.center;
            SendMouse(EventType.MouseDown, from);
            for (int step = 1; step <= 10; step++)
            {
                SendMouse(EventType.MouseDrag, Vector2.Lerp(from, to, step / 10f));
                yield return null;
            }
            SendMouse(EventType.MouseUp, to);
        }

        void SendMouse(EventType type, Vector2 position)
        {
            var systemEvent = new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
            EventBase pointerEvent = type == EventType.MouseDown ? PointerDownEvent.GetPooled(systemEvent)
                : type == EventType.MouseUp ? PointerUpEvent.GetPooled(systemEvent)
                : (EventBase)PointerMoveEvent.GetPooled(systemEvent);
            using (pointerEvent) root.panel.visualTree.SendEvent(pointerEvent);
        }

        string FontReport()
        {
            var sb = new StringBuilder();
            foreach (var (role, font) in new[] { ("UI font", uiFont), ("code font", codeFont) })
            {
                if (font == null)
                {
                    sb.AppendLine($"  {role}: not loaded");
                    continue;
                }
                var parts = new List<string>();
                for (int language = 0; language < SpikeStrings.LanguageCodes.Length; language++)
                {
                    string text = SpikeStrings.AllText(language) + "Ωµ°±×→…";
                    bool all = font.HasCharacters(text, out uint[] missing, false, true);
                    parts.Add($"{SpikeStrings.LanguageCodes[language]} {(all ? "complete" : "missing " + Describe(missing))}");
                }
                sb.AppendLine($"  {role}: {font.name} (from Windows) - {string.Join(", ", parts)}");
            }
            sb.AppendLine($"  glyph pre-loading at start-up (both fonts): {fontPreloadMs:F1} ms");
            // What pre-loading every needed glyph costs when a font is created (the fix for first-use hitches).
            string charset = Charset();
            foreach (string family in new[] { "Segoe UI", "Consolas" })
            {
                var watch = Stopwatch.StartNew();
                var fresh = CreateOsFont(family);
                double createMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                bool all = fresh != null && fresh.TryAddCharacters(charset, false);
                sb.AppendLine($"  pre-loading {charset.Length} glyphs into a new {family} atlas: create {createMs:F1} ms, add glyphs {watch.Elapsed.TotalMilliseconds:F1} ms{(all ? "" : " (some missing)")}");
                if (fresh != null) Destroy(fresh);
            }
            return sb.ToString();
        }

        static string Describe(uint[] missing)
        {
            var sb = new StringBuilder();
            foreach (uint c in missing) sb.Append($"U+{c:X4} ");
            return sb.ToString().TrimEnd();
        }

        static string FrameStats(List<double> frames)
        {
            double sum = 0, worst = 0;
            int over20 = 0, over33 = 0;
            foreach (double f in frames)
            {
                sum += f;
                worst = Math.Max(worst, f);
                if (f > 20) over20++;
                if (f > 33.3) over33++;
            }
            return $"{frames.Count} frames, {1000.0 * frames.Count / sum:F0} fps average, worst {worst:F1} ms, {over20} over 20 ms, {over33} over 33 ms";
        }
    }
}
