using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CoreEngine.Sim.Compile;
using CoreEngine.Sim.Components;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Prototype of the Garage, the main screen (ADR-0009, docs/03 §3.1): the selected robot on a turntable,
    /// the robot bar, the robot card, and every action one click away: Build, Wire and Body (GarageEdit.cs),
    /// Code (with a real arduino-cli upload), Customize (free and pack finishes with "Try"), Check &amp; repair,
    /// and START with the arena picker. Throwaway prototype, not the Phase 1 Garage.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class GarageSpike : MonoBehaviour
    {
        public Material litMaterial = null!;
        public StyleSheet? styleSheet;
        public StyleSheet? editorStyleSheet;
        public string arenaScene = "RobotSpike";

        static readonly string[] ArenaKeys = { "arena.obstacles", "arena.line", "arena.maze", "arena.sumo" };
        static readonly string[] ActionKeys = { "act.build", "act.wire", "act.code", "act.body", "act.customize", "act.repair" };

        // 3D
        Camera view = null!;
        Transform turntable = null!;
        Transform robotAnchor = null!;
        RobotVisuals? shown;
        float yaw = 215f, pitch = 12f, distance = 0.72f;
        float idleSeconds = 10f;
        bool orbiting;
        Vector3 lastMouse;
        readonly List<Material> roomMaterials = new List<Material>();

        // UI
        VisualElement root = null!, actions = null!, sidePanel = null!, overlay = null!, overlayPanel = null!;
        ScrollView sideContent = null!, barContent = null!;
        Label sideTitle = null!, toast = null!;
        Label cardName = null!, cardBoard = null!, cardSketch = null!, cardParts = null!, cardMass = null!, cardBatteryText = null!;
        ProgressBar cardBattery = null!;
        VisualElement cardWarnings = null!, cardBatteryRow = null!;
        Button startButton = null!;
        DropdownField arenaField = null!;
        readonly List<(TextElement element, string key)> localized = new List<(TextElement, string)>();
        readonly List<Button> languageButtons = new List<Button>();
        string sideTitleKey = "";
        Action? renderSide;
        Action? renderOverlay;
        float toastUntil;

        // Robot thumbnails for the bar: images copied out of a render texture, like the project's thumbnail.png.
        readonly List<Texture2D?> thumbnails = new List<Texture2D?>();

        // Code window
        CodeEditor? editor;
        Label? codeStatus;
        ListView? diagnosticsView;
        readonly List<CompilerDiagnostic> diagnostics = new List<CompilerDiagnostic>();
        string openedText = "";
        Task<CompileResult>? compileTask;
        RobotProject? compileRobot;
        string compileText = "";
        Stopwatch compileWatch = new Stopwatch();
        string lastCompileSummary = "not run";

        static RobotProject Robot => GarageState.Current;
        static string Tr(string key) => SpikeStrings.Get(key);

        void Start()
        {
            SpikeReport.Init();
            GarageState.Load(SpikeReport.Active);
            BuildRoom();
            BuildCamera();
            ShowRobot();
            BuildUi();
            SpikeStrings.LanguageChanged += ApplyLanguage;
            ApplyLanguage();
            StartCoroutine(RenderAllThumbnails());
            if (SpikeReport.Active) StartCoroutine(SpikeReport.Stage == 0 ? Benchmark() : AfterRun());
        }

        void OnDestroy()
        {
            SpikeStrings.LanguageChanged -= ApplyLanguage;
            foreach (var texture in thumbnails) if (texture != null) Destroy(texture);
            foreach (var material in roomMaterials) Destroy(material);
            if (previewMaterial != null) Destroy(previewMaterial);
            shown?.Destroy();
        }

        // ------------------------------------------------------------------ room, turntable, camera

        void BuildRoom()
        {
            // The player's desk in the maker room (D11): the robot's turntable in the middle, a pegboard
            // with tools behind it, a drawer cabinet for parts, a lamp, a cutting mat, a breadboard and a
            // multimeter. The desk top is y = 0; the camera looks toward -z.
            var wood = Mat(new Color(0.52f, 0.37f, 0.23f), 0.35f);
            var wall = Mat(new Color(0.27f, 0.30f, 0.35f), 0.1f);
            var peg = Mat(new Color(0.62f, 0.50f, 0.36f), 0.15f);
            var dark = Mat(new Color(0.10f, 0.11f, 0.13f), 0.4f);
            var grey = Mat(new Color(0.42f, 0.44f, 0.47f), 0.35f);

            Box("Floor", new Vector3(0, -0.80f, 0.6f), new Vector3(8, 0.1f, 8), dark);
            Box("Desk", new Vector3(0, -0.02f, -0.02f), new Vector3(2.6f, 0.04f, 1.2f), wood);
            Box("Wall", new Vector3(0, 0.7f, -0.66f), new Vector3(6, 3, 0.1f), wall);

            // Pegboard with hanging tools.
            Box("Pegboard", new Vector3(-0.05f, 0.40f, -0.605f), new Vector3(1.4f, 0.64f, 0.015f), peg);
            var holes = Mat(new Color(0.36f, 0.28f, 0.19f), 0.1f);
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 16; col++)
                    Box("Hole", new Vector3(-0.72f + col * 0.09f + 0.05f, 0.13f + row * 0.09f, -0.597f), new Vector3(0.008f, 0.008f, 0.002f), holes);
            Box("PliersHandle", new Vector3(-0.42f, 0.36f, -0.59f), new Vector3(0.03f, 0.13f, 0.015f), Mat(new Color(0.78f, 0.12f, 0.10f), 0.4f));
            Box("PliersJaw", new Vector3(-0.42f, 0.46f, -0.59f), new Vector3(0.02f, 0.07f, 0.012f), grey);
            Box("Screwdriver", new Vector3(-0.30f, 0.38f, -0.59f), new Vector3(0.022f, 0.10f, 0.02f), Mat(new Color(0.95f, 0.75f, 0.10f), 0.4f));
            Box("ScrewdriverShaft", new Vector3(-0.30f, 0.26f, -0.59f), new Vector3(0.006f, 0.14f, 0.006f), grey);
            Box("Snips", new Vector3(-0.18f, 0.37f, -0.59f), new Vector3(0.028f, 0.12f, 0.015f), Mat(new Color(0.15f, 0.35f, 0.75f), 0.4f));
            Box("Ruler", new Vector3(0.12f, 0.58f, -0.595f), new Vector3(0.42f, 0.03f, 0.005f), Mat(new Color(0.86f, 0.86f, 0.80f), 0.3f));
            Cylinder("WireSpoolRed", transform, new Vector3(0.28f, 0.38f, -0.57f), 0.07f, 0.035f, Mat(new Color(0.80f, 0.10f, 0.10f), 0.5f), Quaternion.Euler(90, 0, 0));
            Cylinder("WireSpoolBlack", transform, new Vector3(0.38f, 0.38f, -0.57f), 0.07f, 0.035f, Mat(new Color(0.08f, 0.08f, 0.08f), 0.5f), Quaternion.Euler(90, 0, 0));
            Cylinder("SolderSpool", transform, new Vector3(0.48f, 0.38f, -0.57f), 0.05f, 0.03f, grey, Quaternion.Euler(90, 0, 0));

            // Parts cabinet with small coloured drawers on the left of the desk.
            Box("PartsCabinet", new Vector3(-0.78f, 0.15f, -0.42f), new Vector3(0.36f, 0.30f, 0.2f), grey);
            Color[] drawers = { new Color(0.85f, 0.25f, 0.20f), new Color(0.20f, 0.55f, 0.90f), new Color(0.95f, 0.78f, 0.20f), new Color(0.30f, 0.72f, 0.35f) };
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    Box("Drawer", new Vector3(-0.906f + col * 0.084f, 0.045f + row * 0.07f, -0.318f), new Vector3(0.074f, 0.058f, 0.006f), Mat(drawers[(row + col) % drawers.Length], 0.3f));

            // Cutting mat, breadboard and multimeter on the right; a desk lamp at the back.
            Box("CuttingMat", new Vector3(0.62f, 0.001f, 0.05f), new Vector3(0.45f, 0.002f, 0.30f), Mat(new Color(0.12f, 0.42f, 0.30f), 0.2f));
            Box("Breadboard", new Vector3(0.58f, 0.006f, 0.02f), new Vector3(0.165f, 0.009f, 0.055f), Mat(new Color(0.93f, 0.93f, 0.90f), 0.3f));
            Box("Multimeter", new Vector3(0.78f, 0.016f, 0.10f), new Vector3(0.09f, 0.03f, 0.17f), Mat(new Color(0.95f, 0.55f, 0.10f), 0.3f));
            Box("MultimeterScreen", new Vector3(0.78f, 0.032f, 0.14f), new Vector3(0.06f, 0.002f, 0.04f), dark);
            Cylinder("LampBase", transform, new Vector3(0.95f, 0.01f, -0.45f), 0.12f, 0.02f, dark, Quaternion.identity);
            Box("LampArm", new Vector3(0.95f, 0.22f, -0.45f), new Vector3(0.018f, 0.42f, 0.018f), dark);
            Box("LampHead", new Vector3(0.88f, 0.42f, -0.40f), new Vector3(0.14f, 0.05f, 0.09f), dark);
            var lamp = new GameObject("LampLight").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 1.2f;
            lamp.intensity = 0.8f;
            lamp.color = new Color(1f, 0.85f, 0.65f);
            lamp.transform.position = new Vector3(0.85f, 0.36f, -0.36f);

            // Turntable: the robot's wheels stand on its top, 3.5 cm above the desk.
            turntable = new GameObject("Turntable").transform;
            Cylinder("Base", turntable, new Vector3(0, 0.015f, 0), 0.40f, 0.03f, dark, Quaternion.identity);
            Cylinder("Rim", turntable, new Vector3(0, 0.031f, 0), 0.392f, 0.002f, Mat(new Color(0.18f, 0.45f, 0.62f), 0.6f), Quaternion.identity);
            Cylinder("Top", turntable, new Vector3(0, 0.0325f, 0), 0.37f, 0.005f, Mat(new Color(0.16f, 0.17f, 0.19f), 0.55f), Quaternion.identity);
            robotAnchor = new GameObject("RobotAnchor").transform;
            robotAnchor.SetParent(turntable, false);
            robotAnchor.localPosition = new Vector3(0, 0.035f + 0.05f, 0); // chassis origin is 5 cm above the wheels' contact

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.65f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            Spot(new Vector3(0.7f, 1.2f, 0.8f), 5f, true);
            Spot(new Vector3(-0.8f, 1.0f, 0.5f), 2.5f, false);
        }

        void Spot(Vector3 position, float intensity, bool shadows)
        {
            var spot = new GameObject("Spot").AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 5f;
            spot.spotAngle = 38f;
            spot.intensity = intensity;
            spot.color = new Color(1f, 0.95f, 0.88f);
            spot.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            spot.transform.position = position;
            spot.transform.LookAt(new Vector3(0, 0.07f, 0));
        }

        void BuildCamera()
        {
            view = new GameObject("GarageCamera").AddComponent<Camera>();
            view.tag = "MainCamera";
            view.fieldOfView = 44f;
            view.nearClipPlane = 0.02f;
            view.farClipPlane = 30f;
            UpdateCamera();
        }

        /// <summary>Builds the robot's model on the turntable: with part colliders in Build, with pin markers in Wire.</summary>
        void ShowRobot(BodyMeshes? body = null)
        {
            shown?.Destroy();
            shown = RobotVisuals.Build(robotAnchor, null, null, Robot, litMaterial, pickable: mode == EditMode.Build, pins: mode == EditMode.Wire, prebuiltBody: body);
            if (mode == EditMode.Build) shown.Highlight(selectedPart);
            if (mode == EditMode.Wire)
            {
                shown.HighlightWire(selectedWire);
                shown.HighlightPins(hoveredPin, wireStart);
            }
        }

        void Update()
        {
            if (mode == EditMode.None)
            {
                UpdateOrbit();
                if (!orbiting) idleSeconds += Time.deltaTime;
                if (idleSeconds > 4f) turntable.Rotate(0, 10f * Time.deltaTime, 0);
            }
            else
            {
                UpdateEditing();
            }
            UpdateCamera();
            if (toast.style.display == DisplayStyle.Flex && Time.unscaledTime > toastUntil) toast.style.display = DisplayStyle.None;
            if (Input.GetKeyDown(KeyCode.Escape) && overlay.style.display == DisplayStyle.Flex) CloseOverlay();
            PollCompile();
            UpdateEditFrame();
        }

        void UpdateOrbit()
        {
            bool overUi = IsPointerOverUi();
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !overUi)
            {
                orbiting = true;
                lastMouse = Input.mousePosition;
            }
            if (orbiting && (Input.GetMouseButton(0) || Input.GetMouseButton(1)))
            {
                var delta = Input.mousePosition - lastMouse;
                lastMouse = Input.mousePosition;
                yaw += delta.x * 0.3f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.2f, 3f, 60f);
                idleSeconds = 0;
            }
            else
            {
                orbiting = false;
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !overUi)
            {
                distance = Mathf.Clamp(distance * (1f - scroll * 0.1f), 0.35f, 1.3f);
                idleSeconds = 0;
            }
        }

        void UpdateCamera()
        {
            view.transform.position = orbitTarget + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -distance);
            view.transform.LookAt(orbitTarget);
        }

        bool IsPointerOverUi()
        {
            if (root == null || root.panel == null) return false;
            var mouse = Input.mousePosition;
            var point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
            return root.panel.Pick(point) != null;
        }

        // ------------------------------------------------------------------ UI layout

        void BuildUi()
        {
            var document = GetComponent<UIDocument>();
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            root = Layout("garage-root");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (editorStyleSheet != null) root.styleSheets.Add(editorStyleSheet);
            if (SpikeFonts.Ui != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(SpikeFonts.Ui);
            document.rootVisualElement.Add(root);

            // Top bar: navigation, arena picker, START, languages, settings.
            var top = new VisualElement();
            top.AddToClassList("top-bar");
            top.Add(Classed(new Label("CoreEngine"), "app-title"));
            AddNav(top, "nav.garage", true, CloseSide);
            AddNav(top, "nav.notebook", false, () => ShowPage("nav.notebook", "page.notebookInfo"));
            AddNav(top, "nav.shop", false, ShowShop);
            AddNav(top, "nav.workshop", false, () => ShowPage("nav.workshop", "page.workshopInfo"));
            top.Add(Layout("spacer"));
            top.Add(Classed(Localized(new Label(), "arena"), "arena-label"));
            arenaField = new DropdownField(new List<string>(), 0) { focusable = false };
            arenaField.AddToClassList("arena-field");
            arenaField.RegisterValueChangedCallback(_ => OnArenaChosen());
            top.Add(arenaField);
            startButton = new Button(StartRun);
            startButton.AddToClassList("start-button");
            localized.Add((startButton, "start"));
            top.Add(startButton);
            for (int i = 0; i < SpikeStrings.LanguageButtons.Length; i++)
            {
                int language = i;
                var button = new Button(() => SpikeStrings.SetLanguage(language)) { text = SpikeStrings.LanguageButtons[i], focusable = false };
                button.AddToClassList("lang-button");
                languageButtons.Add(button);
                top.Add(button);
            }
            var settings = new Button(() => ShowToast(Tr("settings.info"))) { text = "⚙", focusable = false };
            settings.AddToClassList("icon-button");
            top.Add(settings);
            root.Add(top);

            // Middle: robot card | free view of the robot | actions and side panel.
            var middle = Layout("middle");
            middle.Add(BuildCard());
            middle.Add(Layout("centre"));
            var right = Layout("right-column");
            actions = Layout("actions");
            foreach (string key in ActionKeys)
            {
                string action = key;
                var button = new Button(() => OnAction(action));
                button.AddToClassList("action-button");
                localized.Add((button, key));
                actions.Add(button);
            }
            right.Add(actions);
            sidePanel = new VisualElement();
            sidePanel.AddToClassList("side-panel");
            var header = Layout("side-header");
            var back = new Button(CloseSide) { text = "◀", focusable = false };
            back.AddToClassList("back-button");
            header.Add(back);
            sideTitle = Classed(new Label(), "side-title");
            header.Add(sideTitle);
            sidePanel.Add(header);
            sideContent = new ScrollView();
            sideContent.AddToClassList("side-content");
            sidePanel.Add(sideContent);
            sidePanel.style.display = DisplayStyle.None;
            right.Add(sidePanel);
            middle.Add(right);
            root.Add(middle);

            hint = Classed(new Label { pickingMode = PickingMode.Ignore }, "garage-hint");
            root.Add(hint);

            // Robot bar.
            var bar = new VisualElement();
            bar.AddToClassList("robot-bar");
            barContent = new ScrollView(ScrollViewMode.Horizontal);
            barContent.AddToClassList("bar-scroll");
            bar.Add(barContent);
            root.Add(bar);

            toast = Classed(new Label(), "toast");
            toast.style.display = DisplayStyle.None;
            root.Add(toast);

            overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlayPanel = new VisualElement();
            overlayPanel.AddToClassList("overlay-panel");
            overlay.Add(overlayPanel);
            overlay.style.display = DisplayStyle.None;
            root.Add(overlay);
            BuildEditUi();
        }

        VisualElement BuildCard()
        {
            var card = new VisualElement();
            card.AddToClassList("robot-card");
            cardName = Classed(new Label(), "card-name");
            card.Add(cardName);
            cardBoard = CardRow(card, "card.board");
            cardSketch = CardRow(card, "card.sketch");
            cardParts = CardRow(card, "card.parts");
            cardMass = CardRow(card, "card.mass");
            cardBatteryRow = new VisualElement();
            cardBatteryText = CardRow(cardBatteryRow, "card.battery");
            cardBattery = new ProgressBar { lowValue = 0, highValue = 100 };
            cardBattery.AddToClassList("battery-bar");
            cardBatteryRow.Add(cardBattery);
            card.Add(cardBatteryRow);
            cardWarnings = Layout("card-warnings");
            card.Add(cardWarnings);
            return card;
        }

        Label CardRow(VisualElement parent, string key)
        {
            var row = Layout("card-row");
            row.Add(Classed(Localized(new Label(), key), "card-key"));
            var value = Classed(new Label(), "card-value");
            row.Add(value);
            parent.Add(row);
            return value;
        }

        void AddNav(VisualElement top, string key, bool active, Action onClick)
        {
            var button = new Button(onClick) { focusable = false };
            button.AddToClassList("nav-button");
            button.EnableInClassList("nav-button--active", active);
            localized.Add((button, key));
            top.Add(button);
        }

        // ------------------------------------------------------------------ robot card and bar

        void RefreshCard()
        {
            var robot = Robot;
            cardName.text = robot.Name;
            cardBoard.text = robot.Electronics ? "Arduino Uno R3" : Tr("card.noBoard");
            cardSketch.text = !robot.HasSketch ? Tr("card.noSketch")
                : robot.RunsFactoryBlink ? $"{robot.SketchFile}\n" + Tr("card.factoryBlink")
                : $"{robot.SketchFile}\n" + SpikeStrings.Format("card.compiled", robot.ProgramBytes);
            cardParts.text = robot.PartCount.ToString();
            cardMass.text = $"{robot.MassKg * 1000:F0} g";
            bool hasBattery = robot.Design.Count(PartCatalog.Battery4AA) > 0;
            cardBatteryRow.style.display = hasBattery ? DisplayStyle.Flex : DisplayStyle.None;
            double charge = robot.Battery.StateOfCharge * 100;
            cardBattery.value = (float)charge;
            cardBatteryText.text = $"{charge:F1} %";

            cardWarnings.Clear();
            var warnings = new List<string>();
            var design = robot.Design;
            if (!robot.Electronics) warnings.Add(Tr("warn.noBoard"));
            if (hasBattery)
            {
                if (robot.Battery.IsEmpty) warnings.Add(Tr("warn.batteryEmpty"));
                else if (charge < 20) warnings.Add(SpikeStrings.Format("warn.batteryLow", charge));
            }
            if (design.HasSlot(PartCatalog.TtMotor, "left") && robot.LeftMotor.Burnt) warnings.Add(SpikeStrings.Format("warn.motorBurnt", Tr("side.left")));
            if (design.HasSlot(PartCatalog.TtMotor, "right") && robot.RightMotor.Burnt) warnings.Add(SpikeStrings.Format("warn.motorBurnt", Tr("side.right")));
            if (robot.Electronics && robot.BoardBurnt) warnings.Add(Tr("warn.boardBurnt"));
            if (design.Count(PartCatalog.HcSr04) > 0 && robot.SonarBurnt) warnings.Add(Tr("warn.sonarBurnt"));
            if (design.Parts.Count > 0)
            {
                int problems = CircuitAnalysis.Analyse(design).Warnings.FindAll(w => !w.Info).Count;
                if (problems > 0 && robot.Electronics) warnings.Add(SpikeStrings.Format("warn.wiring", problems));
            }
            if (robot.Electronics && robot.CodeNotUploaded) warnings.Add(Tr("warn.notUploaded"));
            if (robot.IsTrying) warnings.Add(Tr("warn.trying"));
            foreach (string warning in warnings) cardWarnings.Add(Classed(new Label(warning), warning.StartsWith("★") ? "try-line" : "warn-line"));
            if (warnings.Count == 0 || (warnings.Count == 1 && robot.IsTrying)) cardWarnings.Add(Classed(new Label(Tr("card.ready")), "ok-line"));

            // Warnings never block START (ADR-0009): a robot without a board simply stands in the arena.
        }

        void RefreshBar()
        {
            barContent.Clear();
            for (int i = 0; i < GarageState.Robots.Count; i++)
            {
                int index = i;
                var robot = GarageState.Robots[i];
                var card = new Button(() => Select(index)) { focusable = false };
                card.AddToClassList("bar-card");
                card.EnableInClassList("bar-card--selected", i == GarageState.Selected);
                var thumb = Layout("bar-thumb");
                var texture = i < thumbnails.Count ? thumbnails[i] : null;
                if (texture != null) thumb.style.backgroundImage = Background.FromTexture2D(texture);
                card.Add(thumb);
                card.Add(Classed(new Label(robot.Name), "bar-name"));
                string sub = robot.Electronics ? "Arduino Uno R3" : robot.Design.Parts.Count == 0 ? Tr("bar.empty") : SpikeStrings.Format("bar.parts", robot.Design.Parts.Count);
                card.Add(Classed(new Label(sub), "bar-sub"));
                barContent.Add(card);
            }
            var add = new Button(NewRobot) { focusable = false };
            add.AddToClassList("bar-card");
            add.AddToClassList("bar-new");
            add.Add(Classed(new Label(Tr("bar.new")), "bar-new-label"));
            barContent.Add(add);
        }

        void Select(int index)
        {
            if (mode != EditMode.None && index != GarageState.Selected) StartCoroutine(RenderThumbnail(GarageState.Selected));
            ResetEditState();
            GarageState.Selected = index;
            GarageState.Save();
            ShowRobot();
            RefreshBar();
            RefreshCard();
            renderSide?.Invoke();
        }

        void NewRobot()
        {
            if (mode != EditMode.None) StartCoroutine(RenderThumbnail(GarageState.Selected));
            ResetEditState();
            GarageState.NewRobot();
            ShowRobot();
            RefreshBar();
            RefreshCard();
            renderSide?.Invoke();
            StartCoroutine(RenderThumbnail(GarageState.Selected));
        }

        void OnArenaChosen()
        {
            if (arenaField.index > 0)
            {
                ShowToast(Tr("arena.later"));
                arenaField.index = 0;
            }
        }

        void StartRun()
        {
            LeaveMode(show: false);
            GarageState.Arena = 0;
            GarageState.Save();
            SpikeReport.Transition = Stopwatch.StartNew();
            SceneManager.LoadScene(arenaScene);
        }

        // ------------------------------------------------------------------ actions and side panels

        void OnAction(string key)
        {
            switch (key)
            {
                case "act.build": EnterMode(EditMode.Build, key, RenderBuild); break;
                case "act.wire": EnterMode(EditMode.Wire, key, RenderWire); break;
                case "act.body": EnterMode(EditMode.Body, key, RenderBody); break;
                case "act.code":
                    LeaveMode();
                    OpenCode();
                    break;
                case "act.customize":
                    LeaveMode();
                    OpenSide(key, RenderCustomize);
                    break;
                case "act.repair":
                    LeaveMode();
                    OpenSide(key, RenderRepair);
                    break;
            }
        }

        void OpenSide(string titleKey, Action render)
        {
            actions.style.display = DisplayStyle.None;
            sidePanel.style.display = DisplayStyle.Flex;
            sideTitleKey = titleKey;
            sideTitle.text = Tr(titleKey);
            renderSide = () =>
            {
                sideContent.Clear();
                render();
            };
            renderSide();
        }

        void CloseSide()
        {
            LeaveMode();
            actions.style.display = DisplayStyle.Flex;
            sidePanel.style.display = DisplayStyle.None;
            renderSide = null;
        }

        void Info(string key) => sideContent.Add(Classed(new Label(Tr(key)), "info-text"));

        void Section(string key) => sideContent.Add(Classed(new Label(Tr(key)), "section-title"));

        void RenderCustomize()
        {
            Section("cust.body");
            Swatches(FinishTarget.Body);
            Section("cust.wheels");
            Swatches(FinishTarget.Wheels);
            var robot = Robot;
            if (robot.IsTrying)
            {
                string id = robot.TriedBodyFinish.Length > 0 ? robot.TriedBodyFinish : robot.TriedWheelFinish;
                var finish = Finishes.Get(id, robot.TriedBodyFinish.Length > 0 ? FinishTarget.Body : FinishTarget.Wheels);
                sideContent.Add(Classed(new Label(SpikeStrings.Format("cust.trying", finish.Name, Tr(finish.Pack!))), "info-text"));
                var stop = new Button(StopTrying) { text = Tr("cust.stop"), focusable = false };
                stop.AddToClassList("small-button");
                sideContent.Add(stop);
            }
        }

        void Swatches(FinishTarget target)
        {
            var robot = Robot;
            string active = target == FinishTarget.Body ? robot.ActiveBodyFinish : robot.ActiveWheelFinish;
            var grid = Layout("swatch-grid");
            foreach (var finish in Finishes.All)
            {
                if (finish.Target != target) continue;
                var chosen = finish;
                var item = new Button(() => Choose(chosen)) { focusable = false };
                item.AddToClassList("swatch-item");
                item.EnableInClassList("swatch-item--active", finish.Id == active);
                var chip = Layout("swatch-chip");
                chip.style.backgroundColor = finish.Color;
                if (!Finishes.Owns(finish.Pack)) chip.Add(Classed(new Label("\U0001F512"), "swatch-lock"));
                item.Add(chip);
                item.Add(Classed(new Label(finish.Name), "swatch-name"));
                item.Add(Classed(new Label(finish.IsFree ? Tr("cust.free") : Tr(finish.Pack!)), "swatch-pack"));
                grid.Add(item);
            }
            sideContent.Add(grid);
        }

        void Choose(Finish finish)
        {
            var robot = Robot;
            bool owned = Finishes.Owns(finish.Pack);
            if (finish.Target == FinishTarget.Body)
            {
                if (owned) robot.BodyFinish = finish.Id;
                robot.TriedBodyFinish = owned ? "" : finish.Id;
            }
            else
            {
                if (owned) robot.WheelFinish = finish.Id;
                robot.TriedWheelFinish = owned ? "" : finish.Id;
            }
            AfterRobotChanged(thumbnail: true);
        }

        void StopTrying()
        {
            Robot.TriedBodyFinish = "";
            Robot.TriedWheelFinish = "";
            AfterRobotChanged(thumbnail: true);
        }

        void RenderRepair()
        {
            var robot = Robot;
            var design = robot.Design;
            Section("rep.readiness");
            if (design.Parts.Count == 0)
            {
                Info("warn.noBoard");
                Info("rep.noParts");
                return;
            }
            bool hasBattery = design.Count(PartCatalog.Battery4AA) > 0;
            if (hasBattery)
            {
                string power = SpikeStrings.Format("rep.power", robot.Battery.TerminalVolts(0.25));
                sideContent.Add(Classed(new Label((robot.Battery.IsEmpty ? "⚠ " : "✓ ") + power), robot.Battery.IsEmpty ? "warn-line" : "ok-line"));
            }
            else
            {
                sideContent.Add(Classed(new Label("⚠ " + Tr("rep.noBattery")), "warn-line"));
            }
            if (!robot.Electronics) sideContent.Add(Classed(new Label(Tr("warn.noBoard")), "warn-line"));
            else if (robot.RunsFactoryBlink) sideContent.Add(Classed(new Label("• " + Tr("rep.factoryBlink")), "info-line"));
            else sideContent.Add(Classed(new Label("✓ " + SpikeStrings.Format("rep.sketch", robot.ProgramBytes)), "ok-line"));
            int problems = CircuitAnalysis.Analyse(design).Warnings.FindAll(w => !w.Info).Count;
            sideContent.Add(problems == 0
                ? Classed(new Label("✓ " + Tr("rep.wiringOk")), "ok-line")
                : Classed(new Label("⚠ " + SpikeStrings.Format("rep.wiringBad", problems)), "warn-line"));

            Section("rep.parts");
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                switch (def.Kind)
                {
                    case PartKind.Motor:
                        MotorRow(part.Slot == "right" ? robot.RightMotor : robot.LeftMotor, part.Slot == "right" ? "side.right" : "side.left");
                        break;
                    case PartKind.Battery:
                        PartRow(SpikeStrings.Format("rep.battery", robot.Battery.StateOfCharge * 100), robot.Battery.IsEmpty ? Tr("rep.burnt") : Tr("rep.ok"), !robot.Battery.IsEmpty);
                        if (robot.Battery.StateOfCharge < 0.999)
                            sideContent.Add(SmallButton("rep.replaceBatteries", () => { robot.Battery.Replace(); AfterRobotChanged(false); }));
                        break;
                    case PartKind.Board:
                        BurnablePartRow(def.Name, robot.BoardBurnt, () => robot.BoardBurnt = false, "rep.whyF7");
                        break;
                    case PartKind.Ultrasonic:
                        BurnablePartRow(def.Name, robot.SonarBurnt, () => robot.SonarBurnt = false, "rep.whyF26");
                        break;
                    default:
                        PartRow(def.Name, Tr("rep.ok"), true);
                        break;
                }
            }
        }

        /// <summary>A part that a wiring fault can destroy (F7, F26): its state, and Replace with Why when it burnt out.</summary>
        void BurnablePartRow(string name, bool burnt, Action replace, string whyKey)
        {
            PartRow(name, burnt ? Tr("rep.burnt") : Tr("rep.ok"), !burnt);
            if (!burnt) return;
            var buttons = Layout("repair-buttons");
            buttons.Add(SmallButton("rep.replace", () => { replace(); AfterRobotChanged(false); }));
            buttons.Add(SmallButton("rep.why", () => ShowPage("rep.why", whyKey)));
            sideContent.Add(buttons);
        }

        void MotorRow(MotorWinding motor, string sideKey)
        {
            string text = SpikeStrings.Format("rep.motor", Tr(sideKey), motor.TemperatureC, motor.PeakC);
            PartRow(text, motor.Burnt ? Tr("rep.burnt") : Tr("rep.ok"), !motor.Burnt);
            if (!motor.Burnt) return;
            var buttons = Layout("repair-buttons");
            buttons.Add(SmallButton("rep.replace", () => { motor.Replace(); AfterRobotChanged(false); }));
            buttons.Add(SmallButton("rep.why", () => ShowPage("rep.why", "rep.whyF18")));
            sideContent.Add(buttons);
        }

        Label PartRow(string name, string status, bool ok)
        {
            var row = Layout("part-row");
            row.Add(Classed(new Label(name), "part-name"));
            var label = Classed(new Label(status), ok ? "status-ok" : "status-bad");
            row.Add(label);
            sideContent.Add(row);
            return label;
        }

        Button SmallButton(string key, Action onClick)
        {
            var button = new Button(onClick) { text = Tr(key), focusable = false };
            button.AddToClassList("small-button");
            return button;
        }

        void AfterRobotChanged(bool thumbnail)
        {
            shown?.ApplyFinishes(Robot);
            GarageState.Save();
            RefreshCard();
            renderSide?.Invoke();
            if (thumbnail) StartCoroutine(RenderThumbnail(GarageState.Selected));
        }

        // ------------------------------------------------------------------ overlay pages and the code window

        void ShowOverlay(bool page, Action render)
        {
            overlayPanel.EnableInClassList("overlay-page", page);
            renderOverlay = () =>
            {
                overlayPanel.Clear();
                render();
            };
            renderOverlay();
            overlay.style.display = DisplayStyle.Flex;
        }

        void CloseOverlay()
        {
            if (editor != null && editor.Text != openedText) Robot.SketchText = editor.Text;
            editor = null;
            codeStatus = null;
            diagnosticsView = null;
            overlay.style.display = DisplayStyle.None;
            renderOverlay = null;
            GarageState.Save();
            RefreshCard();
        }

        VisualElement OverlayHeader(string title)
        {
            var header = Layout("overlay-header");
            header.Add(Classed(new Label(title), "overlay-title"));
            return header;
        }

        void ShowPage(string titleKey, string textKey) => ShowOverlay(true, () =>
        {
            var header = OverlayHeader(Tr(titleKey));
            header.Add(SmallButton("page.close", CloseOverlay));
            overlayPanel.Add(header);
            overlayPanel.Add(Classed(new Label(Tr(textKey)), "info-text"));
        });

        void ShowShop() => ShowOverlay(true, () =>
        {
            var header = OverlayHeader(Tr("nav.shop"));
            header.Add(SmallButton("page.close", CloseOverlay));
            overlayPanel.Add(header);
            overlayPanel.Add(Classed(new Label(Tr("page.shopInfo")), "info-text"));
            foreach (string key in new[] { "shop.mega", "shop.carbon", "shop.neon", "shop.bundle" })
                overlayPanel.Add(Classed(new Label("• " + Tr(key)), "info-text"));
            overlayPanel.Add(Classed(new Label(Tr("shop.try")), "info-text"));
        });

        void OpenCode()
        {
            var robot = Robot;
            if (!robot.Electronics)
            {
                ShowToast(Tr("chk.noBoard"));
                return;
            }
            robot.EnsureSketch();
            openedText = robot.SketchText.Length > 0 ? robot.SketchText : ReadSketchFile(robot);
            ShowOverlay(false, () =>
            {
                var header = OverlayHeader($"{Tr("act.code")} — {robot.SketchFile}");
                var upload = new Button(Upload) { text = Tr("code.upload"), focusable = false };
                upload.AddToClassList("upload-button");
                header.Add(upload);
                header.Add(SmallButton("code.close", CloseOverlay));
                overlayPanel.Add(header);
                editor = new CodeEditor();
                editor.AddToClassList("code-overlay-editor");
                if (SpikeFonts.Code != null) editor.SetFont(FontDefinition.FromSDFFont(SpikeFonts.Code));
                overlayPanel.Add(editor);
                editor.SetText(openedText);
                codeStatus = Classed(new Label(), "code-status-line");
                overlayPanel.Add(codeStatus);
                diagnosticsView = new ListView(diagnostics, 18, () => Classed(new Label(), "diag-line"),
                    (element, i) => ((Label)element).text = diagnostics[i].ToString())
                {
                    selectionType = SelectionType.Single,
                };
                diagnosticsView.AddToClassList("diagnostics");
                diagnosticsView.style.display = DisplayStyle.None;
                diagnosticsView.selectionChanged += _ =>
                {
                    if (diagnosticsView.selectedIndex >= 0 && diagnosticsView.selectedIndex < diagnostics.Count)
                    {
                        var d = diagnostics[diagnosticsView.selectedIndex];
                        editor?.MoveCaret(d.Line - 1, d.Column - 1);
                        editor?.Focus();
                    }
                };
                overlayPanel.Add(diagnosticsView);
            });
        }

        static string ReadSketchFile(RobotProject robot)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Sketches", robot.SketchFile);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        /// <summary>Compiles the editor's text with the bundled arduino-cli on a worker thread (ADR-0003).</summary>
        void Upload()
        {
            if (compileTask != null || editor == null) return;
            var compiler = ArduinoCliCompiler.FindBundled(Application.dataPath);
            if (compiler == null)
            {
                if (codeStatus != null) codeStatus.text = Tr("code.noToolchain");
                lastCompileSummary = "toolchain not found";
                return;
            }
            compileRobot = Robot;
            compileText = editor.Text;
            string name = SafeName(compileRobot.Name);
            string data = Application.persistentDataPath;
            string sketch = Path.Combine(data, "Sketches", name);
            Directory.CreateDirectory(sketch);
            File.WriteAllText(Path.Combine(sketch, name + ".ino"), compileText);
            string build = Path.Combine(data, "Build", name);
            string output = Path.Combine(data, "Firmware", name);
            compileWatch = Stopwatch.StartNew();
            compileTask = Task.Run(() => compiler.Compile(sketch, "arduino:avr:uno", build, output, TimeSpan.FromMinutes(2)));
            if (codeStatus != null) codeStatus.text = Tr("code.compiling");
        }

        void PollCompile()
        {
            if (compileTask == null || !compileTask.IsCompleted) return;
            var task = compileTask;
            compileTask = null;
            double seconds = compileWatch.Elapsed.TotalSeconds;
            var robot = compileRobot!;
            diagnostics.Clear();
            if (task.IsFaulted)
            {
                string message = task.Exception?.GetBaseException().Message ?? "unknown error";
                if (codeStatus != null) codeStatus.text = message;
                lastCompileSummary = "failed: " + message;
                diagnosticsView?.RefreshItems();
                return;
            }
            var result = task.Result;
            foreach (var d in result.Diagnostics)
                if (d.Severity == DiagnosticSeverity.Error) diagnostics.Add(d);
            diagnosticsView?.RefreshItems();
            if (diagnosticsView != null) diagnosticsView.style.display = diagnostics.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (result.Success && result.HexPath != null)
            {
                var match = Regex.Match(result.Output, @"Sketch uses ([\d,.]+) bytes");
                int bytes = match.Success ? int.Parse(match.Groups[1].Value.Replace(",", "").Replace(".", "")) : 0;
                robot.FirmwarePath = result.HexPath;
                robot.ProgramBytes = bytes;
                robot.SketchText = compileText;
                robot.UploadedText = compileText;
                openedText = compileText;
                GarageState.Save();
                RefreshCard();
                if (codeStatus != null) codeStatus.text = SpikeStrings.Format("code.ok", bytes, seconds);
                lastCompileSummary = $"OK, {bytes} bytes in {seconds:F1} s";
            }
            else
            {
                if (codeStatus != null) codeStatus.text = SpikeStrings.Format("code.failed", diagnostics.Count);
                lastCompileSummary = $"failed with {diagnostics.Count} errors in {seconds:F1} s";
            }
        }

        static string SafeName(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!(char.IsLetterOrDigit(chars[i]) && chars[i] < 128) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            string safe = new string(chars).Trim('_');
            return safe.Length == 0 || !char.IsLetterOrDigit(safe[0]) ? "Robot_" + safe : safe;
        }

        void ShowToast(string text)
        {
            toast.text = text;
            toast.style.display = DisplayStyle.Flex;
            toastUntil = Time.unscaledTime + 4f;
        }

        void ApplyLanguage()
        {
            foreach (var (element, key) in localized) element.text = Tr(key);
            for (int i = 0; i < languageButtons.Count; i++)
                languageButtons[i].EnableInClassList("lang-button--active", i == SpikeStrings.Language);
            var choices = new List<string>();
            foreach (string key in ArenaKeys) choices.Add(Tr(key));
            arenaField.choices = choices;
            arenaField.SetValueWithoutNotify(choices[0]);
            if (sideTitleKey.Length > 0) sideTitle.text = Tr(sideTitleKey);
            UpdateHint();
            renderSide?.Invoke();
            if (editor == null) renderOverlay?.Invoke();
            RefreshCard();
            RefreshBar();
        }

        // ------------------------------------------------------------------ thumbnails

        IEnumerator RenderAllThumbnails()
        {
            for (int i = 0; i < GarageState.Robots.Count; i++) yield return RenderThumbnail(i);
        }

        /// <summary>Renders one robot off-screen and copies the image into its card's thumbnail.</summary>
        IEnumerator RenderThumbnail(int index)
        {
            const int width = 680, height = 256;
            while (thumbnails.Count <= index) thumbnails.Add(null);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var stage = new GameObject("ThumbnailStage");
            stage.transform.position = new Vector3(40f + index * 3f, 0.05f, 0);
            var visual = RobotVisuals.Build(stage.transform, null, null, GarageState.Robots[index], litMaterial);
            var camera = new GameObject("ThumbnailCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            camera.fieldOfView = 24f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.17f, 0.19f, 0.23f);
            camera.transform.position = stage.transform.position + Quaternion.Euler(24f, 215f, 0) * new Vector3(0, 0, -0.5f);
            camera.transform.LookAt(stage.transform.position);

            // A render request draws the camera into the texture right now. During the first frames after
            // start-up URP drops such requests silently, so check the result (the clear colour is opaque)
            // and try again on the next frame. The image is copied out at once: a render texture can lose
            // its content, a Texture2D keeps it.
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                Debug.LogWarning("GarageSpike: the render pipeline cannot render thumbnails on request");
            }
            else
            {
                for (int attempt = 1; attempt <= 120; attempt++)
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    if (HasPixels(target))
                    {
                        var image = thumbnails[index] ?? new Texture2D(width, height, TextureFormat.RGBA32, false);
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        image.Apply();
                        RenderTexture.active = previous;
                        thumbnails[index] = image;
                        if (index == 0) ThumbnailAttempts = attempt;
                        break;
                    }
                    yield return null;
                }
            }
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Destroy(camera.gameObject);
            visual.Destroy();
            Destroy(stage);
            RefreshBar();
        }

        // ------------------------------------------------------------------ benchmark (-spikeBench)

        IEnumerator Benchmark()
        {
            var report = SpikeReport.Text;
            report.AppendLine("garage (main screen, ADR-0009):");
            report.AppendLine($"  fonts from Windows (Segoe UI and Consolas with a Segoe UI Symbol fallback) created with their glyphs in {SpikeFonts.PreloadMs:F1} ms, " +
                              $"of which the symbol font {SpikeFonts.SymbolFontMs:F1} ms");
            for (int i = 0; i < 30; i++) yield return null;

            var frames = new List<double>();
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000.0);
            }
            report.AppendLine("  garage view with the turntable turning: " + SpikeReport.FrameStats(frames));
            report.AppendLine($"  robot thumbnails rendered off-screen; the first needed {ThumbnailAttempts} attempt(s) after start-up: " +
                              SaveThumbnail(0, SpikeReport.Shot("garage-thumbnail")));
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-en"));

            // Customize: trying pack finishes changes the look but not the saved finishes.
            OnAction("act.customize");
            Choose(Finishes.Get("carbon-fibre", FinishTarget.Body));
            Choose(Finishes.Get("chrome-hubs", FinishTarget.Wheels));
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-customize"));
            bool tryKept = Robot.IsTrying && Robot.BodyFinish == "blue-acrylic" && Robot.WheelFinish == "yellow-hubs";
            StopTrying();
            report.AppendLine($"  customize: trying carbon fibre and chrome hubs leaves the saved finishes unchanged: {(tryKept ? "OK" : "WRONG")}");

            // Check & repair with modelled damage: a 60 s stall of the right motor and 10 minutes of driving.
            Robot.RightMotor.Update(1.05, 4.0, 60);
            Robot.Battery.Drain(0.5, 600);
            RefreshCard();
            OnAction("act.repair");
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-repair"));
            bool burnt = Robot.RightMotor.Burnt;
            double charge = Robot.Battery.StateOfCharge * 100;
            Robot.RightMotor.Replace();
            Robot.Battery.Replace();
            AfterRobotChanged(false);
            report.AppendLine($"  check & repair: a modelled 60 s stall at 1.05 A burnt the right motor: {(burnt ? "yes" : "no")}; 10 min at 0.5 A left {charge:F1} % battery; " +
                              $"after Replace: motor {(Robot.RightMotor.Burnt ? "still burnt" : "OK")}, battery {Robot.Battery.StateOfCharge * 100:F0} %");
            CloseSide();

            // Code: upload the sketch with the real toolchain; the arena then runs the new hex.
            OpenCode();
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-code"));
            Upload();
            float waitStart = Time.realtimeSinceStartup;
            while (compileTask != null && Time.realtimeSinceStartup - waitStart < 180f) yield return null;
            yield return Frames(2);
            report.AppendLine("  upload from the Code window (arduino-cli, worker thread): " + lastCompileSummary);
            CloseOverlay();

            for (int language = 1; language <= 2; language++)
            {
                SpikeStrings.SetLanguage(language);
                yield return Frames(3);
                yield return SpikeReport.Capture(SpikeReport.Shot("garage-" + SpikeStrings.LanguageCodes[language]));
            }
            SpikeStrings.SetLanguage(0);

            Select(1);
            yield return Frames(5);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-holed"));
            Select(0);
            yield return Frames(3);
            report.AppendLine("  screenshots: -garage-en, -garage-customize, -garage-repair, -garage-code, -garage-uz, -garage-ru, -garage-holed");

            // Build, Wire and Body: a new robot made from nothing; START takes that robot to the arena.
            yield return BuildFromScratch();

            SpikeReport.Stage = 1;
            StartRun();
        }

        IEnumerator AfterRun()
        {
            var report = SpikeReport.Text;
            double loadMs = SpikeReport.Transition?.Elapsed.TotalMilliseconds ?? 0;
            yield return Frames(20);
            var robot = Robot;
            report.AppendLine("back in the garage after the arena run:");
            report.AppendLine($"  garage loaded in {loadMs:F0} ms; battery {robot.Battery.StateOfCharge * 100:F2} %; motor peaks left {robot.LeftMotor.PeakC:F1} °C, " +
                              $"right {robot.RightMotor.PeakC:F1} °C; burnt: {(robot.LeftMotor.Burnt || robot.RightMotor.Burnt ? "yes" : "no")}");
            OnAction("act.repair");
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-return"));
            report.AppendLine("  screenshot: -garage-return");
            SpikeReport.Finish();
        }

        public int ThumbnailAttempts { get; private set; }
        static Texture2D? probe;

        /// <summary>True when something was drawn into the texture: its corner has the opaque clear colour.</summary>
        static bool HasPixels(RenderTexture texture)
        {
            probe ??= new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = previous;
            return probe.GetPixel(0, 0).a > 0.5f;
        }

        /// <summary>Writes a thumbnail to a PNG and reports its centre colour (the background is 0.17, 0.19, 0.23).</summary>
        string SaveThumbnail(int index, string path)
        {
            var image = index < thumbnails.Count ? thumbnails[index] : null;
            if (image == null) return "not rendered";
            File.WriteAllBytes(path, image.EncodeToPNG());
            var centre = image.GetPixel(image.width / 2, image.height / 2);
            return $"{image.width}x{image.height}, centre colour ({centre.r:F2}, {centre.g:F2}, {centre.b:F2}), saved as {Path.GetFileName(path)}";
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        // ------------------------------------------------------------------ helpers

        Material Mat(Color color, float smoothness)
        {
            var material = new Material(litMaterial);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            roomMaterials.Add(material);
            return material;
        }

        static void Box(string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(go.GetComponent<Collider>());
        }

        static void Cylinder(string name, Transform parent, Vector3 position, float diameter, float height, Material material, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = new Vector3(diameter, height / 2, diameter);
            go.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(go.GetComponent<Collider>());
        }

        static VisualElement Layout(string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        static T Classed<T>(T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }

        T Localized<T>(T element, string key) where T : TextElement
        {
            localized.Add((element, key));
            element.text = Tr(key);
            return element;
        }
    }
}
