using System;
using System.Collections;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Shaping a wire in Wire (docs/03 §6.2; the owner, 2026-09-25: the player decides "where it will go ... where
    /// it will rotate, where it will pass, where it will be glued"). With a wire chosen, a drag on it pulls out a
    /// bend point the wire then passes through; a point's handle drags it, a glued one sliding over the plates and
    /// parts; Glue, then a click on a plate or part, glues the wire there under a blob of hot glue. Del removes the
    /// chosen point and every change can be undone. <see cref="WireRouter"/> lays the wire between the points as
    /// between its pins, so it still goes round everything.
    /// </summary>
    public sealed partial class GarageSpike
    {
        enum PointGrip { None, Wire, Point }

        const float PointHandlePx = 11;

        int selectedPoint = -1;     // in the chosen wire's points
        bool gluing;                // Glue is on: a click on a plate or part glues the chosen wire there
        PointGrip pointGrip;        // what the left press took hold of
        int pointGripIndex;         // the point held
        Vector3 pointGripWorld;     // where on the wire the press was
        Plane pointPlane;           // a free point moves in this plane, facing the camera
        RobotDesign? pointBefore;   // the design as it was at the press, for undo
        bool pointMoved;
        HandleOverlay? wireOverlay;

        void BuildWireOverlay()
        {
            wireOverlay = new HandleOverlay();
            root.Insert(0, wireOverlay); // under the panels, over the 3D view
        }

        WireInstance? ChosenWire => mode == EditMode.Wire && selectedWire >= 0 && selectedWire < Design.Wires.Count ? Design.Wires[selectedWire] : null;

        /// <summary>Forgets the chosen point, the grip and Glue (a new wire chosen, another mode, an undo).</summary>
        void ResetPointState()
        {
            selectedPoint = -1;
            gluing = false;
            pointGrip = PointGrip.None;
            pointBefore = null;
            pointMoved = false;
        }

        // ------------------------------------------------------------------ the mouse

        /// <summary>
        /// A left press in Wire with a wire chosen: on one of its points' handles, the point is taken; on the wire
        /// itself, a drag will pull out a new bend point there. False when the press is for something else.
        /// </summary>
        bool BeginPointPress(Vector2 mouse)
        {
            pointGrip = PointGrip.None;
            if (ChosenWire == null || gluing || shown == null) return false;
            int handle = PointHandleUnder(mouse);
            if (handle >= 0 && MarkOf(handle) is { } mark)
            {
                pointGrip = PointGrip.Point;
                pointGripIndex = handle;
                pointPlane = new Plane(-view.transform.forward, shown.Root.transform.TransformPoint(mark.At));
            }
            else
            {
                var (pin, wire) = WireModeTarget(mouse);
                if (pin != null || wire != selectedWire || !NearestOnWire(selectedWire, mouse, out pointGripWorld)) return false;
                pointGrip = PointGrip.Wire;
            }
            pointBefore = Design.Clone();
            pointMoved = false;
            return true;
        }

        void UpdatePointDrag(Vector2 mouse)
        {
            var wire = ChosenWire;
            if (wire == null || shown == null || (!pointMoved && (mouse - pressPosition).magnitude <= 4)) return;
            if (pointGrip == PointGrip.Wire)
            {
                // The press on the wire becomes a bend point there, which the mouse then carries.
                var at = MmOf(pointGripWorld);
                int index = InsertIndex(selectedWire, pointGripWorld);
                wire.Points.Insert(index, new WirePoint { X = at.x, Y = at.y, Z = at.z });
                pointGrip = PointGrip.Point;
                pointGripIndex = index;
                selectedPoint = index;
                pointPlane = new Plane(-view.transform.forward, pointGripWorld);
            }
            if (pointGripIndex < 0 || pointGripIndex >= wire.Points.Count) return;
            var point = wire.Points[pointGripIndex];
            var ray = view.ScreenPointToRay(mouse);
            if (point.Glued)
            {
                // A glued point slides over whatever plate or part is under the mouse.
                var hit = RobotSurfaceUnder(ray);
                if (hit == null || hit.Value.Wheel) return;
                var glued = DesignGeometry.GluePoint(Design, hit.Value.Owner, hit.Value.Point, hit.Value.Normal);
                if (glued == null) return;
                wire.Points[pointGripIndex] = glued;
            }
            else
            {
                if (!pointPlane.Raycast(ray, out float enter)) return;
                var clear = ClearTowardCamera(MmOf(ray.GetPoint(enter)), ray);
                (point.X, point.Y, point.Z) = (clear.x, clear.y, clear.z);
            }
            pointMoved = true;
            shown.RebuildWire(Design, selectedWire);
        }

        /// <summary>The release: a moved point is kept (and can be undone); a click on a handle chooses its point.</summary>
        void EndPointDrag()
        {
            var grip = pointGrip;
            pointGrip = PointGrip.None;
            if (pointMoved && pointBefore != null)
            {
                RecordUndo(pointBefore);
                selectedPoint = pointGripIndex;
                DesignChanged();
            }
            else
            {
                selectedPoint = grip == PointGrip.Point ? pointGripIndex : -1;
                renderSide?.Invoke();
            }
            pointBefore = null;
            pointMoved = false;
        }

        /// <summary>With Glue on, a click: the chosen wire is glued to the plate or part under the mouse.</summary>
        void GlueAt(Vector2 mouse)
        {
            var wire = ChosenWire;
            if (wire == null)
            {
                gluing = false;
                return;
            }
            var hit = RobotSurfaceUnder(view.ScreenPointToRay(mouse));
            if (hit == null || hit.Value.Wheel)
            {
                ShowToast(Tr(hit == null ? "wire.glueNothing" : "wire.glueWheel"));
                return;
            }
            var point = DesignGeometry.GluePoint(Design, hit.Value.Owner, hit.Value.Point, hit.Value.Normal);
            if (point == null) return;
            PushUndo();
            int index = InsertIndex(selectedWire, robotAnchor.TransformPoint(V(hit.Value.Point) * StudioMm));
            wire.Points.Insert(index, point);
            selectedPoint = index;
            DesignChanged();
        }

        // ------------------------------------------------------------------ the panel's buttons and keys

        void ToggleGluing()
        {
            gluing = !gluing && ChosenWire != null;
            UpdateHint();
            renderSide?.Invoke();
        }

        /// <summary>The chosen point glued to the plate or part nearest it, or a glued one let go, a little off its surface.</summary>
        void GlueOrFreeChosenPoint()
        {
            var wire = ChosenWire;
            if (wire == null || shown == null || selectedPoint < 0 || selectedPoint >= wire.Points.Count || MarkOf(selectedPoint) is not { } mark) return;
            var at = MmOf(shown.Root.transform.TransformPoint(mark.At));
            var point = wire.Points[selectedPoint];
            WirePoint changed;
            if (point.Glued)
            {
                var off = shown.Router.Clear((at.x + mark.Normal.x * 5, at.y + mark.Normal.y * 5, at.z + mark.Normal.z * 5));
                changed = new WirePoint { X = off.x, Y = off.y, Z = off.z };
            }
            else
            {
                var hit = shown.Router.NearestSurface((at.x, at.y, at.z), 20);
                var glued = hit == null || hit.Value.Wheel ? null : DesignGeometry.GluePoint(Design, hit.Value.Owner, hit.Value.Point, hit.Value.Normal);
                if (glued == null)
                {
                    ShowToast(Tr("wire.glueFar"));
                    return;
                }
                changed = glued;
            }
            PushUndo();
            wire.Points[selectedPoint] = changed;
            DesignChanged();
        }

        void RemoveChosenPoint()
        {
            var wire = ChosenWire;
            if (wire == null || selectedPoint < 0 || selectedPoint >= wire.Points.Count) return;
            PushUndo();
            wire.Points.RemoveAt(selectedPoint);
            selectedPoint = -1;
            DesignChanged();
        }

        /// <summary>Takes all the chosen wire's points away: the router finds its own way again.</summary>
        void StraightenChosenWire()
        {
            var wire = ChosenWire;
            if (wire == null || wire.Points.Count == 0) return;
            PushUndo();
            wire.Points.Clear();
            selectedPoint = -1;
            DesignChanged();
        }

        /// <summary>Esc and Del for points and Glue; true when the key was used.</summary>
        bool PointKeys(bool delete)
        {
            if (KeyPressed(KeyCode.Escape))
            {
                if (gluing) ToggleGluing();
                else if (selectedPoint >= 0)
                {
                    selectedPoint = -1;
                    renderSide?.Invoke();
                }
                else return false;
                return true;
            }
            if (delete && selectedPoint >= 0 && ChosenWire != null)
            {
                RemoveChosenPoint();
                return true;
            }
            return false;
        }

        /// <summary>The Wire panel's section for the chosen wire: how to shape it, Glue, and the chosen point's buttons.</summary>
        void RenderWireShape()
        {
            var wire = ChosenWire;
            if (wire == null) return;
            Section("wire.shape", Icon.Bend);
            int glued = wire.Points.FindAll(p => p.Glued).Count;
            sideContent.Add(Classed(new Label(Tr(gluing ? "wire.gluingHint" : "wire.shapeHint")), gluing ? "try-line" : "info-text"));
            var row = Layout("seg-row");
            var glue = new Button(ToggleGluing) { focusable = false };
            glue.Add(Classed(new IconView(Icon.Glue), "seg-icon"));
            glue.Add(new Label(Tr("wire.glue")));
            glue.AddToClassList("seg-button");
            glue.AddToClassList("look-button");
            glue.EnableInClassList("seg-button--active", gluing);
            row.Add(glue);
            var straighten = new Button(StraightenChosenWire) { focusable = false };
            straighten.Add(Classed(new IconView(Icon.Route), "seg-icon"));
            straighten.Add(new Label(Tr("wire.straighten")));
            straighten.AddToClassList("seg-button");
            straighten.AddToClassList("look-button");
            straighten.SetEnabled(wire.Points.Count > 0);
            row.Add(straighten);
            sideContent.Add(row);
            if (wire.Points.Count > 0) sideContent.Add(Classed(new Label(SpikeStrings.Format("wire.points", wire.Points.Count, glued)), "bin-sub"));
            if (selectedPoint < 0 || selectedPoint >= wire.Points.Count) return;
            var buttons = Layout("repair-buttons");
            buttons.Add(IconSmallButton(Icon.Glue, wire.Points[selectedPoint].Glued ? "wire.unglue" : "wire.gluePoint", GlueOrFreeChosenPoint));
            buttons.Add(IconSmallButton(Icon.Trash, "wire.removePoint", RemoveChosenPoint));
            sideContent.Add(buttons);
        }

        /// <summary>The label that follows the mouse with Glue on: what the wire would be glued to.</summary>
        string GlueHoverText(Vector2 mouse)
        {
            var hit = RobotSurfaceUnder(view.ScreenPointToRay(mouse));
            if (hit == null) return "";
            if (hit.Value.Wheel) return Tr("wire.glueWheel");
            var part = Design.Find(hit.Value.Owner);
            var shape = part == null ? Design.Body.Feature(hit.Value.Owner) : null;
            return SpikeStrings.Format("wire.glueHere", part != null ? PartLabel(part.Id) : shape != null ? FeatureName(shape) : hit.Value.Owner);
        }

        // ------------------------------------------------------------------ handles

        /// <summary>The chosen wire's points as handles over the 3D view: a circle where it bends, a square where it is glued.</summary>
        void UpdateWireHandles()
        {
            if (wireOverlay == null) return;
            wireOverlay.Begin();
            if (ChosenWire != null && shown != null && selectedWire < shown.WireMarks.Count && root.panel != null)
            {
                wireOverlay.Halo = 1.2f;
                int hovered = pointGrip == PointGrip.None && !gluing ? PointHandleUnder(input.Position) : -1;
                int held = pointGrip == PointGrip.Point ? pointGripIndex : selectedPoint;
                foreach (var mark in shown.WireMarks[selectedWire])
                {
                    var screen = view.WorldToScreenPoint(shown.Root.transform.TransformPoint(mark.At));
                    if (screen.z <= 0) continue;
                    var at = ToPanel(screen);
                    bool chosen = mark.Point == held;
                    float size = chosen || mark.Point == hovered ? 13 : 11;
                    var fill = chosen ? HandleHot : Color.white;
                    if (mark.Glued) wireOverlay.Square(at, size, fill, HandleInk, HandleBorderPx);
                    else wireOverlay.Circle(at, size / 2, fill, HandleInk, HandleBorderPx);
                }
            }
            wireOverlay.End();
        }

        /// <summary>The chosen wire's point whose handle is within <see cref="PointHandlePx"/> of the mouse (its index), or -1.</summary>
        int PointHandleUnder(Vector2 mouse)
        {
            if (ChosenWire == null || shown == null || selectedWire >= shown.WireMarks.Count) return -1;
            int best = -1;
            float bestDistance = PointHandlePx;
            foreach (var mark in shown.WireMarks[selectedWire])
            {
                var screen = view.WorldToScreenPoint(shown.Root.transform.TransformPoint(mark.At));
                if (screen.z <= 0) continue;
                float d = Vector2.Distance(mouse, screen);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = mark.Point;
            }
            return best;
        }

        WireMark? MarkOf(int point)
        {
            if (shown == null || selectedWire < 0 || selectedWire >= shown.WireMarks.Count) return null;
            foreach (var mark in shown.WireMarks[selectedWire]) if (mark.Point == point) return mark;
            return null;
        }

        // ------------------------------------------------------------------ benchmark

        /// <summary>
        /// Shaping a wire with the mouse, as a player does: the D5 → IN1 jumper chosen by a click and pulled up into
        /// a bend point by a drag on it; Glue, then a click on an open spot of the top deck; Esc, and the glue's
        /// handle slid along the deck; the bend point chosen by a click on its handle, removed with Del and brought
        /// back with Ctrl+Z; every change then undone.
        /// </summary>
        IEnumerator ShapeWireByMouse()
        {
            var report = SpikeReport.Text;
            ViewWholeRobot();
            for (int i = 0; i < 300 && viewGoal != null; i++) yield return null;
            yield return Frames(2);
            int index = Design.Wires.FindIndex(w => w.FromPart == "uno1" && w.FromPin == "D5");
            if (index < 0 || shown == null)
            {
                report.AppendLine("  shaping a wire by mouse: NO D5 wire to shape");
                yield break;
            }
            WireInstance Wire() => Design.Wires[index];

            yield return MouseClick(WireScreen(index));
            bool chosen = selectedWire == index;

            // A drag up from the middle of the wire pulls out a bend point there, and the wire passes through it.
            var grab = WireScreen(index);
            yield return MouseDrag(grab, grab + new Vector2(0, 45));
            var bend = Wire().Points.Count == 1 ? Wire().Points[0] : null;
            bool bent = bend != null && !bend.Glued;
            var marks = shown!.WireMarks[index];
            bool through = bent && marks.Length == 1 && Vector3.Distance(marks[0].At / StudioMm, new Vector3(bend!.X, bend.Y, bend.Z)) < 0.5f;
            yield return Frames(2);

            // Glue, then a click on an open spot of the top deck that the camera sees.
            var deck = Design.Body.Features.Find(f => f.Kind == FeatureKind.Plate && f.Y > 60);
            Vector2? spot = null;
            float deckTop = deck == null ? 0 : deck.Y + deck.SizeY / 2;
            foreach (var mm in new[] { new Vector3(-40, deckTop, 40), new Vector3(-45, deckTop, 20), new Vector3(-20, deckTop, 60), new Vector3(40, deckTop, 60), new Vector3(45, deckTop, -60) })
            {
                var screen = Screen2(WorldOf(mm));
                if (deck != null && RobotSurfaceUnder(view.ScreenPointToRay(screen))?.Owner == deck.Id)
                {
                    spot = screen;
                    break;
                }
            }
            Button? glueButton = null;
            sideContent.Query<Button>().ForEach(b => { if (b.Q<Label>()?.text == Tr("wire.glue")) glueButton = b; });
            if (glueButton != null) yield return ClickElement(glueButton);
            bool gluingOn = gluing;
            if (spot != null) yield return MouseClick(spot.Value);
            var gluePoint = Wire().Points.Find(p => p.Glued);
            bool glued = gluePoint != null && deck != null && gluePoint.Shape == deck.Id && Wire().Points.Count == 2;
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-wire-points"));

            // Esc ends gluing; the glue's handle slides it 30 pixels along the deck.
            yield return Press(KeyCode.Escape, false);
            bool slid = false;
            int glueIndex = gluePoint == null ? -1 : Wire().Points.IndexOf(gluePoint);
            if (glueIndex >= 0 && MarkOf(glueIndex) is { } glueMark)
            {
                var before = DesignGeometry.PointPlace(Design, gluePoint!)!.Value.at;
                var from = Screen2(shown!.Root.transform.TransformPoint(glueMark.At));
                yield return MouseDrag(from, from + new Vector2(-30, 0));
                var now = Wire().Points[glueIndex];
                var after = DesignGeometry.PointPlace(Design, now)?.at ?? before;
                float moved = new Vector3(after.x - before.x, after.y - before.y, after.z - before.z).magnitude;
                slid = now.Glued && now.Shape == deck!.Id && moved > 3;
            }

            // A close look at the glue with nothing chosen: the blob of hot glue over the wire on the deck.
            yield return Press(KeyCode.Escape, false);
            yield return Press(KeyCode.Escape, false);
            var blob = Array.Find(shown!.WireMarks[index], m => m.Glued);
            if (blob.Glued)
            {
                GlideTo(yaw, 50f, 0.13f, shown.Root.transform.TransformPoint(blob.At));
                for (int i = 0; i < 300 && viewGoal != null; i++) yield return null;
                yield return Frames(3);
                yield return SpikeReport.Capture(SpikeReport.Shot("garage-wire-glue"));
            }
            ViewWholeRobot();
            for (int i = 0; i < 300 && viewGoal != null; i++) yield return null;
            SelectWire(index);
            yield return Frames(2);

            // The bend point: a click on its handle chooses it, Del removes it, Ctrl+Z brings it back.
            int bendIndex = Wire().Points.FindIndex(p => !p.Glued);
            bool pointChosen = false, removed = false;
            if (bendIndex >= 0 && MarkOf(bendIndex) is { } bendMark)
            {
                yield return MouseClick(Screen2(shown!.Root.transform.TransformPoint(bendMark.At)));
                pointChosen = selectedPoint == bendIndex && selectedWire == index;
                yield return Press(KeyCode.Delete, false);
                removed = Wire().Points.Count == 1 && Wire().Points[0].Glued && Design.Wires.Count > index;
            }
            yield return Press(KeyCode.Z, true);
            bool back = Wire().Points.Count == 2;
            for (int i = 0; i < 3; i++) Undo();
            bool clean = Wire().Points.Count == 0;
            yield return Frames(2);
            report.AppendLine($"  shaping a wire by mouse: a click chose it: {Yes(chosen)}; a drag on it made a bend point it passes through: {Yes(bent && through)}; " +
                              $"Glue, then a click on the top deck, glued it there: {Yes(gluingOn && glued)}; its handle slid the glue along the deck: {Yes(slid)}; " +
                              $"a click on the bend point's handle chose it: {Yes(pointChosen)}, Del removed it: {Yes(removed)}, Ctrl+Z brought it back: {Yes(back)}; " +
                              $"three undos left the wire as it was: {Yes(clean)}; screenshots -garage-wire-points, -garage-wire-glue");
        }

        // ------------------------------------------------------------------ places

        /// <summary>A world point in the chassis frame (mm).</summary>
        Vector3 MmOf(Vector3 world) => robotAnchor.InverseTransformPoint(world) / StudioMm;

        /// <summary>The first plate or part a ray from the camera meets (chassis frame, mm), by the router's picture of the robot.</summary>
        SurfaceHit? RobotSurfaceUnder(Ray ray)
        {
            if (shown == null) return null;
            var from = MmOf(ray.origin);
            var direction = robotAnchor.InverseTransformDirection(ray.direction);
            return shown.Router.Pick((from.x, from.y, from.z), (direction.x, direction.y, direction.z), 5000);
        }

        /// <summary>
        /// Where a free point may go: where the mouse puts it, or, when that is in something or too near it, the
        /// nearest spot back toward the camera with room for the wire, so the point stays on the side it is seen from.
        /// </summary>
        (float x, float y, float z) ClearTowardCamera(Vector3 mm, Ray ray)
        {
            var router = shown!.Router;
            var back = -robotAnchor.InverseTransformDirection(ray.direction).normalized;
            for (int i = 0; i < 240; i++)
            {
                var p = mm + back * (0.5f * i);
                if (router.Distance(p.x, p.y, p.z) >= WireRouter.Clearance + 0.6f) return (p.x, p.y, p.z);
            }
            return router.Clear((mm.x, mm.y, mm.z));
        }

        /// <summary>The spot on a wire's curve nearest the mouse on the screen (world), if the mouse is near it.</summary>
        bool NearestOnWire(int index, Vector2 mouse, out Vector3 world)
        {
            world = default;
            var path = shown != null && index >= 0 && index < shown.WirePaths.Count ? shown.WirePaths[index] : null;
            if (path == null) return false;
            var frame = shown!.Root.transform;
            float best = 12f;
            for (int k = 1; k < path.Length; k++)
            {
                Vector3 a = frame.TransformPoint(path[k - 1]), b = frame.TransformPoint(path[k]);
                Vector3 sa = view.WorldToScreenPoint(a), sb = view.WorldToScreenPoint(b);
                if (sa.z <= 0 || sb.z <= 0) continue;
                var ab = (Vector2)(sb - sa);
                float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)sa, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(mouse, (Vector2)sa + t * ab);
                if (d >= best) continue;
                best = d;
                world = Vector3.Lerp(a, b, t);
            }
            return best < 12f;
        }

        /// <summary>
        /// Where a new point at a spot (world) goes in a wire's list: after the points the wire passes before its
        /// place nearest the spot.
        /// </summary>
        int InsertIndex(int index, Vector3 world)
        {
            var wire = Design.Wires[index];
            var path = shown != null && index < shown.WirePaths.Count ? shown.WirePaths[index] : null;
            if (path == null || shown == null) return wire.Points.Count;
            var local = shown.Root.transform.InverseTransformPoint(world);
            int nearest = 0; // on the curve: the path is the pin, the curve, the other pin
            float best = float.MaxValue;
            for (int k = 1; k + 1 < path.Length; k++)
            {
                float d = (path[k] - local).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = k - 1;
            }
            int at = 0;
            foreach (var mark in shown.WireMarks[index]) if (mark.Index < nearest) at = Math.Max(at, mark.Point + 1);
            return Math.Min(at, wire.Points.Count);
        }
    }
}
