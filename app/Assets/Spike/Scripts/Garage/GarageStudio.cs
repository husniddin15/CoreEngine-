using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>Marks the collider of a Body Studio shape, so a click on the robot finds the shape.</summary>
    public sealed class StudioShape : MonoBehaviour
    {
        public string Id = "";
        public bool Hole;
    }

    /// <summary>
    /// The Body Studio (docs/08, ADR-0005): where the player builds the whole robot, as in Tinkercad. A new robot
    /// starts with nothing. The body is made of shapes, each of a real material (PLA, acrylic, plywood,
    /// cardboard, EVA foam, foam board, aluminium), moved, turned and sized with handles or typed in millimetres;
    /// holes cut the shapes they are grouped with. Real parts (the Uno, the L298N, motors, the sensor, the
    /// battery holder, the caster) are placed and turned the same way but keep their size, and land on the
    /// surface the mouse points at: a board stands on a plate, a motor or the caster hangs under it. Every
    /// change edits the robot's <see cref="RobotDesign"/>; Manifold rebuilds the body on the worker thread
    /// (GarageEdit's FlushBody) while each shape's see-through "ghost" follows the mouse at once.
    /// </summary>
    public sealed partial class GarageSpike
    {
        public Material? overlayMaterial;  // CoreEngine/StudioOverlay (SpikeSetup): see-through shapes and handles
        public Material? gridMaterial;     // CoreEngine/StudioGrid: the workplane's grid
        public Material? acrylicMaterial;  // transparent URP Lit: acrylic shapes (BodyLook)
        public Material? partMaterial;     // URP Lit with normal, metallic and emission maps on: the part models (PartLooks)

        /// <summary>What a drag holds: the item itself, or one of its Tinkercad handles (GarageHandles.cs).</summary>
        enum Grip { None, Shape, Corner, Edge, Top, Lift, Turn }

        enum LibraryTab { Shapes, Parts }

        /// <summary>Something in the Studio: a body shape or group (by feature id), or a part (by part id).</summary>
        readonly struct Pick : IEquatable<Pick>
        {
            public readonly bool Part;
            public readonly string Id;

            public Pick(bool part, string id)
            {
                Part = part;
                Id = id;
            }

            public bool Equals(Pick other) => other.Part == Part && other.Id == Id;
            public override bool Equals(object? obj) => obj is Pick other && Equals(other);
            public override int GetHashCode() => (Part, Id).GetHashCode();
        }

        const float StudioMm = 0.001f;
        const int MaxImportTriangles = 200000;

        static readonly FeatureKind[] PaletteKinds =
            { FeatureKind.Plate, FeatureKind.Box, FeatureKind.RoundedBox, FeatureKind.Cylinder, FeatureKind.Cone, FeatureKind.Sphere, FeatureKind.Wedge, FeatureKind.Tube };
        static readonly BodyMaterial[] MaterialOrder =
            { BodyMaterial.Pla, BodyMaterial.Acrylic, BodyMaterial.Plywood, BodyMaterial.Cardboard, BodyMaterial.EvaFoam, BodyMaterial.FoamBoard, BodyMaterial.Aluminium };
        static readonly string[] ColourSwatches = { "#F4F4F0", "#1E1F22", "#D8352A", "#F07A1A", "#F2C418", "#2FA84F", "#2F6FD8", "#7B4BC9", "#E64D93", "#8A8F98" };
        static readonly float[] SnapSteps = { 1, 5, 10 };

        // Chrome: the Garage's own parts are hidden while the Studio is open, and its side panel moves in.
        readonly List<VisualElement> garageChrome = new List<VisualElement>();
        VisualElement rightColumn = null!;
        VisualElement? studio;
        VisualElement studioViewport = null!, studioRight = null!, studioTop = null!, studioMain = null!, studioStatusBar = null!, libraryBody = null!;
        Label studioTitle = null!, studioStatus = null!, studioStats = null!;
        readonly Dictionary<LibraryTab, Button> tabButtons = new Dictionary<LibraryTab, Button>();
        readonly List<Button> snapButtons = new List<Button>();
        readonly List<Button> paletteButtons = new List<Button>();
        readonly Dictionary<string, Button> partButtons = new Dictionary<string, Button>();
        Button solidButton = null!, holeButton = null!, partsButton = null!, drawButton = null!, undoButton = null!, redoButton = null!;

        // What the Studio is doing
        LibraryTab libraryTab = LibraryTab.Shapes;
        float studioSnap = 5;
        bool studioHoles, studioShowParts = true, keepProportions, projectionShifted;
        BodyMaterial studioMaterial = BodyMaterial.Pla;
        string studioColour = "";
        readonly List<Pick> selection = new List<Pick>();
        Pick? hovered;
        float lastNudge = -10;

        // An item riding under the mouse after a click in the library, until a click sets it down
        Pick? carrying;
        RobotDesign? carryBefore;
        bool swallowClick;

        // The shapes' ghosts, the handles and the grid, under the robot's anchor (the chassis frame)
        sealed class Ghost
        {
            public Transform Outer = null!, Inner = null!;
            public MeshRenderer Renderer = null!;
            public BodyFeature Built = null!;
        }

        Transform? studioScene, grid;
        readonly Dictionary<string, Ghost> ghosts = new Dictionary<string, Ghost>();
        Material? ghostHole, ghostHoleSelected, ghostSelected, ghostHover, lineMaterial;

        // A drag of a handle or of an item
        (Grip grip, int axis, int sign) hot, drag;
        RobotDesign? studioBefore;
        bool studioDragChanged;
        Vector3 dragPivot, dragDirection, dragFrom, dragPivotMm;
        Vector3 grabOffset; // a part dragged by a point off its mounting face keeps that point under the mouse (mm)
        float dragT0;
        Vector2 dragMouse0;
        string dragReadout = "";

        // Drawing an outline on the workplane
        bool drawing;
        readonly List<Vector2> drawPoints = new List<Vector2>();
        LineRenderer? drawLine;
        readonly List<Transform> drawDots = new List<Transform>();

        // Inspector fields updated while a handle moves: position, rotation, size
        readonly FloatField?[] vecFields = new FloatField?[9];

        // Triangle counts of uploaded models by path, so the inspector does not read a large file on every redraw
        readonly Dictionary<string, int> triangleCounts = new Dictionary<string, int>();

        Pick? Primary => selection.Count > 0 ? selection[selection.Count - 1] : (Pick?)null;

        /// <summary>The selected shape or group, when the selection is exactly one of them.</summary>
        BodyFeature? SelectedFeature => selection.Count == 1 && !selection[0].Part ? Design.Body.Feature(selection[0].Id) : null;

        /// <summary>The selected part, when the selection is exactly one.</summary>
        PartInstance? SelectedPart => selection.Count == 1 && selection[0].Part ? Design.Find(selection[0].Id) : null;

        // ------------------------------------------------------------------ opening and closing

        void OpenStudio(LibraryTab tab)
        {
            if (studio == null) BuildStudioUi();
            foreach (var element in garageChrome) element.style.display = DisplayStyle.None;
            studio!.style.display = DisplayStyle.Flex;
            studioRight.Add(sidePanel);
            sidePanel.AddToClassList("side-panel--studio");
            libraryTab = tab;
            selection.Clear();
            hovered = null;
            drawing = false;
            carrying = null;
            drag = default;
            EnsureStudioScene();
            RenderLibrary();
            RefreshStudioChrome();
        }

        void CloseStudio()
        {
            CancelCarry();
            CancelDrawing();
            EndStudioDrag();
            if (studio != null) studio.style.display = DisplayStyle.None;
            foreach (var element in garageChrome) element.style.display = DisplayStyle.Flex;
            sidePanel.RemoveFromClassList("side-panel--studio");
            rightColumn.Add(sidePanel);
            DestroyStudioScene();
            if (projectionShifted) view.ResetProjectionMatrix();
            projectionShifted = false;
            selection.Clear();
            hovered = null;
            Array.Clear(vecFields, 0, vecFields.Length);
        }

        // ------------------------------------------------------------------ chrome

        void BuildStudioUi()
        {
            studio = Layout("studio");

            var top = new VisualElement();
            top.AddToClassList("studio-top");
            studioTop = top;
            top.Add(StudioButton(Icon.Back, "studio.back", CloseSide, "studio-back"));
            studioTitle = Classed(new Label(), "studio-title");
            top.Add(studioTitle);

            var history = Layout("studio-group");
            undoButton = StudioButton(Icon.Undo, null, Undo);
            redoButton = StudioButton(Icon.Redo, null, Redo);
            history.Add(undoButton);
            history.Add(redoButton);
            top.Add(history);

            var snap = Layout("studio-group");
            snap.Add(Classed(Localized(new Label(), "studio.snap"), "studio-caption"));
            foreach (float step in SnapSteps)
            {
                float chosen = step;
                var button = new Button(() => SetSnap(chosen)) { text = step.ToString("0"), focusable = false };
                button.AddToClassList("snap-button");
                snapButtons.Add(button);
                snap.Add(button);
            }
            snap.Add(Classed(Localized(new Label(), "unit.mm"), "studio-caption"));
            top.Add(snap);

            var files = Layout("studio-group");
            drawButton = StudioButton(Icon.Draw, "studio.draw", ToggleDrawing);
            files.Add(drawButton);
            files.Add(StudioButton(Icon.Import, "studio.import", ImportFromDialog));
            files.Add(StudioButton(Icon.Export, "studio.export", () => ExportStl(null)));
            top.Add(files);

            top.Add(Layout("spacer"));
            partsButton = StudioButton(Icon.Eye, "studio.parts", ToggleParts);
            top.Add(partsButton);
            studio.Add(top);

            var main = Layout("studio-main");
            studioMain = main;
            var palette = new VisualElement();
            palette.AddToClassList("studio-palette");
            var tabs = Layout("library-tabs");
            foreach (var (tab, key) in new[] { (LibraryTab.Shapes, "studio.tabShapes"), (LibraryTab.Parts, "studio.tabParts") })
            {
                var chosen = tab;
                var button = new Button(() => SetLibraryTab(chosen)) { focusable = false };
                button.AddToClassList("library-tab");
                button.Add(Localized(new Label(), key));
                tabButtons[tab] = button;
                tabs.Add(button);
            }
            palette.Add(tabs);
            var scroll = new ScrollView();
            scroll.AddToClassList("library-scroll");
            libraryBody = scroll;
            palette.Add(scroll);
            main.Add(palette);
            studioViewport = Layout("studio-viewport");
            main.Add(studioViewport);
            BuildHandleOverlay();
            studioRight = Layout("studio-right");
            main.Add(studioRight);
            studio.Add(main);

            var status = new VisualElement();
            status.AddToClassList("studio-status");
            studioStatusBar = status;
            studioStatus = Classed(new Label(), "studio-status-text");
            status.Add(studioStatus);
            status.Add(Layout("spacer"));
            studioStats = Classed(new Label(), "studio-stats");
            status.Add(studioStats);
            studio.Add(status);

            studio.style.display = DisplayStyle.None;
            root.Insert(root.IndexOf(toast), studio); // the toast, pages and the tooltip stay on top
        }

        /// <summary>The library under its tabs: shapes with material and colour, or the catalogue's parts.</summary>
        void RenderLibrary()
        {
            if (studio == null) return;
            libraryBody.Clear();
            paletteButtons.Clear();
            partButtons.Clear();
            foreach (var entry in tabButtons) entry.Value.EnableInClassList("library-tab--active", entry.Key == libraryTab);
            if (libraryTab == LibraryTab.Parts)
            {
                libraryBody.Add(Classed(new Label(Tr("studio.partsNote")), "palette-note"));
                var grid = Layout("shape-grid");
                foreach (var def in PartCatalog.All)
                {
                    string id = def.Id;
                    var button = ShapeButton(PartIcon(def.Kind), def.Name, () => AddPartFromLibrary(id), literal: true);
                    button.AddToClassList("part-button");
                    button.Add(Classed(new Label(), "part-count"));
                    partButtons[id] = button;
                    grid.Add(button);
                }
                libraryBody.Add(grid);
                RefreshPartCounts();
                return;
            }

            libraryBody.Add(Classed(new Label(Tr("studio.addAs")), "palette-note"));
            var modes = Layout("palette-switch");
            solidButton = SwitchButton(Icon.Solid, "studio.solid", () => SetHoleMode(false));
            holeButton = SwitchButton(Icon.Hole, "studio.hole", () => SetHoleMode(true));
            modes.Add(solidButton);
            modes.Add(holeButton);
            libraryBody.Add(modes);

            libraryBody.Add(Classed(new Label(Tr("studio.material")), "palette-title"));
            libraryBody.Add(MaterialPicker(studioMaterial, studioColour, (material, colour) =>
            {
                studioMaterial = material;
                studioColour = colour;
                RenderLibrary();
            }));

            libraryBody.Add(Classed(new Label(Tr("studio.shapes")), "palette-title"));
            var shapes = Layout("shape-grid");
            foreach (var kind in PaletteKinds)
            {
                var chosen = kind;
                var button = ShapeButton(IconFor(kind), KindKey(kind), () => AddShape(chosen));
                paletteButtons.Add(button);
                shapes.Add(button);
            }
            libraryBody.Add(shapes);
            libraryBody.Add(Classed(new Label(Tr("studio.more")), "palette-title"));
            var own = Layout("shape-grid");
            var draw = ShapeButton(Icon.Draw, "shape.draw", StartDrawing);
            var upload = ShapeButton(Icon.Import, "shape.import", ImportFromDialog);
            paletteButtons.Add(draw);
            paletteButtons.Add(upload);
            own.Add(draw);
            own.Add(upload);
            libraryBody.Add(own);
            RefreshStudioChrome();
        }

        /// <summary>Seven materials, and colour swatches for the ones sold in colours.</summary>
        VisualElement MaterialPicker(BodyMaterial current, string colour, Action<BodyMaterial, string> choose)
        {
            var box = Layout("material-picker");
            var row = Layout("material-row");
            foreach (var material in MaterialOrder)
            {
                var chosen = material;
                var button = new Button(() => choose(chosen, BodyLook.Coloured(chosen) && BodyLook.Coloured(current) ? colour : "")) { focusable = false };
                button.AddToClassList("material-button");
                button.EnableInClassList("material-button--active", material == current);
                var chip = Layout("material-chip");
                chip.style.backgroundColor = BodyLook.ColourOf(material, material == current ? colour : "");
                if (material == BodyMaterial.Acrylic) chip.AddToClassList("material-chip--clear");
                button.Add(chip);
                button.Add(Classed(new Label(Tr(MaterialKey(material))), "material-name"));
                row.Add(button);
            }
            box.Add(row);
            if (BodyLook.Coloured(current))
            {
                var swatches = Layout("colour-row");
                if (current == BodyMaterial.Acrylic)
                {
                    var clear = new Button(() => choose(current, "")) { text = Tr("studio.clear"), focusable = false };
                    clear.AddToClassList("colour-clear");
                    clear.EnableInClassList("colour-swatch--active", colour.Length == 0);
                    swatches.Add(clear);
                }
                foreach (string hex in ColourSwatches)
                {
                    string chosen = hex;
                    var swatch = new Button(() => choose(current, chosen)) { focusable = false };
                    swatch.AddToClassList("colour-swatch");
                    swatch.EnableInClassList("colour-swatch--active", string.Equals(colour, hex, StringComparison.OrdinalIgnoreCase));
                    swatch.style.backgroundColor = BodyLook.ColourOf(current, hex);
                    swatches.Add(swatch);
                }
                box.Add(swatches);
            }
            box.Add(Classed(new Label(SpikeStrings.Format("studio.density", BodyDesign.DensityGPerCm3(current))), "material-note"));
            return box;
        }

        Button StudioButton(Icon icon, string? key, Action onClick, string? extraClass = null)
        {
            var button = new Button(onClick) { focusable = false };
            button.AddToClassList("tool-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            button.Add(Classed(new IconView(icon), "tool-icon"));
            if (key != null) button.Add(Classed(Localized(new Label(), key), "tool-label"));
            else button.AddToClassList("tool-button--icon");
            return button;
        }

        Button SwitchButton(Icon icon, string key, Action onClick)
        {
            var button = new Button(onClick) { focusable = false };
            button.AddToClassList("switch-button");
            button.Add(Classed(new IconView(icon), "switch-icon"));
            button.Add(new Label(Tr(key)));
            return button;
        }

        Button ShapeButton(Icon icon, string key, Action onClick, bool literal = false)
        {
            var button = new Button(onClick) { focusable = false };
            button.AddToClassList("shape-button");
            button.Add(Classed(new IconView(icon), "shape-icon"));
            button.Add(Classed(new Label(literal ? key : Tr(key)), "shape-label"));
            return button;
        }

        void RefreshPartCounts()
        {
            foreach (var entry in partButtons)
            {
                var def = PartCatalog.Get(entry.Key);
                if (def == null) continue;
                int count = Design.Count(entry.Key);
                entry.Value.Q<Label>(className: "part-count").text = SpikeStrings.Format("studio.partCount", count, def.MaxCount, def.MassG);
                entry.Value.SetEnabled(count < def.MaxCount);
            }
        }

        void RefreshStudioChrome()
        {
            if (studio == null) return;
            studioTitle.text = Tr("studio.title") + " · " + Robot.Name;
            for (int i = 0; i < snapButtons.Count; i++) snapButtons[i].EnableInClassList("snap-button--active", Mathf.Approximately(SnapSteps[i], studioSnap));
            if (libraryTab == LibraryTab.Shapes && solidButton != null)
            {
                solidButton.EnableInClassList("switch-button--active", !studioHoles);
                holeButton.EnableInClassList("switch-button--active", studioHoles);
                foreach (var button in paletteButtons) button.EnableInClassList("shape-button--hole", studioHoles);
            }
            if (libraryTab == LibraryTab.Parts) RefreshPartCounts();
            partsButton.EnableInClassList("tool-button--active", studioShowParts);
            drawButton.EnableInClassList("tool-button--active", drawing);
            undoButton.SetEnabled(undo.Count > 0);
            redoButton.SetEnabled(redo.Count > 0);
            UpdateStudioStatus();
        }

        void UpdateStudioStatus()
        {
            if (studio == null) return;
            string text;
            var f = SelectedFeature;
            var part = SelectedPart;
            if (carrying != null) text = Tr("studio.hint.carry");
            else if (drawing) text = Tr("studio.hint.draw");
            else if (part != null) text = PartName(part) + "   ·   " + Tr("studio.hint.part");
            else if (f != null && f.Kind != FeatureKind.Group)
                text = SpikeStrings.Format("studio.selected", FeatureName(f), f.Hole ? "(" + Tr("studio.holeTag") + ")" : "",
                    f.SizeX, f.SizeY, f.SizeZ, f.X, f.Y, f.Z) + "   ·   " + Tr("studio.hint.shape");
            else if (f != null) text = FeatureName(f) + "   ·   " + Tr("studio.hint.oneGroup");
            else if (selection.Count > 1) text = SpikeStrings.Format("studio.selectedMany", selection.Count) + "   ·   " + Tr("studio.hint.group");
            else text = Tr("studio.hint.none");
            float lowest = DesignGeometry.LowestPoint(Design);
            if (lowest < -1 && carrying == null) text = SpikeStrings.Format("studio.below", -lowest) + "   ·   " + text;
            studioStatus.text = text;
            var meshes = shown?.Body;
            double grams = Design.MassKg() * 1000;
            if (meshes != null) grams += meshes.MassG - DesignGeometry.BodyMassG(Design.Body);
            studioStats.text = SpikeStrings.Format("studio.stats", (meshes?.VolumeMm3 ?? 0) / 1000.0, grams, meshes?.BuildMs ?? 0);
        }

        void SetSnap(float step)
        {
            studioSnap = step;
            RefreshStudioChrome();
        }

        void SetHoleMode(bool holes)
        {
            studioHoles = holes;
            RefreshStudioChrome();
        }

        void SetLibraryTab(LibraryTab tab)
        {
            libraryTab = tab;
            RenderLibrary();
        }

        void ToggleParts()
        {
            studioShowParts = !studioShowParts;
            ApplyPartsVisibility();
            RefreshStudioChrome();
        }

        void ApplyPartsVisibility()
        {
            if (shown == null) return;
            bool show = mode != EditMode.Body || studioShowParts;
            foreach (var part in shown.Parts.Values) part.SetActive(show);
            var wires = shown.Root.transform.Find("Wires");
            if (wires != null) wires.gameObject.SetActive(show);
        }

        /// <summary>
        /// Centres the camera's picture in the free space between the library and the inspector, with an
        /// off-centre projection (as a shift lens does), so the robot is not hidden behind a panel.
        /// </summary>
        void ApplyStudioProjection()
        {
            bool open = mode == EditMode.Body && studio != null && studio.style.display == DisplayStyle.Flex && root.panel != null;
            if (!open)
            {
                if (projectionShifted) view.ResetProjectionMatrix();
                projectionShifted = false;
                return;
            }
            var area = studioViewport.worldBound;
            var panel = root.panel!.visualTree.layout.size;
            if (area.width < 10 || panel.x < 10 || float.IsNaN(area.width)) return;
            float cx = area.center.x / panel.x * 2 - 1, cy = 1 - area.center.y / panel.y * 2;
            view.ResetProjectionMatrix();
            var m = view.projectionMatrix;
            m.m02 = -cx; // a point on the view axis lands at x = -m02 in normalised device coordinates
            m.m12 = -cy;
            view.projectionMatrix = m;
            projectionShifted = true;
        }

        // ------------------------------------------------------------------ the scene: grid, ghosts, handles

        void EnsureStudioScene()
        {
            if (studioScene != null) return;
            studioScene = new GameObject("StudioScene").transform;
            studioScene.SetParent(robotAnchor, false);
            if (gridMaterial != null)
            {
                grid = new GameObject("Workplane").transform;
                grid.SetParent(studioScene, false);
                grid.localPosition = new Vector3(0, 0.0002f, 0);
                grid.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Square;
                var renderer = grid.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = gridMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            if (ghostHole == null)
            {
                ghostHole = OverlayMat(new Color(0.80f, 0.84f, 0.92f, 0.30f), false);
                ghostHoleSelected = OverlayMat(new Color(0.31f, 0.76f, 1.0f, 0.42f), false);
                ghostSelected = OverlayMat(new Color(0.31f, 0.76f, 1.0f, 0.30f), false);
                ghostHover = OverlayMat(new Color(1f, 1f, 1f, 0.16f), false);
                lineMaterial = OverlayMat(new Color(0.31f, 0.76f, 1.0f, 1f), true);
                lineMaterial.SetFloat("_Shade", 0);
                lineMaterial.SetFloat("_Rim", 0);
            }
        }

        Material OverlayMat(Color colour, bool onTop)
        {
            Material material;
            if (overlayMaterial != null)
            {
                material = new Material(overlayMaterial);
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_ZTest", (float)(onTop ? CompareFunction.Always : CompareFunction.LessEqual));
                if (onTop)
                {
                    material.SetFloat("_OffsetFactor", 0);
                    material.SetFloat("_OffsetUnits", 0);
                    material.renderQueue = 3100; // after the see-through shapes
                }
            }
            else
            {
                material = new Material(litMaterial); // a project set up before the Studio: opaque, but usable
                material.SetColor("_BaseColor", colour);
            }
            roomMaterials.Add(material); // destroyed with the Garage
            return material;
        }

        void DestroyStudioScene()
        {
            if (studioScene != null) Destroy(studioScene.gameObject);
            studioScene = null;
            grid = null;
            ghosts.Clear();
            spots.Clear();
            if (drawLine != null) Destroy(drawLine.gameObject);
            drawLine = null;
            foreach (var dot in drawDots) if (dot != null) Destroy(dot.gameObject);
            drawDots.Clear();
        }

        /// <summary>
        /// New ghosts for the model just shown: every shape's own mesh, which a click picks and which shows a hole
        /// or the selection see-through. The meshes belong to the model, so ghosts are made again with it.
        /// </summary>
        void RebuildGhosts()
        {
            if (studioScene == null || shown?.Body == null) return;
            foreach (var ghost in ghosts.Values) Destroy(ghost.Outer.gameObject);
            ghosts.Clear();
            var body = shown.Body;
            foreach (var (id, mesh, hole) in body.Features) AddGhost(id, mesh, hole, body.Source);
            for (int i = 0; i < body.Loose.Count && i < body.NotClosed.Count; i++) AddGhost(body.NotClosed[i], body.Loose[i], false, body.Source);
            ApplyPartsVisibility();
            if (SelectedPart != null) shown.Highlight(SelectedPart.Id);
            UpdateStudioScene();
            UpdateStudioStatus();
        }

        void AddGhost(string id, Mesh mesh, bool hole, BodyDesign? source)
        {
            var built = source?.Feature(id);
            if (built == null || studioScene == null) return;
            var outer = new GameObject("Shape " + id).transform;
            outer.SetParent(studioScene, false);
            var inner = new GameObject("Mesh").transform;
            inner.SetParent(outer, false);
            inner.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = inner.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            // A perforated plate is picked and built on by its outline (the hull of its mesh): parts sit on its
            // surface, as real ones do, and a mouse over one of its 3 mm holes does not fall in.
            var collider = inner.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = built.Kind == FeatureKind.Plate && built.Pitch >= 5;
            var marker = inner.gameObject.AddComponent<StudioShape>();
            marker.Id = id;
            marker.Hole = hole;
            ghosts[id] = new Ghost { Outer = outer, Inner = inner, Renderer = renderer, Built = built.Clone() };
        }

        /// <summary>
        /// Every frame in the Studio: each ghost moves from where its mesh was built to where the shape is now
        /// (the parent carries the new place, turn and size; the child undoes the old place and turn), the handles
        /// follow the selection at a constant size on the screen, and the item being carried rides the mouse.
        /// </summary>
        void UpdateStudioScene()
        {
            if (studioScene == null || mode != EditMode.Body) return;
            var body = Design.Body;
            var selectedShapes = new HashSet<string>();
            foreach (var pick in selection)
                if (!pick.Part) foreach (string id in ShapeIds(pick)) selectedShapes.Add(id);
            var hoveredShapes = new HashSet<string>();
            if (hovered is { Part: false } h) foreach (string id in ShapeIds(h)) hoveredShapes.Add(id);
            foreach (var entry in ghosts)
            {
                var ghost = entry.Value;
                var now = body.Feature(entry.Key);
                if (now == null)
                {
                    ghost.Outer.gameObject.SetActive(false);
                    continue;
                }
                ghost.Outer.gameObject.SetActive(true);
                var b = ghost.Built;
                var turn = Quaternion.Inverse(Quaternion.Euler(b.RotX, b.RotY, b.RotZ));
                ghost.Inner.localRotation = turn;
                ghost.Inner.localPosition = turn * (-new Vector3(b.X, b.Y, b.Z) * StudioMm);
                ghost.Outer.localPosition = new Vector3(now.X, now.Y, now.Z) * StudioMm;
                ghost.Outer.localRotation = Quaternion.Euler(now.RotX, now.RotY, now.RotZ);
                ghost.Outer.localScale = new Vector3(Ratio(now.SizeX, b.SizeX), Ratio(now.SizeY, b.SizeY), Ratio(now.SizeZ, b.SizeZ));
                bool selected = selectedShapes.Contains(entry.Key), hovering = hoveredShapes.Contains(entry.Key) && drag.grip == Grip.None;
                var material = now.Hole ? (selected ? ghostHoleSelected : ghostHole) : selected ? ghostSelected : hovering ? ghostHover : null;
                ghost.Renderer.enabled = material != null;
                if (material != null) ghost.Renderer.sharedMaterial = material;
            }
            if (carrying != null && !IsPointerOverUi(input.Position)) Carry(input.Position);
            UpdateGizmo();
            UpdateDrawPreview();
        }

        static float Ratio(float now, float built) => built > 1e-4f ? now / built : 1;

        Vector3 WorldOf(Vector3 mm) => robotAnchor.TransformPoint(mm * StudioMm);

        Vector3 AxisWorld(int axis) => robotAnchor.TransformDirection(Unit(axis));

        static Vector3 Unit(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        static float Component(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        static Vector3 V((float x, float y, float z) t) => new Vector3(t.x, t.y, t.z);

        // ------------------------------------------------------------------ items: shapes, groups and parts

        bool Exists(Pick p) => p.Part ? Design.Find(p.Id) != null : Design.Body.Feature(p.Id) != null;

        /// <summary>The ids of the shapes a pick moves: the shape itself, or every shape in a group.</summary>
        List<string> ShapeIds(Pick p)
        {
            var ids = new List<string>();
            if (p.Part) return ids;
            var feature = Design.Body.Feature(p.Id);
            if (feature == null) return ids;
            if (feature.Kind != FeatureKind.Group) ids.Add(feature.Id);
            else foreach (var shape in Design.Body.Shapes(feature)) ids.Add(shape.Id);
            return ids;
        }

        /// <summary>An item's box in the chassis frame (mm): a shape's turned box, a group's shapes, a part with its wheel.</summary>
        (Vector3 min, Vector3 max) BoundsOf(Pick p)
        {
            if (p.Part)
            {
                var part = Design.Find(p.Id);
                if (part == null) return (Vector3.zero, Vector3.zero);
                var (min, max) = DesignGeometry.PartBounds(part);
                return (V(min), V(max));
            }
            var lo = Vector3.positiveInfinity;
            var hi = Vector3.negativeInfinity;
            foreach (string id in ShapeIds(p))
            {
                var (min, max) = DesignGeometry.FeatureBounds(Design.Body.Feature(id)!);
                lo = Vector3.Min(lo, V(min));
                hi = Vector3.Max(hi, V(max));
            }
            return float.IsInfinity(lo.x) ? (Vector3.zero, Vector3.zero) : (lo, hi);
        }

        /// <summary>Where an item's handles sit and what it turns about (mm): a shape's or part's own origin, a group's middle.</summary>
        Vector3 PivotOf(Pick p)
        {
            if (p.Part)
            {
                var part = Design.Find(p.Id);
                return part == null ? Vector3.zero : new Vector3(part.X, part.Y, part.Z);
            }
            var f = Design.Body.Feature(p.Id);
            if (f == null) return Vector3.zero;
            if (f.Kind != FeatureKind.Group) return new Vector3(f.X, f.Y, f.Z);
            var (min, max) = BoundsOf(p);
            return (min + max) / 2;
        }

        /// <summary>Moves an item by a step from where it was when the drag began (the snapshot).</summary>
        void MoveItem(Pick p, Vector3 delta, RobotDesign from)
        {
            if (p.Part)
            {
                var start = from.Find(p.Id);
                var part = Design.Find(p.Id);
                if (start == null || part == null) return;
                (part.X, part.Y, part.Z) = (start.X + delta.x, start.Y + delta.y, start.Z + delta.z);
                shown?.MovePart(Design, part.Id);
                return;
            }
            foreach (string id in ShapeIds(p))
            {
                var start = from.Body.Feature(id);
                var f = Design.Body.Feature(id);
                if (start == null || f == null) continue;
                (f.X, f.Y, f.Z) = (start.X + delta.x, start.Y + delta.y, start.Z + delta.z);
            }
        }

        /// <summary>Turns an item about a point (mm, chassis frame) from where it was when the drag began.</summary>
        void TurnItem(Pick p, Quaternion turn, Vector3 pivotMm, RobotDesign from)
        {
            if (p.Part)
            {
                var start = from.Find(p.Id);
                var part = Design.Find(p.Id);
                if (start == null || part == null) return;
                var position = pivotMm + turn * (new Vector3(start.X, start.Y, start.Z) - pivotMm);
                var euler = (turn * Quaternion.Euler(start.RotX, start.Rotation, start.RotZ)).eulerAngles;
                (part.X, part.Y, part.Z) = (position.x, position.y, position.z);
                (part.RotX, part.Rotation, part.RotZ) = (Tidy(euler.x), Tidy(euler.y), Tidy(euler.z));
                shown?.MovePart(Design, part.Id);
                return;
            }
            foreach (string id in ShapeIds(p))
            {
                var start = from.Body.Feature(id);
                var f = Design.Body.Feature(id);
                if (start == null || f == null) continue;
                var position = pivotMm + turn * (new Vector3(start.X, start.Y, start.Z) - pivotMm);
                var euler = (turn * Quaternion.Euler(start.RotX, start.RotY, start.RotZ)).eulerAngles;
                (f.X, f.Y, f.Z) = (position.x, position.y, position.z);
                (f.RotX, f.RotY, f.RotZ) = (Tidy(euler.x), Tidy(euler.y), Tidy(euler.z));
            }
        }

        string ItemName(Pick p)
        {
            if (p.Part) return Design.Find(p.Id) is { } part ? PartName(part) : p.Id;
            return Design.Body.Feature(p.Id) is { } f ? FeatureName(f) : p.Id;
        }

        static string PartName(PartInstance part) => PartCatalog.Get(part.Part)?.Name ?? part.Part;

        // ------------------------------------------------------------------ handles

        /// <summary>Size handles belong to a single shape; parts keep their real size and groups keep theirs here.</summary>
        BodyFeature? SizedShape => SelectedFeature is { Kind: not FeatureKind.Group } f ? f : null;

        // ------------------------------------------------------------------ picking

        Vector2 Screen2(Vector3 world) => view.WorldToScreenPoint(world);

        /// <summary>
        /// The item under the mouse, with the point hit: a part, or a shape (a grouped shape picks its whole group,
        /// as in Tinkercad). Holes are picked too; the carried item is skipped.
        /// </summary>
        Pick? ItemUnder(Vector2 mouse, out Vector3 point)
        {
            point = default;
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(view.ScreenPointToRay(mouse), 5f);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var shape = hit.collider.GetComponent<StudioShape>();
                if (shape != null)
                {
                    var feature = Design.Body.Feature(shape.Id);
                    if (feature == null) continue;
                    var pick = new Pick(false, Design.Body.TopLevel(feature).Id);
                    if (carrying != null && carrying.Value.Equals(pick)) continue;
                    point = hit.point;
                    return pick;
                }
                var part = hit.collider.GetComponentInParent<Pickable>();
                if (part != null && Design.Find(part.PartId) != null)
                {
                    var pick = new Pick(true, part.PartId);
                    if (carrying != null && carrying.Value.Equals(pick)) continue;
                    point = hit.point;
                    return pick;
                }
            }
            return null;
        }

        /// <summary>
        /// The surface under the mouse for placing an item: a solid shape of the body or another part, never the item
        /// itself or a hole. Its point and outward normal come back in the chassis frame (mm).
        /// </summary>
        bool SurfaceUnder(Ray ray, Pick self, out Vector3 point, out Vector3 normal, out Collider collider)
        {
            point = normal = default;
            collider = null!;
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(ray, 5f);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var ownShapes = new HashSet<string>(ShapeIds(self));
            foreach (var hit in hits)
            {
                var shape = hit.collider.GetComponent<StudioShape>();
                if (shape != null && (shape.Hole || ownShapes.Contains(shape.Id))) continue;
                var part = hit.collider.GetComponentInParent<Pickable>();
                if (shape == null && part == null) continue;
                if (part != null && self.Part && part.PartId == self.Id) continue;
                point = robotAnchor.InverseTransformPoint(hit.point) / StudioMm;
                normal = robotAnchor.InverseTransformDirection(hit.normal).normalized;
                collider = hit.collider;
                return true;
            }
            return false;
        }

        /// <summary>The point on the workplane (the ground, y = 0 of the chassis frame) under the mouse, in mm.</summary>
        bool WorkplanePoint(Vector2 mouse, out Vector3 mm)
        {
            mm = default;
            var ray = view.ScreenPointToRay(mouse);
            if (!new Plane(robotAnchor.up, robotAnchor.position).Raycast(ray, out float enter) || enter > 5f) return false;
            mm = robotAnchor.InverseTransformPoint(ray.GetPoint(enter)) / StudioMm;
            return true;
        }

        // ------------------------------------------------------------------ placing on surfaces

        /// <summary>
        /// Puts a part where the mouse points, as a builder would: its mounting face against the face under the
        /// mouse (a board stands on a plate, a sensor on its bracket), or, for a part that hangs by its top (a motor,
        /// the caster), under the plate the mouse points at. Over empty space it stands on the workplane as it is
        /// turned. Its turn about the face's normal is kept, so turning it once lasts across a drag.
        /// </summary>
        bool PlacePart(PartInstance part, Vector2 mouse, float step, Vector3 offset = default)
        {
            var def = PartCatalog.Get(part.Part);
            if (def == null) return false;
            var ray = view.ScreenPointToRay(mouse);
            var rotation = Quaternion.Euler(part.RotX, part.Rotation, part.RotZ);
            var mountNormal = V(def.MountNormal);
            var mountPoint = V(def.MountPoint);
            Vector3 position;
            if (SurfaceUnder(ray, new Pick(true, part.Id), out var point, out var n, out var collider))
            {
                bool hangs = mountNormal.y > 0.5f;
                if (hangs && n.y > 0.5f)
                {
                    // Under the plate: a ray from below, straight up at this spot, finds the plate's underside.
                    var from = robotAnchor.TransformPoint(new Vector3(point.x, point.y - 500, point.z) * StudioMm);
                    if (collider.Raycast(new Ray(from, robotAnchor.up), out var below, 1f))
                    {
                        point = robotAnchor.InverseTransformPoint(below.point) / StudioMm;
                        n = robotAnchor.InverseTransformDirection(below.normal).normalized;
                    }
                }
                rotation = Quaternion.FromToRotation(rotation * mountNormal, -n) * rotation;
                point -= offset - n * Vector3.Dot(offset, n); // along the face only
                if (Mathf.Abs(n.y) > 0.9f)
                {
                    point.x = Snap(point.x, step);
                    point.z = Snap(point.z, step);
                }
                position = point - rotation * mountPoint + n * 0.05f; // a hair off the face
            }
            else if (WorkplanePoint(mouse, out var ground))
            {
                position = new Vector3(Snap(ground.x - offset.x, step), part.Y, Snap(ground.z - offset.z, step));
            }
            else
            {
                return false;
            }
            var euler = rotation.eulerAngles;
            (part.X, part.Y, part.Z) = (position.x, position.y, position.z);
            (part.RotX, part.Rotation, part.RotZ) = (Tidy(euler.x), Tidy(euler.y), Tidy(euler.z));
            if (!SurfaceUnder(ray, new Pick(true, part.Id), out _, out _, out _))
                part.Y -= DesignGeometry.PartBounds(part).min.y; // on the workplane: its lowest point on y = 0
            shown?.MovePart(Design, part.Id);
            return true;
        }

        /// <summary>A shape lands on the top of the solid under the mouse, or on the workplane, at the mouse.</summary>
        bool PlaceShape(Pick p, Vector2 mouse, float step)
        {
            var ray = view.ScreenPointToRay(mouse);
            var (min, max) = BoundsOf(p);
            var pivot = PivotOf(p);
            Vector3 target;
            if (SurfaceUnder(ray, p, out var point, out var n, out _) && n.y > 0.5f)
                target = new Vector3(Snap(point.x, step), point.y + (pivot.y - min.y), Snap(point.z, step));
            else if (WorkplanePoint(mouse, out var ground))
                target = new Vector3(Snap(ground.x, step), pivot.y - min.y, Snap(ground.z, step));
            else
                return false;
            var delta = target - pivot;
            foreach (string id in ShapeIds(p))
            {
                var f = Design.Body.Feature(id)!;
                (f.X, f.Y, f.Z) = (f.X + delta.x, f.Y + delta.y, f.Z + delta.z);
            }
            return true;
        }

        // ------------------------------------------------------------------ carrying a new item

        void StartCarry(Pick p, RobotDesign before)
        {
            carrying = p;
            carryBefore = before;
            selection.Clear();
            selection.Add(p);
            if (p.Part) ShowRobot(); // the new part needs its model and collider
            else StudioChanged(rerender: true);
            renderSide?.Invoke();
            RefreshStudioChrome();
        }

        void Carry(Vector2 mouse)
        {
            var p = carrying!.Value;
            float step = input.Shift ? 0.1f : studioSnap;
            if (p.Part)
            {
                var part = Design.Find(p.Id);
                if (part != null) PlacePart(part, mouse, step);
            }
            else if (PlaceShape(p, mouse, step))
            {
                bodyDirty = true;
            }
        }

        /// <summary>A click sets the carried item down: one undo step for adding and placing it.</summary>
        void EndCarry()
        {
            if (carrying == null) return;
            var p = carrying.Value;
            carrying = null;
            if (carryBefore != null) RecordUndo(carryBefore);
            carryBefore = null;
            selection.Clear();
            if (Exists(p)) selection.Add(p);
            if (p.Part) ShowRobot();
            StudioChanged(rerender: true);
        }

        /// <summary>Esc while carrying: the item goes back to the library, the design is as it was.</summary>
        void CancelCarry()
        {
            if (carrying == null) return;
            carrying = null;
            if (carryBefore != null) Robot.Design = carryBefore;
            carryBefore = null;
            selection.Clear();
            DesignChanged();
        }

        // ------------------------------------------------------------------ mouse

        /// <summary>A press in the scene: sets a carried item down, starts a handle's drag, or picks and drags an item.</summary>
        bool BeginStudioPress(Vector2 mouse)
        {
            if (carrying != null)
            {
                EndCarry();
                swallowClick = true;
                return true;
            }
            if (drawing) return false; // points are placed on release, as clicks
            var handle = PickHandle(mouse);
            if (handle.grip != Grip.None)
            {
                StartStudioDrag(handle, mouse, default);
                return true;
            }
            var pick = ItemUnder(mouse, out var point);
            if (pick == null) return false;
            if (input.Shift)
            {
                // Shift+click adds to the selection (or takes away), as in Tinkercad; no drag.
                if (!selection.Remove(pick.Value)) selection.Add(pick.Value);
                swallowClick = true;
                SelectionChanged();
                return true;
            }
            if (!selection.Contains(pick.Value) || selection.Count > 1) Select(pick.Value);
            StartStudioDrag((Grip.Shape, 0, 0), mouse, point);
            return true;
        }

        void StartStudioDrag((Grip grip, int axis, int sign) handle, Vector2 mouse, Vector3 grabPoint)
        {
            var target = Primary;
            if (target == null) return;
            var p = target.Value;
            drag = handle;
            studioBefore = Design.Clone();
            studioDragChanged = false;
            dragMouse0 = mouse;
            dragPivotMm = PivotOf(p);
            dragPivot = WorldOf(dragPivotMm);
            dragReadout = "";
            var ray = view.ScreenPointToRay(mouse);
            switch (handle.grip)
            {
                case Grip.Shape:
                    dragFrom = grabPoint;
                    grabOffset = Vector3.zero;
                    if (p.Part && Design.Find(p.Id) is { } part && PartCatalog.Get(part.Part) is { } def)
                    {
                        // Where the mouse meets the surface behind the part, measured from the part's mounting point.
                        var contact = new Vector3(part.X, part.Y, part.Z) + Quaternion.Euler(part.RotX, part.Rotation, part.RotZ) * V(def.MountPoint);
                        if (SurfaceUnder(ray, p, out var point, out _, out _)) grabOffset = point - contact;
                        else if (WorkplanePoint(mouse, out var ground)) grabOffset = new Vector3(ground.x - part.X, 0, ground.z - part.Z);
                    }
                    break;
                default:
                    StartHandleDrag(p, handle, ray);
                    break;
            }
            if (float.IsNaN(dragT0)) dragT0 = 0;
        }

        void UpdateStudioDrag(Vector2 mouse)
        {
            var target = Primary;
            if (target == null || studioBefore == null || !Exists(target.Value)) return;
            var p = target.Value;
            float step = input.Ctrl || input.Shift ? 0.1f : studioSnap; // moving the item: Ctrl (or Shift) for fine steps
            var ray = view.ScreenPointToRay(mouse);
            string before = Signature(p);
            if (!UpdateHandleDrag(p, ray, mouse, studioBefore))
            {
                switch (drag.grip)
                {
                    case Grip.Shape:
                    {
                        if (p.Part)
                        {
                            var part = Design.Find(p.Id)!;
                            if (PlacePart(part, mouse, step, grabOffset)) dragReadout = $"x {part.X:0.#} · y {part.Y:0.#} · z {part.Z:0.#} {Tr("unit.mm")}";
                            break;
                        }
                        if (!new Plane(robotAnchor.up, dragFrom).Raycast(ray, out float enter) || enter > 5f) break;
                        var moved = robotAnchor.InverseTransformVector(ray.GetPoint(enter) - dragFrom) / StudioMm;
                        var delta = new Vector3(Snap(dragPivotMm.x + moved.x, step) - dragPivotMm.x, 0, Snap(dragPivotMm.z + moved.z, step) - dragPivotMm.z);
                        MoveItem(p, delta, studioBefore);
                        dragReadout = $"x {dragPivotMm.x + delta.x:0.#} · z {dragPivotMm.z + delta.z:0.#} {Tr("unit.mm")}";
                        break;
                    }
                }
            }
            if (before != Signature(p))
            {
                studioDragChanged = true;
                if (!p.Part) bodyDirty = true; // the throttled rebuild in UpdateEditFrame follows the drag
                UpdateStudioFields();
                UpdateStudioStatus();
            }
        }

        /// <summary>A short text of an item's place, turn and size, to tell whether a drag changed it.</summary>
        string Signature(Pick p)
        {
            if (p.Part)
            {
                var part = Design.Find(p.Id);
                return part == null ? "" : FormattableString.Invariant($"{part.X}|{part.Y}|{part.Z}|{part.RotX}|{part.Rotation}|{part.RotZ}");
            }
            var text = new System.Text.StringBuilder();
            foreach (string id in ShapeIds(p))
            {
                var f = Design.Body.Feature(id)!;
                text.Append(FormattableString.Invariant($"{f.X}|{f.Y}|{f.Z}|{f.RotX}|{f.RotY}|{f.RotZ}|{f.SizeX}|{f.SizeY}|{f.SizeZ};"));
            }
            return text.ToString();
        }

        /// <summary>Sets one size of a shape, or all three in proportion when "Keep proportions" is on.</summary>
        void ApplySize(BodyFeature f, BodyFeature start, int axis, float size)
        {
            if (keepProportions)
            {
                float ratio = size / Mathf.Max(0.01f, Component(new Vector3(start.SizeX, start.SizeY, start.SizeZ), axis));
                f.SizeX = Mathf.Max(0.5f, start.SizeX * ratio);
                f.SizeY = Mathf.Max(0.5f, start.SizeY * ratio);
                f.SizeZ = Mathf.Max(0.5f, start.SizeZ * ratio);
            }
            if (axis == 0) f.SizeX = size;
            else if (axis == 1) f.SizeY = size;
            else f.SizeZ = size;
        }

        void EndStudioDrag()
        {
            if (drag.grip == Grip.None) return;
            if (studioDragChanged && studioBefore != null)
            {
                RecordUndo(studioBefore);
                saveAt = Time.unscaledTime + 0.5f;
                if (Primary is { Part: true }) DesignChanged(); // wires, the card and the new resting height
                else FlushBody();
                RefreshStudioChrome();
                renderSide?.Invoke();
            }
            drag = default;
            studioBefore = null;
            dragReadout = "";
        }

        /// <summary>
        /// The parameter t of the point on the line p + t·a (a of unit length) nearest to the mouse ray; NaN when the
        /// line points along the ray and no drag along it can be read.
        /// </summary>
        static float RayLineParameter(Ray ray, Vector3 p, Vector3 a)
        {
            var w = p - ray.origin;
            float b = Vector3.Dot(a, ray.direction);
            float denominator = 1 - b * b;
            if (denominator < 1e-4f) return float.NaN;
            return (b * Vector3.Dot(ray.direction, w) - Vector3.Dot(a, w)) / denominator;
        }

        static float Snap(float value, float step) => Mathf.Round(value / step) * step;

        /// <summary>An angle in (-180, 180], rounded to 0.01° so that 359.99998 reads as 0.</summary>
        static float Tidy(float degrees)
        {
            degrees = Mathf.Round(degrees * 100) / 100 % 360;
            if (degrees > 180) degrees -= 360;
            if (degrees <= -180) degrees += 360;
            return degrees == 0 ? 0 : degrees; // no -0
        }

        void StudioClick(Vector2 mouse)
        {
            if (swallowClick)
            {
                swallowClick = false;
                return;
            }
            if (drawing)
            {
                AddDrawPoint(mouse);
                return;
            }
            if (ItemUnder(mouse, out _) == null && selection.Count > 0)
            {
                selection.Clear();
                SelectionChanged();
            }
        }

        void UpdateStudioHover(Vector2 mouse, bool overUi)
        {
            string text = "";
            if (drag.grip != Grip.None)
            {
                hot = default;
                hovered = null;
                text = dragReadout;
            }
            else if (!overUi && !leftDown && viewDrag == ViewDrag.None && carrying == null)
            {
                hot = PickHandle(mouse);
                hovered = hot.grip == Grip.None && !drawing ? ItemUnder(mouse, out _) : null;
                if (hovered != null && !selection.Contains(hovered.Value)) text = ItemName(hovered.Value);
            }
            else
            {
                hot = default;
                hovered = null;
            }
            if (text.Length == 0 || root.panel == null)
            {
                tooltip.style.display = DisplayStyle.None;
                return;
            }
            var point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
            tooltip.text = text;
            tooltip.style.left = point.x + 16;
            tooltip.style.top = point.y + 12;
            tooltip.style.display = DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ keys

        /// <summary>The Studio's keys; true when a key was used (Esc then does not leave the Studio).</summary>
        bool StudioKeys()
        {
            if (carrying != null)
            {
                if (!KeyPressed(KeyCode.Escape)) return false;
                CancelCarry();
                return true;
            }
            if (drawing)
            {
                if (KeyPressed(KeyCode.Escape)) CancelDrawing();
                else if (KeyPressed(KeyCode.Return) || KeyPressed(KeyCode.KeypadEnter)) FinishDrawing();
                else if (KeyPressed(KeyCode.Backspace) || KeyPressed(KeyCode.Delete) || (input.Ctrl && KeyPressed(KeyCode.Z))) RemoveLastDrawPoint();
                else return false;
                return true;
            }
            if (input.Ctrl && KeyPressed(KeyCode.G))
            {
                if (input.Shift) UngroupSelected();
                else GroupSelected();
                return true;
            }
            if (selection.Count == 0) return false;
            if (KeyPressed(KeyCode.Escape))
            {
                selection.Clear();
                SelectionChanged();
                return true;
            }
            if (KeyPressed(KeyCode.Delete) || KeyPressed(KeyCode.Backspace))
            {
                DeleteSelected();
                return true;
            }
            if (input.Ctrl && KeyPressed(KeyCode.D)) DuplicateSelected();
            if (!input.Ctrl && KeyPressed(KeyCode.M)) MirrorSelected();
            if (KeyPressed(KeyCode.H) && SelectedFeature != null) EditFeature(x => x.Hole = !x.Hole, rerender: true);
            if (KeyPressed(KeyCode.D) && !input.Ctrl) DropSelected();
            if (KeyPressed(KeyCode.F) && Primary != null) GlideTo(yaw, pitch, distance, WorldOf(PivotOf(Primary.Value)));
            float step = input.Shift ? 0.1f : studioSnap;
            if (KeyPressed(KeyCode.LeftArrow)) Nudge(new Vector3(-step, 0, 0));
            if (KeyPressed(KeyCode.RightArrow)) Nudge(new Vector3(step, 0, 0));
            if (KeyPressed(KeyCode.UpArrow)) Nudge(new Vector3(0, 0, step));
            if (KeyPressed(KeyCode.DownArrow)) Nudge(new Vector3(0, 0, -step));
            if (KeyPressed(KeyCode.PageUp)) Nudge(new Vector3(0, step, 0));
            if (KeyPressed(KeyCode.PageDown)) Nudge(new Vector3(0, -step, 0));
            return false;
        }

        /// <summary>Moves the selection by a step; key presses close together make one undo step.</summary>
        void Nudge(Vector3 step)
        {
            if (selection.Count == 0) return;
            if (Time.unscaledTime - lastNudge > 0.6f) PushUndo();
            lastNudge = Time.unscaledTime;
            var from = Design.Clone();
            foreach (var p in selection) MoveItem(p, step, from);
            StudioChanged(rerender: false);
        }

        // ------------------------------------------------------------------ editing

        /// <summary>After a change of the body: rebuild on the worker, update the panel and status, save soon.</summary>
        void StudioChanged(bool rerender)
        {
            bodyDirty = true;
            saveAt = Time.unscaledTime + 0.8f;
            FlushBody();
            if (rerender) renderSide?.Invoke();
            else UpdateStudioFields();
            RefreshStudioChrome();
        }

        void EditFeature(Action<BodyFeature> change, bool rerender)
        {
            var f = SelectedFeature;
            if (f == null) return;
            PushUndo();
            change(f);
            StudioChanged(rerender);
        }

        void EditPart(Action<PartInstance> change)
        {
            var part = SelectedPart;
            if (part == null) return;
            PushUndo();
            change(part);
            DesignChanged();
        }

        void Select(Pick p)
        {
            selection.Clear();
            selection.Add(p);
            SelectionChanged();
        }

        void SelectionChanged()
        {
            var f = SelectedFeature;
            keepProportions = f?.Kind == FeatureKind.Imported; // a model keeps its shape by default
            shown?.Highlight(SelectedPart?.Id);
            renderSide?.Invoke();
            RefreshStudioChrome();
        }

        /// <summary>A shape from the library rides the mouse until a click sets it down.</summary>
        void AddShape(FeatureKind kind)
        {
            CancelCarry();
            CancelDrawing();
            var before = Design.Clone();
            var f = BodyFeature.Create(kind, studioHoles, 0, studioMaterial);
            f.Colour = studioColour;
            PlaceBeside(f);
            Design.Body.AddFeature(f);
            StartCarry(new Pick(false, f.Id), before);
        }

        /// <summary>A part from the library, beside the others until the mouse brings it where it goes.</summary>
        void AddPartFromLibrary(string partId)
        {
            CancelCarry();
            CancelDrawing();
            var before = Design.Clone();
            var part = Design.AddPart(partId);
            if (part == null)
            {
                ShowToast(Tr("build.full"));
                return;
            }
            if (partId == PartCatalog.Uno && !Robot.HasSketch)
            {
                Robot.EnsureSketch();
                ShowToast(Tr("build.newBoard"));
            }
            StartCarry(new Pick(true, part.Id), before);
        }

        /// <summary>A new shape starts on the workplane beside the shapes already there.</summary>
        void PlaceBeside(BodyFeature f)
        {
            var spots = new[] { (0f, 0f), (40f, 0f), (-40f, 0f), (0f, 40f), (0f, -40f), (40f, 40f), (-40f, 40f), (40f, -40f), (-40f, -40f) };
            foreach (var (x, z) in spots)
            {
                bool free = true;
                foreach (var other in Design.Body.Features)
                    if (other.Kind != FeatureKind.Group && Mathf.Abs(other.X - x) < 25 && Mathf.Abs(other.Z - z) < 25) free = false;
                if (!free) continue;
                f.X = x;
                f.Z = z;
                return;
            }
        }

        void DuplicateSelected()
        {
            if (selection.Count == 0) return;
            PushUndo();
            var copies = new List<Pick>();
            foreach (var p in selection)
            {
                if (p.Part) continue; // a real part is added from the library, as many as the circuit allows
                copies.Add(new Pick(false, CopyShapeOrGroup(Design.Body.Feature(p.Id)!, new Vector3(10, 0, 10), mirror: false)));
            }
            if (copies.Count == 0) return;
            selection.Clear();
            selection.AddRange(copies);
            StudioChanged(rerender: true);
        }

        void MirrorSelected()
        {
            if (selection.Count == 0) return;
            PushUndo();
            var copies = new List<Pick>();
            foreach (var p in selection)
                if (!p.Part) copies.Add(new Pick(false, CopyShapeOrGroup(Design.Body.Feature(p.Id)!, Vector3.zero, mirror: true)));
            if (copies.Count == 0) return;
            selection.Clear();
            selection.AddRange(copies);
            StudioChanged(rerender: true);
        }

        /// <summary>Copies a shape, or a group with everything in it, moved by an offset or mirrored across x = 0.</summary>
        string CopyShapeOrGroup(BodyFeature original, Vector3 offset, bool mirror)
        {
            var body = Design.Body;
            var copy = mirror ? original.MirroredX() : original.Clone();
            copy.X += offset.x;
            copy.Y += offset.y;
            copy.Z += offset.z;
            body.AddFeature(copy);
            if (original.Kind == FeatureKind.Group)
            {
                foreach (var member in body.Members(original))
                {
                    if (member == copy) continue;
                    string id = CopyShapeOrGroup(member, offset, mirror);
                    body.Feature(id)!.Group = copy.Id;
                }
            }
            return copy.Id;
        }

        void DeleteSelected()
        {
            if (selection.Count == 0) return;
            PushUndo();
            bool parts = false;
            foreach (var p in selection)
            {
                if (p.Part)
                {
                    Design.RemovePart(p.Id);
                    parts = true;
                }
                else
                {
                    Design.Body.Remove(p.Id);
                }
            }
            selection.Clear();
            if (parts) DesignChanged();
            else StudioChanged(rerender: true);
        }

        void DeleteItem(Pick p)
        {
            selection.Clear();
            selection.Add(p);
            DeleteSelected();
        }

        /// <summary>Tinkercad's Drop: the selection's lowest point rests on the workplane.</summary>
        void DropSelected()
        {
            if (selection.Count == 0) return;
            PushUndo();
            var from = Design.Clone();
            bool parts = false;
            foreach (var p in selection)
            {
                MoveItem(p, new Vector3(0, -BoundsOf(p).min.y, 0), from);
                parts |= p.Part;
            }
            if (parts) DesignChanged();
            else StudioChanged(rerender: true);
        }

        /// <summary>Moves everything up or down so that the robot's lowest point is on the workplane.</summary>
        void StandOnGround()
        {
            float lowest = DesignGeometry.LowestPoint(Design);
            if (Mathf.Abs(lowest) < 0.01f) return;
            PushUndo();
            foreach (var f in Design.Body.Features) f.Y -= lowest;
            foreach (var part in Design.Parts) part.Y -= lowest;
            DesignChanged();
        }

        void GroupSelected()
        {
            var ids = new List<string>();
            foreach (var p in selection) if (!p.Part) ids.Add(p.Id);
            if (ids.Count < 2)
            {
                ShowToast(Tr("studio.groupNeedsTwo"));
                return;
            }
            PushUndo();
            var group = Design.Body.Group(ids);
            if (group == null)
            {
                undo.RemoveAt(undo.Count - 1);
                return;
            }
            Select(new Pick(false, group.Id));
            StudioChanged(rerender: true);
        }

        void UngroupSelected()
        {
            var f = SelectedFeature;
            if (f == null || f.Kind != FeatureKind.Group) return;
            PushUndo();
            var members = Design.Body.Members(f);
            Design.Body.Ungroup(f.Id);
            selection.Clear();
            foreach (var member in members) selection.Add(new Pick(false, member.Id));
            StudioChanged(rerender: true);
        }

        /// <summary>Material and colour for the selection's shapes (a group: every shape in it).</summary>
        void SetMaterial(BodyMaterial material, string colour)
        {
            if (SelectedFeature == null) return;
            PushUndo();
            foreach (string id in ShapeIds(selection[0]))
            {
                var f = Design.Body.Feature(id)!;
                f.Material = material;
                f.Colour = BodyLook.Coloured(material) ? colour : "";
            }
            studioMaterial = material; // the next shape from the library is made of the same
            studioColour = colour;
            StudioChanged(rerender: true);
        }

        // ------------------------------------------------------------------ the inspector

        /// <summary>The Studio's side panel: the selection, or a list of everything in the robot.</summary>
        void RenderStudio()
        {
            RefreshStudioChrome();
            Array.Clear(vecFields, 0, vecFields.Length);
            selection.RemoveAll(p => !Exists(p));
            if (selection.Count > 1)
            {
                RenderMany();
                return;
            }
            if (SelectedPart is { } part)
            {
                RenderPart(part);
                return;
            }
            if (SelectedFeature is { } f)
            {
                RenderFeature(f);
                return;
            }
            RenderOverview();
        }

        /// <summary>Nothing selected: the robot's shapes and parts, its mass, and the STL export.</summary>
        void RenderOverview()
        {
            var body = Design.Body;
            var top = body.Members(null);
            sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.shapeList", top.Count)), "section-title"));
            if (top.Count == 0) Info("studio.noShapes");
            foreach (var item in top) sideContent.Add(ItemRow(new Pick(false, item.Id), IconFor(item.Kind), FeatureName(item), item.Hole ? Tr("studio.holeTag") : ""));
            sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.partList", Design.Parts.Count)), "section-title"));
            if (Design.Parts.Count == 0) Info("studio.noParts");
            foreach (var part in Design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                sideContent.Add(ItemRow(new Pick(true, part.Id), def == null ? Icon.Chip : PartIcon(def.Kind), PartName(part), ""));
            }
            float lowest = DesignGeometry.LowestPoint(Design);
            if (Mathf.Abs(lowest) >= 0.5f && (top.Count > 0 || Design.Parts.Count > 0))
            {
                sideContent.Add(Classed(new Label(SpikeStrings.Format(lowest < 0 ? "studio.below" : "studio.above", Mathf.Abs(lowest))), lowest < 0 ? "warn-line" : "info-text"));
                sideContent.Add(IconSmallButton(Icon.Drop, "studio.ground", StandOnGround));
            }
            var meshes = shown?.Body;
            if (meshes != null)
                sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.bodyTotal", meshes.MassG, meshes.VolumeMm3 / 1000)), "info-text"));
            var buttons = Layout("repair-buttons");
            buttons.Add(SmallButton("body.export", () => ExportStl(null)));
            if (lastExport.Length > 0) buttons.Add(SmallButton("body.openFolder", OpenExportFolder));
            sideContent.Add(buttons);
            Info("body.stlNote");
        }

        VisualElement ItemRow(Pick p, Icon icon, string name, string tag)
        {
            var row = new VisualElement();
            row.AddToClassList("shape-row");
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is not Button) Select(p);
            });
            row.Add(Classed(new IconView(icon), "shape-row-icon"));
            row.Add(Classed(new Label(name), "shape-row-name"));
            if (tag.Length > 0) row.Add(Classed(new Label(tag), "shape-row-tag"));
            var remove = new Button(() => DeleteItem(p)) { text = "✕", focusable = false };
            remove.AddToClassList("wire-remove");
            row.Add(remove);
            return row;
        }

        /// <summary>Several things selected: group them, or delete them.</summary>
        void RenderMany()
        {
            sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.selectedMany", selection.Count)), "part-title"));
            foreach (var p in selection) sideContent.Add(Classed(new Label("• " + ItemName(p)), "info-text"));
            var buttons = Layout("repair-buttons");
            buttons.Add(IconSmallButton(Icon.Parts, "studio.group", GroupSelected));
            buttons.Add(IconSmallButton(Icon.Trash, "studio.delete", DeleteSelected));
            sideContent.Add(buttons);
            Info("studio.groupNote");
        }

        void RenderPart(PartInstance part)
        {
            var def = PartCatalog.Get(part.Part);
            var title = Layout("feature-title");
            title.Add(Classed(new IconView(def == null ? Icon.Chip : PartIcon(def.Kind)), "feature-title-icon"));
            title.Add(Classed(new Label(PartName(part)), "part-title"));
            sideContent.Add(title);
            if (def != null)
                sideContent.Add(Classed(new Label($"{def.SizeX:0.#} × {def.SizeZ:0.#} × {def.SizeY:0.#} {Tr("unit.mm")} · {def.MassG:0.#} {Tr("unit.g")} · {Tr("studio.fixedSize")}"), "info-text"));
            Section("studio.position");
            sideContent.Add(VecRow(0, (part.X, part.Y, part.Z), (axis, value) => EditPart(x =>
            {
                if (axis == 0) x.X = value;
                else if (axis == 1) x.Y = value;
                else x.Z = value;
            })));
            Section("studio.rotation");
            sideContent.Add(VecRow(3, (part.RotX, part.Rotation, part.RotZ), (axis, value) => EditPart(x =>
            {
                if (axis == 0) x.RotX = Tidy(value);
                else if (axis == 1) x.Rotation = Tidy(value);
                else x.RotZ = Tidy(value);
            })));
            if (def?.Kind == PartKind.Motor)
            {
                int sign = DesignGeometry.ForwardSign(part);
                string side = Tr(DesignGeometry.SideOf(part) == "right" ? "side.right" : "side.left");
                sideContent.Add(Classed(new Label(sign == 0 ? Tr("studio.noDrive") : SpikeStrings.Format(sign > 0 ? "studio.driveForward" : "studio.driveBack", side)), sign == 0 ? "warn-line" : "info-text"));
            }
            var buttons = Layout("repair-buttons");
            buttons.Add(IconSmallButton(Icon.Drop, "studio.drop", DropSelected));
            buttons.Add(IconSmallButton(Icon.Trash, "studio.delete", DeleteSelected));
            sideContent.Add(buttons);
            Info("studio.partPlaceNote");
        }

        void RenderFeature(BodyFeature f)
        {
            var title = Layout("feature-title");
            title.Add(Classed(new IconView(IconFor(f.Kind)), "feature-title-icon"));
            title.Add(Classed(new Label(FeatureName(f)), "part-title"));
            sideContent.Add(title);

            var kind = Layout("seg-row");
            kind.Add(FeatureSegment("studio.solid", !f.Hole, () => EditFeature(x => x.Hole = false, rerender: true)));
            kind.Add(FeatureSegment("studio.hole", f.Hole, () => EditFeature(x => x.Hole = true, rerender: true)));
            sideContent.Add(kind);
            if (f.Hole && Design.Body.Parent(f) == null) sideContent.Add(Classed(new Label(Tr("studio.holeAlone")), "info-line"));

            if (f.Kind == FeatureKind.Group)
            {
                var shapes = Design.Body.Shapes(f);
                sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.groupHas", shapes.Count)), "info-text"));
                var first = shapes.Find(s => !s.Hole) ?? f;
                Section("studio.material");
                sideContent.Add(MaterialPicker(first.Material, first.Colour, SetMaterial));
                var buttons = Layout("repair-buttons");
                buttons.Add(IconSmallButton(Icon.Parts, "studio.ungroup", UngroupSelected));
                buttons.Add(IconSmallButton(Icon.Duplicate, "studio.duplicate", DuplicateSelected));
                sideContent.Add(buttons);
                var more = Layout("repair-buttons");
                more.Add(IconSmallButton(Icon.Mirror, "studio.mirror", MirrorSelected));
                more.Add(IconSmallButton(Icon.Trash, "studio.delete", DeleteSelected));
                sideContent.Add(more);
                return;
            }

            if (!f.Hole)
            {
                Section("studio.material");
                sideContent.Add(MaterialPicker(f.Material, f.Colour, SetMaterial));
            }

            Section("studio.position");
            sideContent.Add(VecRow(0, (f.X, f.Y, f.Z), (axis, value) => EditFeature(x =>
            {
                if (axis == 0) x.X = value;
                else if (axis == 1) x.Y = value;
                else x.Z = value;
            }, rerender: false)));
            Section("studio.rotation");
            sideContent.Add(VecRow(3, (f.RotX, f.RotY, f.RotZ), (axis, value) => EditFeature(x =>
            {
                if (axis == 0) x.RotX = Tidy(value);
                else if (axis == 1) x.RotY = Tidy(value);
                else x.RotZ = Tidy(value);
            }, rerender: false)));
            Section("studio.sizeMm");
            sideContent.Add(VecRow(6, (f.SizeX, f.SizeY, f.SizeZ), (axis, value) => EditFeature(x => ApplySize(x, x.Clone(), axis, Mathf.Max(0.5f, value)), rerender: false)));
            var keep = new Toggle(Tr("studio.keep")) { value = keepProportions, focusable = false };
            keep.AddToClassList("body-toggle");
            keep.RegisterValueChangedCallback(e => keepProportions = e.newValue);
            sideContent.Add(keep);

            switch (f.Kind)
            {
                case FeatureKind.RoundedBox:
                    DetailField("studio.corner", f.Detail, (x, v) => x.Detail = Mathf.Clamp(v, 0.5f, 50));
                    break;
                case FeatureKind.Plate:
                    DetailField("studio.corner", f.Detail, (x, v) => x.Detail = Mathf.Clamp(v, 0, 500));
                    DetailField("studio.pitch", f.Pitch, (x, v) => x.Pitch = v < 5 ? 0 : Mathf.Min(v, 100));
                    DetailField("studio.holeSize", f.HoleSize, (x, v) => x.HoleSize = Mathf.Clamp(v, 1, 20));
                    break;
                case FeatureKind.Tube:
                    DetailField("studio.wall", f.Detail, (x, v) => x.Detail = Mathf.Clamp(v, 0.4f, 50));
                    break;
                case FeatureKind.Cone:
                    DetailField("studio.top", f.Detail * 100, (x, v) => x.Detail = Mathf.Clamp01(v / 100));
                    break;
                case FeatureKind.Imported:
                {
                    string path = Path.Combine(Robot.ImportFolder, f.MeshFile);
                    if (!triangleCounts.TryGetValue(path, out int triangles))
                    {
                        try
                        {
                            if (File.Exists(path)) triangles = BodyBuilder.ReadModel(path).TriangleCount;
                        }
                        catch (Exception)
                        {
                            // a damaged file shows as 0 triangles; the model shows nothing
                        }
                        triangleCounts[path] = triangles;
                    }
                    sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.file", f.MeshFile, triangles)), "info-text"));
                    if (shown?.Body != null && shown.Body.NotClosed.Contains(f.Id))
                        sideContent.Add(Classed(new Label("⚠ " + Tr("studio.open")), "warn-line"));
                    break;
                }
            }

            var row1 = Layout("repair-buttons");
            row1.Add(IconSmallButton(Icon.Duplicate, "studio.duplicate", DuplicateSelected));
            row1.Add(IconSmallButton(Icon.Mirror, "studio.mirror", MirrorSelected));
            sideContent.Add(row1);
            var row2 = Layout("repair-buttons");
            row2.Add(IconSmallButton(Icon.Drop, "studio.drop", DropSelected));
            row2.Add(IconSmallButton(Icon.Trash, "studio.delete", DeleteSelected));
            sideContent.Add(row2);

            double grams = f.Hole ? 0 : f.ApproximateVolume() / 1000 * BodyDesign.DensityGPerCm3(f.Material);
            sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.shapeVolume", f.ApproximateVolume() / 1000, grams)), "info-text"));
        }

        Button FeatureSegment(string key, bool active, Action apply)
        {
            var button = new Button(apply) { text = Tr(key), focusable = false };
            button.AddToClassList("seg-button");
            button.EnableInClassList("seg-button--active", active);
            return button;
        }

        Button IconSmallButton(Icon icon, string key, Action onClick)
        {
            var button = new Button(onClick) { focusable = false };
            button.AddToClassList("small-button");
            button.AddToClassList("icon-small-button");
            button.Add(Classed(new IconView(icon), "small-icon"));
            button.Add(new Label(Tr(key)));
            return button;
        }

        VisualElement VecRow(int slot, (float x, float y, float z) value, Action<int, float> apply)
        {
            var row = Layout("vec-row");
            float[] values = { value.x, value.y, value.z };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                var field = new FloatField("xyz"[i].ToString().ToUpperInvariant()) { isDelayed = true, formatString = "0.##" };
                field.SetValueWithoutNotify(values[i]);
                field.AddToClassList("vec-field");
                field.AddToClassList("vec-" + "xyz"[i]);
                field.RegisterValueChangedCallback(e =>
                {
                    if (!Mathf.Approximately(e.newValue, e.previousValue)) apply(axis, e.newValue);
                });
                row.Add(field);
                vecFields[slot + i] = field;
            }
            return row;
        }

        void DetailField(string key, float value, Action<BodyFeature, float> set)
        {
            var field = new FloatField(Tr(key)) { isDelayed = true, formatString = "0.##" };
            field.SetValueWithoutNotify(value);
            field.AddToClassList("detail-field");
            field.RegisterValueChangedCallback(e =>
            {
                if (!Mathf.Approximately(e.newValue, e.previousValue)) EditFeature(x => set(x, e.newValue), rerender: false);
            });
            sideContent.Add(field);
        }

        /// <summary>Shows the selection's numbers while a handle moves it (not in a box being typed in).</summary>
        void UpdateStudioFields()
        {
            if (IsTyping()) return;
            float[]? values = null;
            if (SelectedFeature is { Kind: not FeatureKind.Group } f) values = new[] { f.X, f.Y, f.Z, f.RotX, f.RotY, f.RotZ, f.SizeX, f.SizeY, f.SizeZ };
            else if (SelectedPart is { } part) values = new[] { part.X, part.Y, part.Z, part.RotX, part.Rotation, part.RotZ, 0f, 0f, 0f };
            if (values == null) return;
            for (int i = 0; i < vecFields.Length; i++) vecFields[i]?.SetValueWithoutNotify(values[i]);
        }

        static string KindKey(FeatureKind kind) => kind switch
        {
            FeatureKind.Plate => "shape.plate",
            FeatureKind.RoundedBox => "shape.rounded",
            FeatureKind.Cylinder => "shape.cylinder",
            FeatureKind.Cone => "shape.cone",
            FeatureKind.Sphere => "shape.sphere",
            FeatureKind.Wedge => "shape.wedge",
            FeatureKind.Tube => "shape.tube",
            FeatureKind.Extrusion => "shape.extrusion",
            FeatureKind.Imported => "shape.imported",
            FeatureKind.Group => "shape.group",
            _ => "shape.box",
        };

        static string MaterialKey(BodyMaterial material) => material switch
        {
            BodyMaterial.Acrylic => "material.acrylic",
            BodyMaterial.Plywood => "material.plywood",
            BodyMaterial.Cardboard => "material.cardboard",
            BodyMaterial.EvaFoam => "material.evaFoam",
            BodyMaterial.FoamBoard => "material.foamBoard",
            BodyMaterial.Aluminium => "material.aluminium",
            _ => "material.pla",
        };

        static Icon IconFor(FeatureKind kind) => kind switch
        {
            FeatureKind.Plate => Icon.Plate,
            FeatureKind.RoundedBox => Icon.ShapeRounded,
            FeatureKind.Cylinder => Icon.ShapeCylinder,
            FeatureKind.Cone => Icon.ShapeCone,
            FeatureKind.Sphere => Icon.ShapeSphere,
            FeatureKind.Wedge => Icon.ShapeWedge,
            FeatureKind.Tube => Icon.ShapeTube,
            FeatureKind.Extrusion => Icon.Draw,
            FeatureKind.Imported => Icon.Import,
            FeatureKind.Group => Icon.Parts,
            _ => Icon.ShapeBox,
        };

        static Icon PartIcon(PartKind kind) => kind switch
        {
            PartKind.Board => Icon.Chip,
            PartKind.MotorDriver => Icon.Driver,
            PartKind.Ultrasonic => Icon.Sonar,
            PartKind.Motor => Icon.Motor,
            PartKind.Battery => Icon.Battery,
            PartKind.Servo => Icon.Servo,
            PartKind.Led => Icon.Led,
            _ => Icon.Caster,
        };

        /// <summary>"Box 2", "Plate with holes 1", "Group 5", or an imported file's name.</summary>
        static string FeatureName(BodyFeature f) => f.Kind == FeatureKind.Imported && f.MeshFile.Length > 0
            ? Path.GetFileNameWithoutExtension(f.MeshFile)
            : Tr(KindKey(f.Kind)) + " " + (f.Id.StartsWith("f") ? f.Id.Substring(1) : f.Id);

        // ------------------------------------------------------------------ drawing an outline

        void ToggleDrawing()
        {
            if (drawing) CancelDrawing();
            else StartDrawing();
        }

        void StartDrawing()
        {
            CancelCarry();
            selection.Clear();
            drawing = true;
            drawPoints.Clear();
            SelectionChanged();
        }

        void CancelDrawing()
        {
            if (!drawing) return;
            drawing = false;
            drawPoints.Clear();
            UpdateDrawPreview();
            RefreshStudioChrome();
        }

        void AddDrawPoint(Vector2 mouse)
        {
            if (drawPoints.Count >= 3 && Vector2.Distance(mouse, Screen2(PlaneWorld(drawPoints[0]))) < 12)
            {
                FinishDrawing();
                return;
            }
            if (!WorkplanePoint(mouse, out var mm)) return;
            float step = input.Shift ? 0.1f : studioSnap;
            var point = new Vector2(Snap(mm.x, step), Snap(mm.z, step));
            if (drawPoints.Count > 0 && Vector2.Distance(point, drawPoints[drawPoints.Count - 1]) < 0.05f) return;
            drawPoints.Add(point);
        }

        void RemoveLastDrawPoint()
        {
            if (drawPoints.Count > 0) drawPoints.RemoveAt(drawPoints.Count - 1);
            else CancelDrawing();
        }

        /// <summary>The outline becomes a shape 5 mm tall on the workplane (a plate to cut from a sheet), or a hole.</summary>
        void FinishDrawing()
        {
            if (drawPoints.Count < 3)
            {
                ShowToast(Tr("studio.tooFew"));
                return;
            }
            if (SelfCrossing(drawPoints))
            {
                ShowToast(Tr("studio.selfCross"));
                return;
            }
            var flat = new List<float>();
            foreach (var p in drawPoints)
            {
                flat.Add(p.x);
                flat.Add(p.y);
            }
            float height = studioHoles ? 15 : 5;
            var f = BodyFeature.FromOutline(flat, 0, height, studioHoles);
            if (f == null)
            {
                ShowToast(Tr("studio.tooFew"));
                return;
            }
            f.Material = studioMaterial;
            f.Colour = studioColour;
            if (studioHoles) f.Y = 0; // a hole made on the workplane reaches both ways, ready to cut a plate lifted onto it
            drawing = false;
            drawPoints.Clear();
            PushUndo();
            Design.Body.AddFeature(f);
            Select(new Pick(false, f.Id));
            StudioChanged(rerender: true);
        }

        /// <summary>True when two edges of the closed outline cross (neighbouring edges share a corner and do not count).</summary>
        static bool SelfCrossing(List<Vector2> points)
        {
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue; // the closing edge touches the first
                    if (SegmentsCross(a, b, points[j], points[(j + 1) % n])) return true;
                }
            }
            return false;
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            static float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>A point of an outline (x, z in mm) on the workplane, in world space.</summary>
        Vector3 PlaneWorld(Vector2 mm) => WorldOf(new Vector3(mm.x, 0.3f, mm.y));

        /// <summary>The outline so far, the line to the mouse, and a dot on every corner (the first one larger).</summary>
        void UpdateDrawPreview()
        {
            bool show = drawing && studioScene != null && mode == EditMode.Body;
            if (!show)
            {
                if (drawLine != null) drawLine.enabled = false;
                foreach (var dot in drawDots) if (dot != null) dot.gameObject.SetActive(false);
                return;
            }
            if (drawLine == null)
            {
                drawLine = new GameObject("Outline").AddComponent<LineRenderer>();
                drawLine.transform.SetParent(studioScene, false);
                drawLine.sharedMaterial = lineMaterial;
                drawLine.widthMultiplier = 0.0012f;
                drawLine.generateLightingData = true;
                drawLine.shadowCastingMode = ShadowCastingMode.Off;
                drawLine.receiveShadows = false;
                drawLine.numCapVertices = 2;
            }
            var positions = new List<Vector3>();
            foreach (var p in drawPoints) positions.Add(PlaneWorld(p));
            var mouse = input.Position;
            if (!IsPointerOverUi(mouse) && WorkplanePoint(mouse, out var mm))
            {
                float step = input.Shift ? 0.1f : studioSnap;
                positions.Add(PlaneWorld(new Vector2(Snap(mm.x, step), Snap(mm.z, step))));
            }
            drawLine.enabled = positions.Count >= 2;
            drawLine.positionCount = positions.Count;
            drawLine.SetPositions(positions.ToArray());
            while (drawDots.Count < drawPoints.Count)
            {
                var dot = new GameObject("Corner").transform;
                dot.SetParent(studioScene, false);
                dot.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.RoundedBox(Vector3.one, 0.3f);
                var renderer = dot.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = lineMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                drawDots.Add(dot);
            }
            for (int i = 0; i < drawDots.Count; i++)
            {
                bool used = i < drawPoints.Count;
                drawDots[i].gameObject.SetActive(used);
                if (!used) continue;
                var world = PlaneWorld(drawPoints[i]);
                drawDots[i].position = world;
                float size = Vector3.Distance(view.transform.position, world) * (i == 0 && drawPoints.Count >= 3 ? 0.016f : 0.009f);
                drawDots[i].localScale = Vector3.one * size;
            }
        }

        // ------------------------------------------------------------------ importing a model

        void ImportFromDialog()
        {
            CancelCarry();
            CancelDrawing();
            if (!WinFileDialog.Available)
            {
                ShowToast(Tr("studio.noDialog"));
                return;
            }
            string? path = WinFileDialog.OpenFile(Tr("studio.fileTitle"), Tr("studio.fileFilter"), "*.stl;*.obj");
            if (path != null) ImportModel(path);
        }

        /// <summary>
        /// Copies an STL or OBJ file into the robot's own folder and adds it as a shape at the file's size in
        /// millimetres (a file whose longest side is under 2 units is taken to be in metres), standing on the workplane.
        /// </summary>
        bool ImportModel(string path)
        {
            string name = Path.GetFileName(path);
            MeshFileData data;
            try
            {
                data = BodyBuilder.ReadModel(path);
            }
            catch (Exception e)
            {
                ShowToast(SpikeStrings.Format("studio.importFailed", name, e.Message));
                return false;
            }
            if (data.TriangleCount == 0)
            {
                ShowToast(SpikeStrings.Format("studio.importFailed", name, "0 triangles"));
                return false;
            }
            if (data.TriangleCount > MaxImportTriangles)
            {
                ShowToast(SpikeStrings.Format("studio.tooMany", name, data.TriangleCount));
                return false;
            }
            var size = new Vector3(data.Max.x - data.Min.x, data.Max.y - data.Min.y, data.Max.z - data.Min.z);
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            string note = "";
            if (longest > 0 && longest < 2)
            {
                size *= 1000;
                longest *= 1000;
                note = " " + Tr("studio.metres");
            }
            else if (longest > 400)
            {
                note = " " + SpikeStrings.Format("studio.big", longest);
            }
            string stored;
            try
            {
                string folder = Robot.ImportFolder;
                Directory.CreateDirectory(folder);
                stored = UniqueFileName(folder, name);
                File.Copy(path, Path.Combine(folder, stored));
                triangleCounts[Path.Combine(folder, stored)] = data.TriangleCount;
            }
            catch (Exception e)
            {
                ShowToast(SpikeStrings.Format("studio.importFailed", name, e.Message));
                return false;
            }
            var f = new BodyFeature
            {
                Kind = FeatureKind.Imported,
                Hole = studioHoles,
                MeshFile = stored,
                Material = studioMaterial,
                Colour = studioColour,
                SizeX = Mathf.Max(0.5f, size.x),
                SizeY = Mathf.Max(0.5f, size.y),
                SizeZ = Mathf.Max(0.5f, size.z),
            };
            f.Y = f.SizeY / 2;
            PlaceBeside(f);
            PushUndo();
            Design.Body.AddFeature(f);
            Select(new Pick(false, f.Id));
            keepProportions = true;
            StudioChanged(rerender: true);
            ShowToast(SpikeStrings.Format("studio.imported", name, f.SizeX, f.SizeY, f.SizeZ) + note);
            return true;
        }

        /// <summary>The file's name, or "name-2.stl", "name-3.stl"… when the robot already has one of that name.</summary>
        static string UniqueFileName(string folder, string name)
        {
            string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            string candidate = name;
            for (int n = 2; File.Exists(Path.Combine(folder, candidate)); n++) candidate = $"{stem}-{n}{extension}";
            return candidate;
        }

        // ------------------------------------------------------------------ benchmark helpers

        /// <summary>
        /// A UI Toolkit click (pointer down and up) in the middle of an element, once it has been laid out: a panel
        /// that was just drawn again has no place on the screen until the next layout pass.
        /// </summary>
        IEnumerator ClickElement(VisualElement element)
        {
            yield return null;
            for (int i = 0; i < 10 && (element.panel == null || float.IsNaN(element.worldBound.width) || element.worldBound.width < 1); i++) yield return null;
            var scroll = element.GetFirstAncestorOfType<ScrollView>();
            if (scroll != null)
            {
                scroll.ScrollTo(element); // as a player scrolls down to a tile before clicking it
                yield return null;
            }
            var centre = element.worldBound.center;
            var picked = element.panel?.Pick(centre);
            bool hits = picked != null && (picked == element || element.Contains(picked));
            if (!hits || !element.enabledInHierarchy) // the log says why a scripted click did nothing
                Debug.LogWarning($"Benchmark click may miss: {(element as Button)?.text}{element.Q<Label>()?.text} at {centre}, bound {element.worldBound}, " +
                                 $"enabled {element.enabledInHierarchy}, the pointer finds {picked?.GetType().Name} {string.Join(".", picked?.GetClasses() ?? System.Array.Empty<string>())}");
            SendPointer(EventType.MouseDown, centre);
            yield return null;
            SendPointer(EventType.MouseUp, centre);
            yield return null;
        }

        /// <summary>
        /// An L-shaped angle bracket, 30 × 20 mm with 3 mm walls and 20 mm wide, as a closed binary STL in
        /// millimetres with z up: the L is drawn counter-clockwise in xy and pushed up along z.
        /// </summary>
        static void WriteBracketStl(string path)
        {
            var outline = new[] { new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 3), new Vector2(3, 3), new Vector2(3, 20), new Vector2(0, 20) };
            const float width = 20;
            var positions = new List<float>();
            foreach (float z in new[] { 0f, width })
                foreach (var p in outline)
                {
                    positions.Add(p.x);
                    positions.Add(p.y);
                    positions.Add(z);
                }
            int n = outline.Length;
            var triangles = new List<int>();
            // Caps: a fan from the inner corner (3, 3), which sees every other corner of the L.
            int[,] fan = { { 3, 4, 5 }, { 3, 5, 0 }, { 3, 0, 1 }, { 3, 1, 2 } };
            for (int t = 0; t < 4; t++)
            {
                triangles.AddRange(new[] { fan[t, 0] + n, fan[t, 1] + n, fan[t, 2] + n }); // top, seen from above
                triangles.AddRange(new[] { fan[t, 0], fan[t, 2], fan[t, 1] });             // bottom, seen from below
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                triangles.AddRange(new[] { i, j, j + n });
                triangles.AddRange(new[] { i, j + n, i + n });
            }
            using var stream = File.Create(path);
            StlWriter.Write(stream, positions, triangles, "bracket", yUp: false);
        }
    }
}
