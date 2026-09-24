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
    }

    /// <summary>
    /// The Body Studio (docs/08, ADR-0005): the robot's body modelled like in Tinkercad, inside the Garage.
    /// Beginners click a shape in the palette and drag it on the deck; the handles move it along an axis, turn it
    /// in 15° steps and size it in 5 mm steps. Experts type exact millimetres and degrees, change the snap, cut
    /// holes with hole shapes, draw an outline that becomes a plate or a cut-out, and upload their own STL or OBJ
    /// models. Every change edits the robot's <see cref="BodyDesign"/>; Manifold rebuilds the body on the worker
    /// thread (GarageEdit's FlushBody) while each shape's see-through "ghost" follows the mouse at once.
    /// </summary>
    public sealed partial class GarageSpike
    {
        public Material? overlayMaterial; // CoreEngine/StudioOverlay (SpikeSetup): see-through shapes and handles
        public Material? gridMaterial;    // CoreEngine/StudioGrid: the work grid on the deck

        enum StudioTool { Move, Rotate, Size }

        enum Grip { None, Move, Turn, Size, Shape }

        const float StudioMm = 0.001f;
        const float GizmoFactor = 0.13f; // the handles' length as a share of the distance to the camera
        const int MaxImportTriangles = 200000;

        static readonly FeatureKind[] PaletteKinds =
            { FeatureKind.Box, FeatureKind.RoundedBox, FeatureKind.Cylinder, FeatureKind.Cone, FeatureKind.Sphere, FeatureKind.Wedge, FeatureKind.Tube };
        static readonly float[] SnapSteps = { 1, 5, 10 };
        static readonly Color[] AxisColours = { new Color(1f, 0.33f, 0.33f, 0.95f), new Color(0.45f, 0.9f, 0.35f, 0.95f), new Color(0.33f, 0.6f, 1f, 0.95f) };

        // Chrome: the Garage's own parts are hidden while the Studio is open, and its side panel moves in.
        readonly List<VisualElement> garageChrome = new List<VisualElement>();
        VisualElement rightColumn = null!;
        VisualElement? studio;
        VisualElement studioViewport = null!, studioRight = null!, studioTop = null!, studioMain = null!, studioStatusBar = null!;
        Label studioTitle = null!, studioStatus = null!, studioStats = null!;
        readonly Dictionary<StudioTool, Button> toolButtons = new Dictionary<StudioTool, Button>();
        readonly List<Button> snapButtons = new List<Button>();
        readonly List<Button> paletteButtons = new List<Button>();
        Button solidButton = null!, holeButton = null!, partsButton = null!, drawButton = null!, undoButton = null!, redoButton = null!;

        // What the Studio is doing
        StudioTool studioTool = StudioTool.Move;
        float studioSnap = 5;
        bool studioHoles, studioShowParts = true, keepProportions, projectionShifted;
        string? selectedFeature, hoveredFeature;
        float lastNudge = -10;

        // The shapes' ghosts, the handles and the grid, under the robot's anchor (the chassis frame)
        sealed class Ghost
        {
            public Transform Outer = null!, Inner = null!;
            public MeshRenderer Renderer = null!;
            public BodyFeature Built = null!;
        }

        Transform? studioScene, gizmo, grid;
        readonly Dictionary<string, Ghost> ghosts = new Dictionary<string, Ghost>();
        readonly List<(MeshRenderer renderer, Grip grip, int axis, int sign)> gizmoParts = new List<(MeshRenderer, Grip, int, int)>();
        Material? ghostHole, ghostHoleSelected, ghostSelected, ghostHover, hotMaterial, lineMaterial;
        readonly Material?[] axisMaterials = new Material?[3];

        // A drag of a handle or of a shape
        (Grip grip, int axis, int sign) hot, drag;
        BodyFeature? dragStart;
        RobotDesign? studioBefore;
        bool studioDragChanged;
        Vector3 dragPivot, dragDirection, dragFrom;
        float dragT0;
        Vector2 dragMouse0;
        string dragReadout = "";

        // Drawing an outline on the deck
        bool drawing;
        readonly List<Vector2> drawPoints = new List<Vector2>();
        LineRenderer? drawLine;
        readonly List<Transform> drawDots = new List<Transform>();

        // Inspector fields updated while a handle moves: position, rotation, size
        readonly FloatField?[] vecFields = new FloatField?[9];

        // Triangle counts of uploaded models by path, so the inspector does not read a large file on every redraw
        readonly Dictionary<string, int> triangleCounts = new Dictionary<string, int>();

        BodyFeature? SelectedFeature => selectedFeature == null ? null : Design.Body.Feature(selectedFeature);

        // ------------------------------------------------------------------ opening and closing

        void OpenStudio()
        {
            if (studio == null) BuildStudioUi();
            foreach (var element in garageChrome) element.style.display = DisplayStyle.None;
            studio!.style.display = DisplayStyle.Flex;
            studioRight.Add(sidePanel);
            sidePanel.AddToClassList("side-panel--studio");
            selectedFeature = null;
            hoveredFeature = null;
            drawing = false;
            drag = default;
            EnsureStudioScene();
            RefreshStudioChrome();
        }

        void CloseStudio()
        {
            CancelDrawing();
            EndStudioDrag();
            if (studio != null) studio.style.display = DisplayStyle.None;
            foreach (var element in garageChrome) element.style.display = DisplayStyle.Flex;
            sidePanel.RemoveFromClassList("side-panel--studio");
            rightColumn.Add(sidePanel);
            DestroyStudioScene();
            if (projectionShifted) view.ResetProjectionMatrix();
            projectionShifted = false;
            selectedFeature = null;
            hoveredFeature = null;
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

            var tools = Layout("studio-group");
            foreach (var (tool, icon, key) in new[] { (StudioTool.Move, Icon.Move, "studio.move"), (StudioTool.Rotate, Icon.Rotate, "studio.rotate"), (StudioTool.Size, Icon.Size, "studio.size") })
            {
                var chosen = tool;
                var button = StudioButton(icon, key, () => SetStudioTool(chosen));
                toolButtons[tool] = button;
                tools.Add(button);
            }
            top.Add(tools);

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
            palette.Add(Classed(Localized(new Label(), "studio.shapes"), "palette-title"));
            palette.Add(Classed(Localized(new Label(), "studio.addAs"), "palette-note"));
            var modes = Layout("palette-switch");
            solidButton = SwitchButton(Icon.Solid, "studio.solid", () => SetHoleMode(false));
            holeButton = SwitchButton(Icon.Hole, "studio.hole", () => SetHoleMode(true));
            modes.Add(solidButton);
            modes.Add(holeButton);
            palette.Add(modes);
            var shapes = Layout("shape-grid");
            foreach (var kind in PaletteKinds)
            {
                var chosen = kind;
                var button = ShapeButton(IconFor(kind), KindKey(kind), () => AddShape(chosen));
                paletteButtons.Add(button);
                shapes.Add(button);
            }
            palette.Add(shapes);
            palette.Add(Classed(Localized(new Label(), "studio.more"), "palette-title"));
            var own = Layout("shape-grid");
            var draw = ShapeButton(Icon.Draw, "shape.draw", StartDrawing);
            var upload = ShapeButton(Icon.Import, "shape.import", ImportFromDialog);
            paletteButtons.Add(draw);
            paletteButtons.Add(upload);
            own.Add(draw);
            own.Add(upload);
            palette.Add(own);
            main.Add(palette);
            studioViewport = Layout("studio-viewport");
            main.Add(studioViewport);
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
            button.Add(Localized(new Label(), key));
            return button;
        }

        Button ShapeButton(Icon icon, string key, Action onClick)
        {
            var button = new Button(onClick) { focusable = false };
            button.AddToClassList("shape-button");
            button.Add(Classed(new IconView(icon), "shape-icon"));
            button.Add(Classed(Localized(new Label(), key), "shape-label"));
            return button;
        }

        void RefreshStudioChrome()
        {
            if (studio == null) return;
            studioTitle.text = Tr("studio.title") + " · " + Robot.Name;
            foreach (var entry in toolButtons) entry.Value.EnableInClassList("tool-button--active", entry.Key == studioTool && !drawing);
            for (int i = 0; i < snapButtons.Count; i++) snapButtons[i].EnableInClassList("snap-button--active", Mathf.Approximately(SnapSteps[i], studioSnap));
            solidButton.EnableInClassList("switch-button--active", !studioHoles);
            holeButton.EnableInClassList("switch-button--active", studioHoles);
            foreach (var button in paletteButtons) button.EnableInClassList("shape-button--hole", studioHoles);
            partsButton.EnableInClassList("tool-button--active", studioShowParts);
            drawButton.EnableInClassList("tool-button--active", drawing);
            undoButton.SetEnabled(undo.Count > 0);
            redoButton.SetEnabled(redo.Count > 0);
            UpdateStudioStatus();
        }

        void UpdateStudioStatus()
        {
            if (studio == null) return;
            var f = SelectedFeature;
            string hintKey = drawing ? "studio.hint.draw" : f == null ? "studio.hint.none"
                : studioTool == StudioTool.Rotate ? "studio.hint.rotate" : studioTool == StudioTool.Size ? "studio.hint.size" : "studio.hint.move";
            string text = Tr(hintKey);
            if (f != null && !drawing)
                text = SpikeStrings.Format("studio.selected", FeatureName(f), f.Hole ? "(" + Tr("studio.holeTag") + ")" : "",
                    f.SizeX, f.SizeY, f.SizeZ, f.X, f.Y, f.Z) + "   ·   " + text;
            studioStatus.text = text;
            var meshes = shown?.Body;
            if (meshes != null)
            {
                double volumeCm3 = meshes.VolumeMm3 / 1000.0;
                studioStats.text = SpikeStrings.Format("studio.stats", volumeCm3, volumeCm3 * BodyDesign.DensityGPerCm3(Design.Body.Material), meshes.BuildMs);
            }
        }

        void SetStudioTool(StudioTool tool)
        {
            CancelDrawing();
            studioTool = tool;
            RefreshStudioChrome();
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
        /// Centres the camera's picture in the free space between the palette and the inspector, with an
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
                grid = new GameObject("Grid").transform;
                grid.SetParent(studioScene, false);
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
                for (int a = 0; a < 3; a++) axisMaterials[a] = OverlayMat(AxisColours[a], true);
                hotMaterial = OverlayMat(new Color(1f, 0.86f, 0.25f, 1f), true);
                lineMaterial = OverlayMat(new Color(0.31f, 0.76f, 1.0f, 1f), true);
                lineMaterial.SetFloat("_Shade", 0);
                lineMaterial.SetFloat("_Rim", 0);
            }

            gizmo = new GameObject("Gizmo").transform;
            gizmo.SetParent(studioScene, false);
            gizmoParts.Clear();
            for (int a = 0; a < 3; a++)
            {
                GizmoPart(ProceduralMeshes.Arrow, Grip.Move, a, 1);
                GizmoPart(ProceduralMeshes.Ring, Grip.Turn, a, 1);
                GizmoPart(ProceduralMeshes.RoundedBox(Vector3.one, 0.22f), Grip.Size, a, 1);
                GizmoPart(ProceduralMeshes.RoundedBox(Vector3.one, 0.22f), Grip.Size, a, -1);
            }
            gizmo.gameObject.SetActive(false);
        }

        void GizmoPart(Mesh mesh, Grip grip, int axis, int sign)
        {
            var go = new GameObject($"{grip}{"XYZ"[axis]}{(sign > 0 ? "+" : "-")}");
            go.transform.SetParent(gizmo, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = axisMaterials[axis];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            gizmoParts.Add((renderer, grip, axis, sign));
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
            gizmo = null;
            grid = null;
            ghosts.Clear();
            gizmoParts.Clear();
            if (drawLine != null) Destroy(drawLine.gameObject);
            drawLine = null;
            foreach (var dot in drawDots) if (dot != null) Destroy(dot.gameObject);
            drawDots.Clear();
        }

        /// <summary>
        /// New ghosts for the model just shown: every shape's own mesh, which a click picks and which shows a hole
        /// or the selected shape see-through. The meshes belong to the model, so ghosts are made again with it.
        /// </summary>
        void RebuildGhosts()
        {
            if (studioScene == null || shown?.Body == null) return;
            foreach (var ghost in ghosts.Values) Destroy(ghost.Outer.gameObject);
            ghosts.Clear();
            var body = shown.Body;
            foreach (var (id, mesh, _) in body.Features) AddGhost(id, mesh, body.Source);
            for (int i = 0; i < body.Loose.Count && i < body.NotClosed.Count; i++) AddGhost(body.NotClosed[i], body.Loose[i], body.Source);
            ApplyPartsVisibility();
            UpdateStudioScene();
            UpdateStudioStatus();
        }

        void AddGhost(string id, Mesh mesh, BodyDesign? source)
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
            inner.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            inner.gameObject.AddComponent<StudioShape>().Id = id;
            ghosts[id] = new Ghost { Outer = outer, Inner = inner, Renderer = renderer, Built = built.Clone() };
        }

        /// <summary>
        /// Every frame in the Studio: each ghost moves from where its mesh was built to where the shape is now
        /// (the parent carries the new place, turn and size; the child undoes the old place and turn), the handles
        /// follow the selected shape at a constant size on the screen, and the grid lies on the deck.
        /// </summary>
        void UpdateStudioScene()
        {
            if (studioScene == null || mode != EditMode.Body) return;
            var body = Design.Body;
            if (grid != null) grid.localPosition = new Vector3(0, (DesignGeometry.DeckTop(body) + 0.2f) * StudioMm, 0);
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
                bool selected = entry.Key == selectedFeature, hovered = entry.Key == hoveredFeature && drag.grip == Grip.None;
                var material = now.Hole ? (selected ? ghostHoleSelected : ghostHole) : selected ? ghostSelected : hovered ? ghostHover : null;
                ghost.Renderer.enabled = material != null;
                if (material != null) ghost.Renderer.sharedMaterial = material;
            }
            UpdateGizmo();
            UpdateDrawPreview();
        }

        static float Ratio(float now, float built) => built > 1e-4f ? now / built : 1;

        Vector3 FeatureWorld(BodyFeature f) => robotAnchor.TransformPoint(new Vector3(f.X, f.Y, f.Z) * StudioMm);

        Vector3 AxisWorld(int axis) => robotAnchor.TransformDirection(Unit(axis));

        static Vector3 Unit(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        static float Component(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        float GizmoScale(Vector3 pivot) => Vector3.Distance(view.transform.position, pivot) * GizmoFactor;

        /// <summary>How far a shape reaches from its centre along one of the chassis axes, in metres (its turned box).</summary>
        static float Extent(BodyFeature f, int axis)
        {
            var turn = Matrix4x4.Rotate(Quaternion.Euler(f.RotX, f.RotY, f.RotZ));
            return (Mathf.Abs(turn[axis, 0]) * f.SizeX + Mathf.Abs(turn[axis, 1]) * f.SizeY + Mathf.Abs(turn[axis, 2]) * f.SizeZ) / 2 * StudioMm;
        }

        /// <summary>The turn rings' radius: the usual handle size, or more for a shape that would hide them.</summary>
        float RingRadius(BodyFeature f, float scale) =>
            Mathf.Max(scale, new Vector3(f.SizeX, f.SizeY, f.SizeZ).magnitude / 2 * StudioMm * 1.1f);

        /// <summary>A move arrow along a chassis axis, from just outside the shape's side (world space).</summary>
        (Vector3 from, Vector3 to) ArrowWorld(BodyFeature f, int axis, float scale)
        {
            var pivot = FeatureWorld(f);
            var direction = AxisWorld(axis);
            float start = Extent(f, axis) + 0.22f * scale;
            return (pivot + direction * start, pivot + direction * (start + 0.86f * scale));
        }

        /// <summary>Where a size handle sits: just outside the middle of the shape's face (world space).</summary>
        Vector3 KnobWorld(BodyFeature f, int axis, int sign, float scale)
        {
            var turn = robotAnchor.rotation * Quaternion.Euler(f.RotX, f.RotY, f.RotZ);
            float half = Component(new Vector3(f.SizeX, f.SizeY, f.SizeZ), axis) / 2 * StudioMm;
            return FeatureWorld(f) + turn * Unit(axis) * sign * (half + 0.1f * scale);
        }

        void UpdateGizmo()
        {
            if (gizmo == null) return;
            var f = SelectedFeature;
            bool show = f != null && !drawing;
            gizmo.gameObject.SetActive(show);
            if (!show) return;
            var pivot = FeatureWorld(f!);
            float scale = GizmoScale(pivot);
            gizmo.position = pivot;
            gizmo.rotation = robotAnchor.rotation;
            gizmo.localScale = Vector3.one * scale;
            var shapeTurn = Quaternion.Euler(f!.RotX, f.RotY, f.RotZ);
            foreach (var (renderer, grip, axis, sign) in gizmoParts)
            {
                bool active = grip == Grip.Move ? studioTool == StudioTool.Move : grip == Grip.Turn ? studioTool == StudioTool.Rotate : studioTool == StudioTool.Size;
                renderer.gameObject.SetActive(active);
                if (!active) continue;
                var t = renderer.transform;
                if (grip == Grip.Size)
                {
                    float half = Component(new Vector3(f.SizeX, f.SizeY, f.SizeZ), axis) / 2 * StudioMm;
                    t.localPosition = shapeTurn * Unit(axis) * sign * (half / scale + 0.1f);
                    t.localRotation = shapeTurn;
                    t.localScale = Vector3.one * 0.075f;
                }
                else if (grip == Grip.Move)
                {
                    // The arrow mesh starts 0.14 along its length: it then begins 0.22 handle lengths beyond the side.
                    t.localPosition = Unit(axis) * (Extent(f, axis) / scale + 0.08f);
                    t.localRotation = Quaternion.FromToRotation(Vector3.right, Unit(axis));
                    t.localScale = Vector3.one;
                }
                else
                {
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.FromToRotation(Vector3.right, Unit(axis));
                    t.localScale = Vector3.one * (RingRadius(f, scale) / scale);
                }
                var lit = drag.grip != Grip.None ? drag : hot;
                bool isHot = lit.grip == grip && lit.axis == axis && (grip != Grip.Size || lit.sign == sign);
                renderer.sharedMaterial = isHot ? hotMaterial : axisMaterials[axis];
            }
        }

        // ------------------------------------------------------------------ picking

        Vector2 Screen2(Vector3 world) => view.WorldToScreenPoint(world);

        /// <summary>The handle under the mouse, found on the screen: arrows and rings as lines, knobs as points.</summary>
        (Grip grip, int axis, int sign) PickHandle(Vector2 mouse)
        {
            var f = SelectedFeature;
            if (f == null || drawing || gizmo == null || !gizmo.gameObject.activeSelf) return default;
            var pivot = FeatureWorld(f);
            float scale = GizmoScale(pivot);
            (Grip, int, int) best = default;
            float bestDistance = float.MaxValue;
            void Consider(float distance, float limit, Grip grip, int axis, int sign)
            {
                if (distance < limit && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (grip, axis, sign);
                }
            }
            for (int a = 0; a < 3; a++)
            {
                var axis = AxisWorld(a);
                switch (studioTool)
                {
                    case StudioTool.Move:
                    {
                        var (from, to) = ArrowWorld(f, a, scale);
                        Consider(DistanceToSegment(mouse, Screen2(from), Screen2(to)), 10, Grip.Move, a, 1);
                        break;
                    }
                    case StudioTool.Rotate:
                    {
                        var u = AxisWorld((a + 1) % 3);
                        var v = AxisWorld((a + 2) % 3);
                        float radius = RingRadius(f, scale);
                        Vector2 previous = Screen2(pivot + u * radius);
                        for (int k = 1; k <= 48; k++)
                        {
                            float angle = k * Mathf.PI * 2 / 48;
                            Vector2 next = Screen2(pivot + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius);
                            Consider(DistanceToSegment(mouse, previous, next), 8, Grip.Turn, a, 1);
                            previous = next;
                        }
                        break;
                    }
                    case StudioTool.Size:
                        foreach (int sign in new[] { 1, -1 })
                            Consider(Vector2.Distance(mouse, Screen2(KnobWorld(f, a, sign, scale))), 12, Grip.Size, a, sign);
                        break;
                }
            }
            return best;
        }

        /// <summary>The shape under the mouse (its ghost's collider), with the point hit.</summary>
        string? ShapeUnder(Vector2 mouse, out Vector3 point)
        {
            point = default;
            if (studioScene == null) return null;
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(view.ScreenPointToRay(mouse), 5f);
            string? best = null;
            float nearest = float.MaxValue;
            foreach (var hit in hits)
            {
                var shape = hit.collider.GetComponent<StudioShape>();
                if (shape == null || hit.distance >= nearest || Design.Body.Feature(shape.Id) == null) continue;
                nearest = hit.distance;
                best = shape.Id;
                point = hit.point;
            }
            return best;
        }

        // ------------------------------------------------------------------ mouse

        /// <summary>A press in the scene: a handle starts its drag, a shape is selected and can be dragged.</summary>
        bool BeginStudioPress(Vector2 mouse)
        {
            if (drawing) return false; // points are placed on release, as clicks
            var handle = PickHandle(mouse);
            if (handle.grip != Grip.None)
            {
                StartStudioDrag(handle, mouse, default);
                return true;
            }
            string? id = ShapeUnder(mouse, out var point);
            if (id == null) return false;
            if (id != selectedFeature) SelectFeature(id);
            StartStudioDrag((Grip.Shape, 0, 0), mouse, point);
            return true;
        }

        void StartStudioDrag((Grip grip, int axis, int sign) handle, Vector2 mouse, Vector3 grabPoint)
        {
            var f = SelectedFeature;
            if (f == null) return;
            drag = handle;
            dragStart = f.Clone();
            studioBefore = Design.Clone();
            studioDragChanged = false;
            dragMouse0 = mouse;
            dragPivot = FeatureWorld(f);
            dragReadout = "";
            var ray = view.ScreenPointToRay(mouse);
            switch (handle.grip)
            {
                case Grip.Move:
                    dragDirection = AxisWorld(handle.axis);
                    dragT0 = RayLineParameter(ray, dragPivot, dragDirection);
                    break;
                case Grip.Size:
                    dragDirection = robotAnchor.rotation * Quaternion.Euler(f.RotX, f.RotY, f.RotZ) * Unit(handle.axis) * handle.sign;
                    dragT0 = RayLineParameter(ray, dragPivot, dragDirection);
                    break;
                case Grip.Turn:
                    dragDirection = AxisWorld(handle.axis);
                    dragFrom = new Plane(dragDirection, dragPivot).Raycast(ray, out float enter) ? ray.GetPoint(enter) - dragPivot : Vector3.zero;
                    break;
                case Grip.Shape:
                    dragFrom = grabPoint;
                    break;
            }
            if (float.IsNaN(dragT0)) dragT0 = 0;
        }

        void UpdateStudioDrag(Vector2 mouse)
        {
            var f = SelectedFeature;
            var s = dragStart;
            if (f == null || s == null) return;
            float step = input.Shift ? 0.1f : studioSnap;
            var ray = view.ScreenPointToRay(mouse);
            var before = (f.X, f.Y, f.Z, f.RotX, f.RotY, f.RotZ, f.SizeX, f.SizeY, f.SizeZ);
            switch (drag.grip)
            {
                case Grip.Move:
                {
                    float t = RayLineParameter(ray, dragPivot, dragDirection);
                    if (float.IsNaN(t)) break;
                    float delta = (t - dragT0) / StudioMm;
                    float value = Snap(Component(new Vector3(s.X, s.Y, s.Z), drag.axis) + delta, step);
                    if (drag.axis == 0) f.X = value;
                    else if (drag.axis == 1) f.Y = value;
                    else f.Z = value;
                    dragReadout = $"{"xyz"[drag.axis]} {value:0.#} {Tr("unit.mm")}";
                    break;
                }
                case Grip.Shape:
                {
                    if (!new Plane(Vector3.up, dragFrom).Raycast(ray, out float enter) || enter > 5f) break;
                    var delta = robotAnchor.InverseTransformVector(ray.GetPoint(enter) - dragFrom) / StudioMm;
                    f.X = Snap(s.X + delta.x, step);
                    f.Z = Snap(s.Z + delta.z, step);
                    dragReadout = $"x {f.X:0.#} · z {f.Z:0.#} {Tr("unit.mm")}";
                    break;
                }
                case Grip.Turn:
                {
                    float angle;
                    var viewDirection = (dragPivot - view.transform.position).normalized;
                    if (dragFrom.sqrMagnitude > 1e-10f && Mathf.Abs(Vector3.Dot(viewDirection, dragDirection)) > 0.12f &&
                        new Plane(dragDirection, dragPivot).Raycast(ray, out float enter))
                        angle = Vector3.SignedAngle(dragFrom, ray.GetPoint(enter) - dragPivot, dragDirection);
                    else
                        angle = (mouse.x - dragMouse0.x) * 0.5f; // the ring is seen edge-on: turn with the mouse's sideways movement
                    angle = Snap(angle, input.Shift ? 1f : 15f);
                    var turned = Quaternion.AngleAxis(angle, Unit(drag.axis)) * Quaternion.Euler(s.RotX, s.RotY, s.RotZ);
                    var euler = turned.eulerAngles;
                    f.RotX = Tidy(euler.x);
                    f.RotY = Tidy(euler.y);
                    f.RotZ = Tidy(euler.z);
                    dragReadout = $"{angle:0.#}°";
                    break;
                }
                case Grip.Size:
                {
                    float t = RayLineParameter(ray, dragPivot, dragDirection);
                    if (float.IsNaN(t)) break;
                    float delta = (t - dragT0) / StudioMm;
                    bool fromCentre = input.Alt;
                    float start = Component(new Vector3(s.SizeX, s.SizeY, s.SizeZ), drag.axis);
                    float size = Mathf.Max(1, Snap(start + (fromCentre ? 2 * delta : delta), step));
                    ApplySize(f, s, drag.axis, size);
                    if (!fromCentre)
                    {
                        // The opposite face stays where it was: the centre moves by half the growth, along the handle.
                        var offset = Quaternion.Euler(s.RotX, s.RotY, s.RotZ) * Unit(drag.axis) * (drag.sign * (size - start) / 2);
                        f.X = s.X + offset.x;
                        f.Y = s.Y + offset.y;
                        f.Z = s.Z + offset.z;
                    }
                    dragReadout = $"{"xyz"[drag.axis]} {size:0.#} {Tr("unit.mm")}";
                    break;
                }
            }
            if (before != (f.X, f.Y, f.Z, f.RotX, f.RotY, f.RotZ, f.SizeX, f.SizeY, f.SizeZ))
            {
                studioDragChanged = true;
                bodyDirty = true; // the throttled rebuild in UpdateEditFrame follows the drag
                ClampDeckParts();
                UpdateStudioFields();
                UpdateStudioStatus();
            }
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
                FlushBody();
                RefreshStudioChrome();
            }
            drag = default;
            dragStart = null;
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
            if (drawing)
            {
                AddDrawPoint(mouse);
                return;
            }
            if (ShapeUnder(mouse, out _) == null) SelectFeature(null);
        }

        void UpdateStudioHover(Vector2 mouse, bool overUi)
        {
            string text = "";
            if (drag.grip != Grip.None)
            {
                hot = default;
                hoveredFeature = null;
                text = dragReadout;
            }
            else if (!overUi && !leftDown && !rightDown && !panning)
            {
                hot = PickHandle(mouse);
                hoveredFeature = hot.grip == Grip.None && !drawing ? ShapeUnder(mouse, out _) : null;
                var f = hoveredFeature == null ? null : Design.Body.Feature(hoveredFeature);
                if (f != null && hoveredFeature != selectedFeature) text = FeatureName(f);
            }
            else
            {
                hot = default;
                hoveredFeature = null;
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
            if (drawing)
            {
                if (KeyPressed(KeyCode.Escape)) CancelDrawing();
                else if (KeyPressed(KeyCode.Return) || KeyPressed(KeyCode.KeypadEnter)) FinishDrawing();
                else if (KeyPressed(KeyCode.Backspace) || KeyPressed(KeyCode.Delete) || (input.Ctrl && KeyPressed(KeyCode.Z))) RemoveLastDrawPoint();
                else return false;
                return true;
            }
            if (KeyPressed(KeyCode.W)) SetStudioTool(StudioTool.Move);
            if (KeyPressed(KeyCode.E)) SetStudioTool(StudioTool.Rotate);
            if (KeyPressed(KeyCode.R)) SetStudioTool(StudioTool.Size);
            var f = SelectedFeature;
            if (f == null) return false;
            if (KeyPressed(KeyCode.Escape))
            {
                SelectFeature(null);
                return true;
            }
            if (KeyPressed(KeyCode.Delete) || KeyPressed(KeyCode.Backspace))
            {
                DeleteFeature(f.Id);
                return true;
            }
            if (input.Ctrl && KeyPressed(KeyCode.D)) DuplicateSelected();
            if (!input.Ctrl && KeyPressed(KeyCode.M)) MirrorSelected();
            if (KeyPressed(KeyCode.H)) EditFeature(x => x.Hole = !x.Hole, rerender: true);
            if (KeyPressed(KeyCode.F)) MoveTarget(FeatureWorld(f));
            float step = input.Shift ? 0.1f : studioSnap;
            if (KeyPressed(KeyCode.LeftArrow)) Nudge(-step, 0, 0);
            if (KeyPressed(KeyCode.RightArrow)) Nudge(step, 0, 0);
            if (KeyPressed(KeyCode.UpArrow)) Nudge(0, 0, step);
            if (KeyPressed(KeyCode.DownArrow)) Nudge(0, 0, -step);
            if (KeyPressed(KeyCode.PageUp)) Nudge(0, step, 0);
            if (KeyPressed(KeyCode.PageDown)) Nudge(0, -step, 0);
            return false;
        }

        /// <summary>Moves the selected shape by a step; key presses close together make one undo step.</summary>
        void Nudge(float x, float y, float z)
        {
            var f = SelectedFeature;
            if (f == null) return;
            if (Time.unscaledTime - lastNudge > 0.6f) PushUndo();
            lastNudge = Time.unscaledTime;
            f.X += x;
            f.Y += y;
            f.Z += z;
            StudioChanged(rerender: false);
        }

        // ------------------------------------------------------------------ editing shapes

        /// <summary>After a change of the body's shapes: rebuild on the worker, update the panel and status, save soon.</summary>
        void StudioChanged(bool rerender)
        {
            bodyDirty = true;
            saveAt = Time.unscaledTime + 0.8f;
            ClampDeckParts();
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

        void SelectFeature(string? id)
        {
            if (id == selectedFeature) return;
            selectedFeature = id;
            keepProportions = id != null && Design.Body.Feature(id)?.Kind == FeatureKind.Imported; // a model keeps its shape by default
            renderSide?.Invoke();
            RefreshStudioChrome();
        }

        void AddShape(FeatureKind kind)
        {
            CancelDrawing();
            var body = Design.Body;
            var f = BodyFeature.Create(kind, studioHoles, DesignGeometry.DeckTop(body));
            PlaceFree(f);
            PushUndo();
            body.AddFeature(f);
            selectedFeature = f.Id;
            keepProportions = false;
            StudioChanged(rerender: true);
        }

        /// <summary>A new shape goes to the middle of the deck, or beside the shapes already there.</summary>
        void PlaceFree(BodyFeature f)
        {
            var spots = new[] { (0f, 0f), (35f, 0f), (-35f, 0f), (0f, 35f), (0f, -35f), (35f, 35f), (-35f, 35f), (35f, -35f), (-35f, -35f) };
            foreach (var (x, z) in spots)
            {
                bool free = true;
                foreach (var other in Design.Body.Features)
                    if (Mathf.Abs(other.X - x) < 25 && Mathf.Abs(other.Z - z) < 25) free = false;
                if (!free) continue;
                f.X = x;
                f.Z = z;
                return;
            }
        }

        void DuplicateSelected()
        {
            var f = SelectedFeature;
            if (f == null) return;
            PushUndo();
            var copy = f.Clone();
            copy.X += 10;
            copy.Z += 10;
            Design.Body.AddFeature(copy);
            selectedFeature = copy.Id;
            StudioChanged(rerender: true);
        }

        void MirrorSelected()
        {
            var f = SelectedFeature;
            if (f == null) return;
            PushUndo();
            var copy = Design.Body.AddFeature(f.MirroredX());
            selectedFeature = copy.Id;
            StudioChanged(rerender: true);
        }

        void DeleteFeature(string id)
        {
            var f = Design.Body.Feature(id);
            if (f == null) return;
            PushUndo();
            Design.Body.Features.Remove(f);
            if (selectedFeature == id) selectedFeature = null;
            StudioChanged(rerender: true);
        }

        /// <summary>Stands the shape on the deck; a hole goes down through the top plate so that it cuts it.</summary>
        void DropSelected()
        {
            EditFeature(f =>
            {
                var turn = Matrix4x4.Rotate(Quaternion.Euler(f.RotX, f.RotY, f.RotZ));
                float below = Mathf.Abs(turn.m10) * f.SizeX / 2 + Mathf.Abs(turn.m11) * f.SizeY / 2 + Mathf.Abs(turn.m12) * f.SizeZ / 2;
                var body = Design.Body;
                float floor = DesignGeometry.DeckTop(body) - (f.Hole ? body.ThicknessMm + 1 : 0);
                f.Y = floor + below;
            }, rerender: false);
        }

        // ------------------------------------------------------------------ the inspector

        /// <summary>The Studio's side panel: the selected shape, or the list of shapes and the base plates.</summary>
        void RenderStudio()
        {
            RefreshStudioChrome();
            Array.Clear(vecFields, 0, vecFields.Length);
            var f = SelectedFeature;
            if (f != null)
            {
                RenderFeature(f);
                return;
            }
            var features = Design.Body.Features;
            sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.shapeList", features.Count)), "section-title"));
            if (features.Count == 0) Info("studio.noShapes");
            foreach (var feature in features)
            {
                string id = feature.Id;
                var row = new VisualElement();
                row.AddToClassList("shape-row");
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.target is not Button) SelectFeature(id);
                });
                row.Add(Classed(new IconView(IconFor(feature.Kind)), "shape-row-icon"));
                row.Add(Classed(new Label(FeatureName(feature)), "shape-row-name"));
                if (feature.Hole) row.Add(Classed(new Label(Tr("studio.holeTag")), "shape-row-tag"));
                var remove = new Button(() => DeleteFeature(id)) { text = "✕", focusable = false };
                remove.AddToClassList("wire-remove");
                row.Add(remove);
                sideContent.Add(row);
            }
            sideContent.Add(Classed(new Label(Tr("studio.base")), "studio-section"));
            RenderBody();
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
                    DetailField("studio.corner", f.Detail, v => Mathf.Clamp(v, 0.5f, 50));
                    break;
                case FeatureKind.Tube:
                    DetailField("studio.wall", f.Detail, v => Mathf.Clamp(v, 0.4f, 50));
                    break;
                case FeatureKind.Cone:
                    DetailField("studio.top", f.Detail * 100, v => Mathf.Clamp01(v / 100));
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
            row2.Add(IconSmallButton(Icon.Trash, "studio.delete", () => DeleteFeature(f.Id)));
            sideContent.Add(row2);

            sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.shapeVolume", f.ApproximateVolume() / 1000)), "info-text"));
            var meshes = shown?.Body;
            if (meshes != null)
            {
                double cm3 = meshes.VolumeMm3 / 1000;
                string material = Tr(Design.Body.Material switch { BodyMaterial.Pla => "body.pla", BodyMaterial.Plywood => "body.plywood", _ => "body.acrylic" });
                sideContent.Add(Classed(new Label(SpikeStrings.Format("studio.bodyMass", cm3 * BodyDesign.DensityGPerCm3(Design.Body.Material), cm3, material)), "info-text"));
            }
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

        void DetailField(string key, float value, Func<float, float> toDetail)
        {
            var field = new FloatField(Tr(key)) { isDelayed = true, formatString = "0.##" };
            field.SetValueWithoutNotify(value);
            field.AddToClassList("detail-field");
            field.RegisterValueChangedCallback(e =>
            {
                if (!Mathf.Approximately(e.newValue, e.previousValue)) EditFeature(x => x.Detail = toDetail(e.newValue), rerender: false);
            });
            sideContent.Add(field);
        }

        /// <summary>Shows the selected shape's numbers while a handle moves it (not in a box being typed in).</summary>
        void UpdateStudioFields()
        {
            var f = SelectedFeature;
            if (f == null || IsTyping()) return;
            float[] values = { f.X, f.Y, f.Z, f.RotX, f.RotY, f.RotZ, f.SizeX, f.SizeY, f.SizeZ };
            for (int i = 0; i < vecFields.Length; i++) vecFields[i]?.SetValueWithoutNotify(values[i]);
        }

        static string KindKey(FeatureKind kind) => kind switch
        {
            FeatureKind.RoundedBox => "shape.rounded",
            FeatureKind.Cylinder => "shape.cylinder",
            FeatureKind.Cone => "shape.cone",
            FeatureKind.Sphere => "shape.sphere",
            FeatureKind.Wedge => "shape.wedge",
            FeatureKind.Tube => "shape.tube",
            FeatureKind.Extrusion => "shape.extrusion",
            FeatureKind.Imported => "shape.imported",
            _ => "shape.box",
        };

        static Icon IconFor(FeatureKind kind) => kind switch
        {
            FeatureKind.RoundedBox => Icon.ShapeRounded,
            FeatureKind.Cylinder => Icon.ShapeCylinder,
            FeatureKind.Cone => Icon.ShapeCone,
            FeatureKind.Sphere => Icon.ShapeSphere,
            FeatureKind.Wedge => Icon.ShapeWedge,
            FeatureKind.Tube => Icon.ShapeTube,
            FeatureKind.Extrusion => Icon.Draw,
            FeatureKind.Imported => Icon.Import,
            _ => Icon.ShapeBox,
        };

        /// <summary>"Box 2", "Tube 5", or an imported file's name.</summary>
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
            SelectFeature(null);
            drawing = true;
            drawPoints.Clear();
            RefreshStudioChrome();
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
            if (drawPoints.Count >= 3 && Vector2.Distance(mouse, Screen2(DeckWorld(drawPoints[0]))) < 12)
            {
                FinishDrawing();
                return;
            }
            if (!DeckPoint(mouse, out var mm)) return;
            float step = input.Shift ? 0.1f : studioSnap;
            var point = new Vector2(Snap(mm.x, step), Snap(mm.y, step));
            if (drawPoints.Count > 0 && Vector2.Distance(point, drawPoints[drawPoints.Count - 1]) < 0.05f) return;
            drawPoints.Add(point);
        }

        void RemoveLastDrawPoint()
        {
            if (drawPoints.Count > 0) drawPoints.RemoveAt(drawPoints.Count - 1);
            else CancelDrawing();
        }

        /// <summary>The outline becomes a shape 10 mm tall on the deck, or a hole through the top plate.</summary>
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
            var body = Design.Body;
            var flat = new List<float>();
            foreach (var p in drawPoints)
            {
                flat.Add(p.x);
                flat.Add(p.y);
            }
            float height = studioHoles ? body.ThicknessMm + 10 : 10;
            var f = BodyFeature.FromOutline(flat, DesignGeometry.DeckTop(body), height, studioHoles);
            if (f == null)
            {
                ShowToast(Tr("studio.tooFew"));
                return;
            }
            drawing = false;
            drawPoints.Clear();
            PushUndo();
            body.AddFeature(f);
            selectedFeature = f.Id;
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

        Vector3 DeckWorld(Vector2 mm) => robotAnchor.TransformPoint(new Vector3(mm.x, DesignGeometry.DeckTop(Design.Body) + 0.3f, mm.y) * StudioMm);

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
            foreach (var p in drawPoints) positions.Add(DeckWorld(p));
            var mouse = input.Position;
            if (!IsPointerOverUi(mouse) && DeckPoint(mouse, out var mm))
            {
                float step = input.Shift ? 0.1f : studioSnap;
                positions.Add(DeckWorld(new Vector2(Snap(mm.x, step), Snap(mm.y, step))));
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
                var world = DeckWorld(drawPoints[i]);
                drawDots[i].position = world;
                float size = Vector3.Distance(view.transform.position, world) * (i == 0 && drawPoints.Count >= 3 ? 0.016f : 0.009f);
                drawDots[i].localScale = Vector3.one * size;
            }
        }

        // ------------------------------------------------------------------ importing a model

        void ImportFromDialog()
        {
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
        /// millimetres (a file whose longest side is under 2 units is taken to be in metres), standing on the deck.
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
            var body = Design.Body;
            var f = new BodyFeature
            {
                Kind = FeatureKind.Imported,
                Hole = studioHoles,
                MeshFile = stored,
                SizeX = Mathf.Max(0.5f, size.x),
                SizeY = Mathf.Max(0.5f, size.y),
                SizeZ = Mathf.Max(0.5f, size.z),
            };
            f.Y = DesignGeometry.DeckTop(body) + f.SizeY / 2;
            PushUndo();
            body.AddFeature(f);
            selectedFeature = f.Id;
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

        // ------------------------------------------------------------------ benchmark: the Studio by mouse

        /// <summary>
        /// The Body Studio as a player uses it, through the same mouse, key and UI Toolkit paths: a box from the
        /// palette, moved by its arrow, dragged on the deck, sized by a handle and turned by a ring; a cylinder hole;
        /// an outline drawn with five clicks; an uploaded STL bracket; duplicate, mirror, delete, undo and redo; a
        /// typed value; and an STL export of the result. The robot's body is put back afterwards, so the arena run
        /// that follows is the same as before.
        /// </summary>
        IEnumerator StudioByMouse()
        {
            var report = SpikeReport.Text;
            report.AppendLine("body studio (docs/08) by mouse and keys:");
            var saved = Design.Clone();
            OnAction("act.body");
            yield return Frames(3);
            float volume0 = (float)(shown?.Body?.VolumeMm3 ?? 0);
            string Check(bool ok) => ok ? "yes" : "NO";

            // A box from the palette, clicked with a pointer event on the button.
            SetHoleMode(false);
            yield return ClickElement(paletteButtons[0]);
            var box = SelectedFeature;
            bool added = box != null && box.Kind == FeatureKind.Box && Design.Body.Features.Count == saved.Body.Features.Count + 1;
            yield return WaitForBody();
            yield return Frames(2);

            // Move: drag the x arrow 80 pixels along its direction on the screen.
            SetStudioTool(StudioTool.Move);
            yield return Frames(1);
            float x0 = box!.X;
            var pivot = FeatureWorld(box);
            float scale = GizmoScale(pivot);
            var (arrowStart, arrowEnd) = ArrowWorld(box, 0, scale);
            Vector2 arrowFrom = Screen2(Vector3.Lerp(arrowStart, arrowEnd, 0.5f)), arrowDirection = (Screen2(arrowEnd) - Screen2(arrowStart)).normalized;
            yield return MouseDrag(arrowFrom, arrowFrom + arrowDirection * 80);
            float movedX = box.X - x0;

            // Drag the box itself on the deck, 25 mm toward the front.
            yield return WaitForBody();
            yield return Frames(2);
            float z0 = box.Z;
            var grab = FeatureWorld(box) + robotAnchor.up * box.SizeY / 2 * StudioMm;
            yield return MouseDrag(Screen2(grab), Screen2(grab + robotAnchor.forward * 0.025f));
            float movedZ = box.Z - z0;

            // Size: pull the +z handle outward by 40 pixels; the back face stays.
            SetStudioTool(StudioTool.Size);
            yield return Frames(1);
            float sizeZ0 = box.SizeZ, back0 = box.Z - box.SizeZ / 2;
            pivot = FeatureWorld(box);
            scale = GizmoScale(pivot);
            Vector2 knob = Screen2(KnobWorld(box, 2, 1, scale)), outward = (knob - Screen2(pivot)).normalized;
            yield return MouseDrag(knob, knob + outward * 40);
            float grown = box.SizeZ - sizeZ0, backMoved = Mathf.Abs(box.Z - box.SizeZ / 2 - back0);

            // Turn: drag along the y ring from the side facing the camera, a quarter of the way round.
            SetStudioTool(StudioTool.Rotate);
            yield return Frames(1);
            pivot = FeatureWorld(box);
            scale = GizmoScale(pivot);
            var toCamera = Vector3.ProjectOnPlane(view.transform.position - pivot, robotAnchor.up).normalized;
            float ring = RingRadius(box, scale);
            var arc = new List<PointerFrame>();
            Vector2 ringStart = Screen2(pivot + toCamera * ring);
            arc.Add(new PointerFrame { Position = ringStart });
            arc.Add(new PointerFrame { Position = ringStart, LeftPressed = true, LeftHeld = true });
            for (int i = 1; i <= 10; i++)
                arc.Add(new PointerFrame { Position = Screen2(pivot + Quaternion.AngleAxis(4.5f * i, robotAnchor.up) * toCamera * ring), LeftHeld = true });
            arc.Add(new PointerFrame { Position = arc[arc.Count - 1].Position });
            arc.Add(new PointerFrame { Position = arc[arc.Count - 1].Position });
            yield return Play(arc);
            float turned = box.RotY;

            // A cylinder hole through the deck, dragged 30 mm to the left.
            SetStudioTool(StudioTool.Move);
            yield return ClickElement(holeButton);
            yield return ClickElement(paletteButtons[2]);
            var hole = SelectedFeature!;
            yield return WaitForBody();
            yield return Frames(2);
            float holeX0 = hole.X;
            var holeTop = FeatureWorld(hole) + robotAnchor.up * hole.SizeY / 2 * StudioMm;
            yield return MouseDrag(Screen2(holeTop), Screen2(holeTop - robotAnchor.right * 0.03f));
            float holeMoved = hole.X - holeX0;
            yield return WaitForBody();
            yield return Frames(3);
            float volumeHoles = (float)(shown?.Body?.VolumeMm3 ?? 0);
            yield return SpikeReport.Capture(SpikeReport.Shot("studio-shapes"));

            // Draw a solid outline with four corners and a click back on the first.
            yield return ClickElement(solidButton);
            yield return ClickElement(drawButton);
            var corners = new[] { new Vector2(-50, -60), new Vector2(-20, -60), new Vector2(-20, -40), new Vector2(-35, -30), new Vector2(-50, -40) };
            foreach (var corner in corners) yield return MouseClick(Screen2(DeckWorld(corner)));
            yield return Frames(2);
            yield return SpikeReport.Capture(SpikeReport.Shot("studio-draw"));
            yield return MouseClick(Screen2(DeckWorld(corners[0])));
            var drawn = SelectedFeature;
            bool outline = drawn != null && drawn.Kind == FeatureKind.Extrusion && Mathf.Abs(drawn.SizeX - 30) < 0.01f && Mathf.Abs(drawn.SizeZ - 30) < 0.01f;
            yield return WaitForBody();

            // The real Windows dialog: it opens, and a helper thread closes it after a moment as Cancel would.
            string dialogResult = "not available";
            if (WinFileDialog.Available)
            {
                var closeDialog = WinFileDialog.CloseSoon(Tr("studio.fileTitle"), 600);
                string? chosen = WinFileDialog.OpenFile(Tr("studio.fileTitle"), Tr("studio.fileFilter"), "*.stl;*.obj");
                dialogResult = (closeDialog() ? "opened and was cancelled" : "did NOT open") + (chosen == null ? "" : ", returned " + chosen);
            }
            yield return Frames(2);

            // Upload: an L-shaped bracket written as a binary STL, imported as the file dialog would.
            string stlPath = Path.Combine(Path.GetDirectoryName(SpikeReport.Shot("x"))!, "studio-bracket.stl");
            WriteBracketStl(stlPath);
            bool imported = ImportModel(stlPath);
            var bracket = SelectedFeature;
            yield return WaitForBody();
            yield return Frames(3);
            bool closed = bracket != null && shown?.Body != null && !shown.Body.NotClosed.Contains(bracket.Id) && ghosts.ContainsKey(bracket.Id);
            string bracketSize = bracket == null ? "none" : $"{bracket.SizeX:0.#} × {bracket.SizeY:0.#} × {bracket.SizeZ:0.#} mm";
            yield return SpikeReport.Capture(SpikeReport.Shot("studio-import"));

            // Keys: duplicate, mirror, delete, undo and redo.
            int count = Design.Body.Features.Count;
            SelectFeature(box.Id);
            yield return Press(KeyCode.D, true);
            bool duplicated = Design.Body.Features.Count == count + 1;
            yield return Press(KeyCode.M, false);
            var mirror = SelectedFeature;
            bool mirrored = Design.Body.Features.Count == count + 2 && mirror != null && Mathf.Approximately(mirror.X, -(box.X + 10));
            yield return Press(KeyCode.Delete, false);
            bool deleted = Design.Body.Features.Count == count + 1;
            yield return Press(KeyCode.Z, true);
            bool undone = Design.Body.Features.Count == count + 2;
            yield return Press(KeyCode.Y, true);
            bool redone = Design.Body.Features.Count == count + 1;

            // A value typed into the inspector: the box's height.
            SelectFeature(box.Id);
            yield return Frames(2);
            var heightField = vecFields[7];
            if (heightField != null) heightField.value = 12;
            bool typed = Mathf.Approximately(Design.Body.Feature(box.Id)?.SizeY ?? 0, 12);

            // Drag the box around for a second while the body rebuilds, and time the frames.
            SetStudioTool(StudioTool.Move);
            yield return WaitForBody();
            yield return Frames(2);
            var frames = new List<double>();
            int buildsBefore = bodyBuildsShown;
            var from = Screen2(FeatureWorld(box) + robotAnchor.up * box.SizeY / 2 * StudioMm);
            var drag1 = new List<PointerFrame> { new PointerFrame { Position = from }, new PointerFrame { Position = from, LeftPressed = true, LeftHeld = true } };
            for (int i = 1; i <= 140; i++)
                drag1.Add(new PointerFrame { Position = from + new Vector2(60 * Mathf.Sin(i * 0.09f), 25 * Mathf.Sin(i * 0.05f)), LeftHeld = true });
            drag1.Add(new PointerFrame { Position = from });
            drag1.Add(new PointerFrame { Position = from });
            foreach (var frame in drag1) scriptedInput.Enqueue(frame);
            while (scriptedInput.Count > 0)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000.0);
            }
            int builds = bodyBuildsShown - buildsBefore;
            yield return WaitForBody();
            yield return Frames(3);
            yield return SpikeReport.Capture(SpikeReport.Shot("studio"));

            // The STL of the whole body with its shapes.
            string stl = ExportStl(Path.GetDirectoryName(SpikeReport.Shot("x")));
            var meshes = shown?.Body;
            long expected = 84 + 50L * ((meshes?.StlTriangles.Length ?? 0) / 3);
            long actual = stl.Length > 0 && File.Exists(stl) ? new FileInfo(stl).Length : -1;

            report.AppendLine($"  palette click added a box: {Check(added)}; x arrow dragged 80 px moved it {movedX:0.#} mm (5 mm snap); " +
                              $"dragging the box moved it {movedZ:0.#} mm forward; the +z handle grew it {grown:0.#} mm and its back face moved {backMoved:0.##} mm; " +
                              $"the y ring turned it to {turned:0.#}°");
            report.AppendLine($"  cylinder hole: dragged {holeMoved:0.#} mm; body volume {volume0 / 1000:F1} cm³ before the shapes, {volumeHoles / 1000:F1} cm³ with the box and the hole; " +
                              $"outline of 5 clicks became a 30 × 30 mm drawn shape: {Check(outline)}; the Windows file dialog {dialogResult}; " +
                              $"STL bracket uploaded: {Check(imported)}, {bracketSize}, closed solid: {Check(closed)}");
            report.AppendLine($"  keys: Ctrl+D duplicated: {Check(duplicated)}, M mirrored to the other side: {Check(mirrored)}, Del deleted: {Check(deleted)}, " +
                              $"Ctrl+Z undid: {Check(undone)}, Ctrl+Y redid: {Check(redone)}; typed height 12 mm: {Check(typed)}");
            report.AppendLine($"  dragging a shape for {frames.Count} frames: {SpikeReport.FrameStats(frames)}; the body was rebuilt about {builds} times; " +
                              $"{Design.Body.Features.Count} shapes, Manifold {meshes?.BuildMs ?? 0:F1} ms; STL export {actual} bytes for {(meshes?.StlTriangles.Length ?? 0) / 3} triangles " +
                              $"(expected {expected}): {(actual == expected ? "OK" : "WRONG")}");
            report.AppendLine("  screenshots: -studio-shapes, -studio-draw, -studio-import, -studio");
            report.AppendLine($"  layout (panel units): studio {Bounds(studio!)}, toolbar {Bounds(studioTop)}, middle {Bounds(studioMain)}, status bar {Bounds(studioStatusBar)}, " +
                              $"view {Bounds(studioViewport)}, inspector {Bounds(sidePanel)}, panel {root.panel?.visualTree.layout.size}");

            // Back to the robot as it was.
            Robot.Design = saved;
            undo.Clear();
            redo.Clear();
            selectedFeature = null;
            DesignChanged();
            yield return WaitForBody();
        }

        static string Bounds(VisualElement e) => $"({e.worldBound.xMin:0}, {e.worldBound.yMin:0}) {e.worldBound.width:0} × {e.worldBound.height:0}";

        /// <summary>A UI Toolkit click (pointer down and up) in the middle of an element.</summary>
        IEnumerator ClickElement(VisualElement element)
        {
            var centre = element.worldBound.center;
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
