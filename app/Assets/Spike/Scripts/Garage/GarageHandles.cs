using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// A flat picture over the Studio's 3D view, painted with UI Toolkit's vector painter: filled polygons, lines
    /// and white reading boxes, all in panel pixels (docs/08 §3.1). The Studio fills it again every frame.
    /// </summary>
    sealed class HandleOverlay : VisualElement
    {
        struct Command
        {
            public int Start, Count;
            public Color Colour;
            public float Width; // 0 fills the polygon
            public bool Closed;
            public float Halo;  // a light edge this many pixels wide, so dark ink reads on a dark scene too
        }

        static readonly Color HaloColour = new Color(1f, 1f, 1f, 0.78f);

        /// <summary>The halo given to what is drawn next (0: none).</summary>
        public float Halo { get; set; }

        readonly List<Vector2> points = new List<Vector2>();
        readonly List<Command> commands = new List<Command>();
        readonly List<Label> labels = new List<Label>();
        int labelsUsed;

        public HandleOverlay()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("handle-overlay");
            generateVisualContent += Generate;
        }

        /// <summary>Starts a new picture; points are in panel coordinates.</summary>
        public void Begin()
        {
            points.Clear();
            commands.Clear();
            labelsUsed = 0;
        }

        public void End()
        {
            for (int i = labelsUsed; i < labels.Count; i++) labels[i].style.display = DisplayStyle.None;
            MarkDirtyRepaint();
        }

        public void Stroke(IReadOnlyList<Vector2> line, Color colour, float width, bool closed = false)
        {
            if (line.Count < 2) return;
            commands.Add(new Command { Start = points.Count, Count = line.Count, Colour = colour, Width = width, Closed = closed, Halo = Halo });
            for (int i = 0; i < line.Count; i++) points.Add(line[i]);
        }

        public void Fill(IReadOnlyList<Vector2> polygon, Color colour)
        {
            if (polygon.Count < 3) return;
            commands.Add(new Command { Start = points.Count, Count = polygon.Count, Colour = colour, Halo = Halo });
            for (int i = 0; i < polygon.Count; i++) points.Add(polygon[i]);
        }

        public void Line(Vector2 a, Vector2 b, Color colour, float width)
        {
            commands.Add(new Command { Start = points.Count, Count = 2, Colour = colour, Width = width, Halo = Halo });
            points.Add(a);
            points.Add(b);
        }

        /// <summary>A dashed line: dashes of <paramref name="on"/> pixels with gaps of <paramref name="off"/>, as drawn on the screen.</summary>
        public void Dashed(Vector2 a, Vector2 b, Color colour, float width, float on = 4, float off = 3)
        {
            float length = Vector2.Distance(a, b);
            if (length < 0.5f) return;
            var direction = (b - a) / length;
            for (float s = 0; s < length; s += on + off) Line(a + direction * s, a + direction * Mathf.Min(length, s + on), colour, width);
        }

        public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color colour)
        {
            commands.Add(new Command { Start = points.Count, Count = 3, Colour = colour, Halo = Halo });
            points.Add(a);
            points.Add(b);
            points.Add(c);
        }

        /// <summary>A square handle: a border of <paramref name="border"/> pixels round a filled middle.</summary>
        public void Square(Vector2 centre, float size, Color fill, Color edge, float border)
        {
            if (border > 0)
            {
                Rect(centre, size, edge, Halo);
                Rect(centre, size - 2 * border, fill, 0);
            }
            else Rect(centre, size, fill, Halo);
        }

        void Rect(Vector2 centre, float size, Color colour, float halo)
        {
            float h = size / 2;
            commands.Add(new Command { Start = points.Count, Count = 4, Colour = colour, Halo = halo });
            points.Add(centre + new Vector2(-h, -h));
            points.Add(centre + new Vector2(h, -h));
            points.Add(centre + new Vector2(h, h));
            points.Add(centre + new Vector2(-h, h));
        }

        /// <summary>An arrowhead at <paramref name="tip"/>, pointing along <paramref name="direction"/> (panel pixels).</summary>
        public void Arrowhead(Vector2 tip, Vector2 direction, Color colour, float length = 9, float width = 7)
        {
            if (direction.sqrMagnitude < 1e-6f) return;
            direction.Normalize();
            var side = new Vector2(-direction.y, direction.x) * (width / 2);
            var back = tip - direction * length;
            Triangle(tip, back + side, back - side, colour);
        }

        /// <summary>A reading in a small white box centred on a panel point, as Tinkercad shows sizes.</summary>
        public void Reading(Vector2 at, string text)
        {
            if (labelsUsed == labels.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("handle-reading");
                labels.Add(label);
                Add(label);
            }
            var reading = labels[labelsUsed++];
            reading.text = text;
            var local = at - Origin;
            reading.style.left = local.x;
            reading.style.top = local.y;
            reading.style.display = DisplayStyle.Flex;
        }

        Vector2 Origin => float.IsNaN(worldBound.x) ? Vector2.zero : worldBound.position;

        void Generate(MeshGenerationContext context)
        {
            var origin = Origin;
            var painter = context.painter2D;
            foreach (var c in commands)
            {
                painter.BeginPath();
                painter.MoveTo(points[c.Start] - origin);
                for (int i = 1; i < c.Count; i++) painter.LineTo(points[c.Start + i] - origin);
                if (c.Width <= 0 || c.Closed) painter.ClosePath();
                if (c.Halo > 0)
                {
                    // The same path stroked wider in white first: the halo shows round the ink.
                    painter.strokeColor = HaloColour;
                    painter.lineWidth = Mathf.Max(0, c.Width) + 2 * c.Halo;
                    painter.lineJoin = LineJoin.Round;
                    painter.lineCap = LineCap.Round;
                    painter.Stroke();
                }
                if (c.Width <= 0)
                {
                    painter.fillColor = c.Colour;
                    painter.Fill();
                }
                else
                {
                    painter.strokeColor = c.Colour;
                    painter.lineWidth = c.Width;
                    painter.lineJoin = LineJoin.Round;
                    painter.lineCap = LineCap.Butt;
                    painter.Stroke();
                }
            }
        }
    }

    /// <summary>
    /// The Body Studio's handles, as Tinkercad has them (rma_fullstack's "Tinkercad Parity" notes): the selected
    /// item's footprint and stem dashed; on a shape, white squares at the corners of its base and dark squares
    /// at the middles of its edges that size it, and a white square on top for its height; a cone above it that
    /// lifts it; three small curled arrows that turn it about the robot's axes, placed where the camera sees them
    /// clear of the shape. A part keeps its real size, so it has the cone and the curls only. While a handle is
    /// dragged the sizes show as dimension lines, a turn as a protractor.
    /// </summary>
    public sealed partial class GarageSpike
    {
        // Panel pixels, measured on Tinkercad (corners 13 px with a 2.5 px border, edges 8 px, the cone 50 px up)
        const float CornerPx = 13, EdgePx = 8, HandleBorderPx = 2.5f;
        const float ConeWidthPx = 12, ConeHeightPx = 15, LiftGapPx = 50;
        const float CurlRadiusPx = 22, CurlSweepDegrees = 38, CurlWidthPx = 2.6f;
        const float DimensionOutPx = 40, ExtensionPastPx = 12, ReadingOutPx = 24;
        const float MinShapeSize = 0.5f;

        static readonly Color HandleInk = new Color32(0x33, 0x33, 0x33, 0xFF);
        static readonly Color HandleHot = new Color32(0xE5, 0x39, 0x35, 0xFF);
        static readonly Color SweepInk = new Color32(0x36, 0xC0, 0xF3, 0xFF);
        static readonly Color SweepFill = new Color(0.21f, 0.75f, 0.95f, 0.26f);
        static readonly int[] Signs = { -1, 1 };

        /// <summary>A handle's place on the screen this frame, for the mouse to find it.</summary>
        struct HandleSpot
        {
            public Grip Grip;
            public int Axis, Sign;
            public Vector2 At;       // panel point: a square, the cone, the middle of a curl
            public float Reach;      // how near the mouse must come (panel pixels)
            public int Start, Count; // a curl's line in spotLines, when Count > 1
        }

        HandleOverlay? handleOverlay;
        readonly List<HandleSpot> spots = new List<HandleSpot>();
        readonly List<Vector2> spotLines = new List<Vector2>();
        readonly List<Vector2> scratchPoints = new List<Vector2>();
        Plane dragPlane;
        float dragAngle, dragLow0, dragRadius;

        void BuildHandleOverlay()
        {
            handleOverlay = new HandleOverlay();
            studioViewport.Add(handleOverlay);
        }

        /// <summary>Draws the handles of the one selected item (none while drawing or carrying).</summary>
        void UpdateGizmo()
        {
            if (handleOverlay == null) return;
            handleOverlay.Begin();
            spots.Clear();
            spotLines.Clear();
            var target = selection.Count == 1 && Exists(selection[0]) ? selection[0] : (Pick?)null;
            if (target != null && !drawing && carrying == null && root.panel != null && mode == EditMode.Body) DrawHandles(target.Value);
            handleOverlay.End();
        }

        void DrawHandles(Pick p)
        {
            var o = handleOverlay!;
            o.Halo = 1.2f;
            var (min, max) = BoundsOf(p);
            var shape = SizedShape;
            var turn = Quaternion.identity;
            Vector3 centre = (min + max) / 2, half = (max - min) / 2;
            if (shape != null)
            {
                turn = Quaternion.Euler(shape.RotX, shape.RotY, shape.RotZ);
                centre = new Vector3(shape.X, shape.Y, shape.Z);
                half = new Vector3(shape.SizeX, shape.SizeY, shape.SizeZ) / 2;
            }
            Vector3 Box(float x, float y, float z) => WorldOf(centre + turn * new Vector3(x * half.x, y * half.y, z * half.z));
            var lit = drag.grip != Grip.None ? drag : hot;
            bool Lit(Grip grip, int axis, int sign) => lit.grip == grip && lit.axis == axis && lit.sign == sign;

            // The footprint and the stem from its middle to the top, dashed.
            var foot = new[] { Box(-1, -1, -1), Box(1, -1, -1), Box(1, -1, 1), Box(-1, -1, 1) };
            for (int i = 0; i < 4; i++) DashedWorld(foot[i], foot[(i + 1) % 4]);
            var top = Box(0, 1, 0);
            DashedWorld(Box(0, -1, 0), top);

            if (drag.grip == Grip.Turn)
            {
                o.Halo = 0;
                Protractor();
                return; // Tinkercad shows only the protractor while a shape turns
            }

            if (shape != null && drag.grip != Grip.Lift)
            {
                for (int k = 0; k < 4; k++)
                {
                    int sx = (k & 1) != 0 ? 1 : -1, sz = (k & 2) != 0 ? 1 : -1;
                    SquareHandle(Box(sx, -1, sz), CornerPx, true, Grip.Corner, 0, k, Lit(Grip.Corner, 0, k));
                }
                foreach (int s in Signs)
                {
                    SquareHandle(Box(s, -1, 0), EdgePx, false, Grip.Edge, 0, s, Lit(Grip.Edge, 0, s));
                    SquareHandle(Box(0, -1, s), EdgePx, false, Grip.Edge, 2, s, Lit(Grip.Edge, 2, s));
                }
                SquareHandle(top, CornerPx, true, Grip.Top, 1, 1, Lit(Grip.Top, 1, 1));
                if (lit.grip == Grip.Corner || lit.grip == Grip.Edge || lit.grip == Grip.Top) SizeReadings(Box, turn, centre, half, lit);
            }

            // The cone, 50 pixels above the top: it lifts the item off the workplane or lowers it.
            float perPx = WorldPerPixel(top);
            var up = robotAnchor.up;
            var coneBase = top + up * (LiftGapPx - ConeHeightPx / 2) * perPx;
            if (ToPanel(coneBase, out var b) && ToPanel(coneBase + up * ConeHeightPx * perPx, out var a))
            {
                var axis = a - b;
                if (axis.magnitude < 5) axis = Vector2.down * 5; // seen from above: still a small cone pointing up the screen
                var direction = axis.normalized;
                var side = new Vector2(-direction.y, direction.x) * (ConeWidthPx / 2);
                var colour = Lit(Grip.Lift, 1, 1) ? HandleHot : HandleInk;
                o.Triangle(b + axis, b + side, b - side, colour);
                scratchPoints.Clear();
                for (int i = 0; i <= 12; i++)
                {
                    float t = Mathf.PI * i / 12; // the near half of the base's ellipse
                    scratchPoints.Add(b + side * Mathf.Cos(t) - direction * (ConeWidthPx * 0.22f) * Mathf.Sin(t));
                }
                o.Fill(scratchPoints, colour);
                spots.Add(new HandleSpot { Grip = Grip.Lift, Axis = 1, Sign = 1, At = b + axis * 0.4f, Reach = 13 });
                if (drag.grip == Grip.Lift) o.Reading(b + axis * 0.5f + side.normalized * 34, min.y.ToString("0.##"));
            }
            if (drag.grip == Grip.Lift) return;

            // The curls: turning about x and z at the middles of the two top edges away from the camera, turning
            // about the vertical flat under the bottom edge nearest to it.
            var eye = robotAnchor.InverseTransformPoint(view.transform.position) / StudioMm;
            var c = (min + max) / 2;
            float zSide = eye.z > c.z ? -1 : 1, xSide = eye.x > c.x ? -1 : 1;
            Curl(0, new Vector3(c.x, max.y, zSide < 0 ? min.z : max.z), new Vector3(0, 1, zSide), Lit(Grip.Turn, 0, 1));
            Curl(2, new Vector3(xSide < 0 ? min.x : max.x, max.y, c.z), new Vector3(xSide, 1, 0), Lit(Grip.Turn, 2, 1));
            var edges = new[]
            {
                (new Vector3(min.x, min.y, c.z), Vector3.left), (new Vector3(max.x, min.y, c.z), Vector3.right),
                (new Vector3(c.x, min.y, min.z), Vector3.back), (new Vector3(c.x, min.y, max.z), Vector3.forward),
            };
            var nearest = edges[0];
            foreach (var edge in edges)
                if ((edge.Item1 - eye).sqrMagnitude < (nearest.Item1 - eye).sqrMagnitude) nearest = edge;
            Curl(1, nearest.Item1, nearest.Item2, Lit(Grip.Turn, 1, 1));
        }

        void SquareHandle(Vector3 world, float size, bool white, Grip grip, int axis, int sign, bool lit)
        {
            if (!ToPanel(world, out var at)) return;
            handleOverlay!.Square(at, size, lit ? HandleHot : white ? Color.white : HandleInk, HandleInk, white ? HandleBorderPx : 0);
            spots.Add(new HandleSpot { Grip = grip, Axis = axis, Sign = sign, At = at, Reach = size / 2 + 5 });
        }

        void DashedWorld(Vector3 a, Vector3 b)
        {
            if (ToPanel(a, out var pa) && ToPanel(b, out var pb)) handleOverlay!.Dashed(pa, pb, HandleInk, 1.25f);
        }

        /// <summary>
        /// A short curled arrow round an edge of the item's box, in the plane it turns in: its middle points away from
        /// the box (<paramref name="outward"/>, chassis frame) and an arrowhead ends it on each side.
        /// </summary>
        void Curl(int axis, Vector3 edgeMm, Vector3 outward, bool lit)
        {
            var at = WorldOf(edgeMm);
            float r = CurlRadiusPx * WorldPerPixel(at);
            var u = robotAnchor.TransformDirection(outward).normalized;
            var v = Vector3.Cross(AxisWorld(axis), u).normalized;
            int start = spotLines.Count;
            const int steps = 12;
            for (int i = 0; i <= steps; i++)
            {
                float t = (-CurlSweepDegrees + 2 * CurlSweepDegrees * i / steps) * Mathf.Deg2Rad;
                if (!ToPanel(at + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * r, out var q))
                {
                    spotLines.RemoveRange(start, spotLines.Count - start);
                    return;
                }
                spotLines.Add(q);
            }
            int count = spotLines.Count - start;
            var colour = lit ? HandleHot : HandleInk;
            var o = handleOverlay!;
            scratchPoints.Clear();
            for (int i = 0; i < count; i++) scratchPoints.Add(spotLines[start + i]);
            o.Stroke(scratchPoints, colour, CurlWidthPx);
            Vector2 first = spotLines[start], second = spotLines[start + 1], last = spotLines[start + count - 1], before = spotLines[start + count - 2];
            o.Arrowhead(first + (first - second).normalized * 6, first - second, colour, 8, 8);
            o.Arrowhead(last + (last - before).normalized * 6, last - before, colour, 8, 8);
            spots.Add(new HandleSpot { Grip = Grip.Turn, Axis = axis, Sign = 1, At = spotLines[start + count / 2], Reach = 9, Start = start, Count = count });
        }

        /// <summary>The protractor of a turn: a ring in the plane of the turn, ticks every 15°, the angle swept so far and its reading.</summary>
        void Protractor()
        {
            var o = handleOverlay!;
            var n = dragDirection;
            var toEye = (view.transform.position - dragPivot).normalized;
            if (dragFrom.sqrMagnitude < 1e-10f || Mathf.Abs(Vector3.Dot(toEye, n)) < 0.12f) return; // seen edge-on
            var u = Vector3.ProjectOnPlane(dragFrom, n).normalized;
            var v = Vector3.Cross(n, u);
            Vector3 Around(float degrees, float radius) =>
                dragPivot + (u * Mathf.Cos(degrees * Mathf.Deg2Rad) + v * Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

            scratchPoints.Clear();
            if (ToPanel(dragPivot, out var middle)) scratchPoints.Add(middle);
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(dragAngle) / 4));
            for (int i = 0; i <= steps; i++)
                if (ToPanel(Around(dragAngle * i / steps, dragRadius), out var q)) scratchPoints.Add(q);
            if (Mathf.Abs(dragAngle) > 0.5f) o.Fill(scratchPoints, SweepFill);

            scratchPoints.Clear();
            for (int i = 0; i < 96; i++)
                if (ToPanel(Around(i * 3.75f, dragRadius), out var q)) scratchPoints.Add(q);
            o.Stroke(scratchPoints, SweepInk, 1.5f, closed: true);
            for (int k = 0; k < 24; k++)
            {
                float inner = k % 6 == 0 ? 0.84f : k % 3 == 0 ? 0.89f : 0.93f;
                if (ToPanel(Around(k * 15, dragRadius * inner), out var from) && ToPanel(Around(k * 15, dragRadius), out var to))
                    o.Line(from, to, SweepInk, k % 3 == 0 ? 1.6f : 1.1f);
            }
            if (ToPanel(dragPivot, out var pivot) && ToPanel(Around(dragAngle, dragRadius), out var end))
            {
                o.Line(pivot, end, SweepInk, 1.6f);
                if (ToPanel(Around(0, dragRadius), out var zero)) o.Line(pivot, zero, SweepInk, 1f);
                var outward = (end - pivot).normalized;
                o.Reading(end + outward * ReadingOutPx, $"{dragAngle:0.#}°");
            }
        }

        /// <summary>The dimension lines of the handle under the mouse or dragged: two for a corner, one for an edge or the top.</summary>
        void SizeReadings(System.Func<float, float, float, Vector3> box, Quaternion turn, Vector3 centre, Vector3 half, (Grip grip, int axis, int sign) lit)
        {
            var eye = robotAnchor.InverseTransformPoint(view.transform.position) / StudioMm;
            var local = Quaternion.Inverse(turn) * (eye - centre);
            int farX = local.x > 0 ? -1 : 1, farZ = local.z > 0 ? -1 : 1; // the sides away from the camera
            switch (lit.grip)
            {
                case Grip.Corner:
                {
                    int sx = (lit.sign & 1) != 0 ? 1 : -1, sz = (lit.sign & 2) != 0 ? 1 : -1;
                    Dimension(box(-1, -1, sz), box(1, -1, sz), turn * new Vector3(0, 0, sz), half.x * 2);
                    Dimension(box(sx, -1, -1), box(sx, -1, 1), turn * new Vector3(sx, 0, 0), half.z * 2);
                    break;
                }
                case Grip.Edge when lit.axis == 0:
                    Dimension(box(-1, -1, farZ), box(1, -1, farZ), turn * new Vector3(0, 0, farZ), half.x * 2);
                    break;
                case Grip.Edge:
                    Dimension(box(farX, -1, -1), box(farX, -1, 1), turn * new Vector3(farX, 0, 0), half.z * 2);
                    break;
                case Grip.Top:
                {
                    // beside the vertical edge furthest right on the screen
                    int bestX = 1, bestZ = 1;
                    float right = float.MinValue;
                    foreach (int sx in Signs)
                        foreach (int sz in Signs)
                            if (ToPanel(box(sx, -1, sz), out var q) && q.x > right)
                            {
                                right = q.x;
                                (bestX, bestZ) = (sx, sz);
                            }
                    var outward = (turn * new Vector3(bestX, 0, bestZ)).normalized;
                    Dimension(box(bestX, -1, bestZ), box(bestX, 1, bestZ), outward, half.y * 2);
                    break;
                }
            }
        }

        /// <summary>
        /// One Tinkercad dimension: a line 40 pixels out from an edge of the box (a to b, world) with an arrowhead at
        /// each end, an extension line from each end of the edge to just past it, and the size in a white box.
        /// </summary>
        void Dimension(Vector3 a, Vector3 b, Vector3 outwardMm, float sizeMm)
        {
            var o = handleOverlay!;
            var outward = robotAnchor.TransformDirection(outwardMm).normalized;
            float perPx = WorldPerPixel((a + b) / 2);
            if (!ToPanel(a, out var pa) || !ToPanel(b, out var pb) ||
                !ToPanel(a + outward * DimensionOutPx * perPx, out var la) || !ToPanel(b + outward * DimensionOutPx * perPx, out var lb) ||
                !ToPanel(a + outward * (DimensionOutPx + ExtensionPastPx) * perPx, out var ea) || !ToPanel(b + outward * (DimensionOutPx + ExtensionPastPx) * perPx, out var eb) ||
                !ToPanel((a + b) / 2 + outward * (DimensionOutPx + ReadingOutPx) * perPx, out var reading))
                return;
            o.Line(pa, ea, HandleInk, 1f);
            o.Line(pb, eb, HandleInk, 1f);
            if (Vector2.Distance(la, lb) > 20)
            {
                var along = (lb - la).normalized;
                o.Line(la + along * 8, lb - along * 8, HandleInk, 1.2f);
                o.Arrowhead(la, -along, HandleInk, 10, 7);
                o.Arrowhead(lb, along, HandleInk, 10, 7);
            }
            o.Reading(reading, sizeMm.ToString("0.##"));
        }

        // ------------------------------------------------------------------ handles: the mouse

        /// <summary>The handle under the mouse, as last drawn: squares and the cone by distance, curls along their line.</summary>
        (Grip grip, int axis, int sign) PickHandle(Vector2 mouse)
        {
            if (spots.Count == 0 || root.panel == null) return default;
            var m = MousePanel(mouse);
            (Grip, int, int) best = default;
            float bestDistance = float.MaxValue;
            foreach (var s in spots)
            {
                float d;
                if (s.Count > 1)
                {
                    d = float.MaxValue;
                    for (int i = 1; i < s.Count; i++) d = Mathf.Min(d, DistanceToSegment(m, spotLines[s.Start + i - 1], spotLines[s.Start + i]));
                }
                else d = Vector2.Distance(m, s.At);
                if (d <= s.Reach && d < bestDistance)
                {
                    bestDistance = d;
                    best = (s.Grip, s.Axis, s.Sign);
                }
            }
            return best;
        }

        /// <summary>Where a handle drawn this frame is on the screen (pixels from the bottom left), for the benchmark's mouse.</summary>
        Vector2? HandleScreen(Grip grip, int axis, int sign)
        {
            foreach (var s in spots)
                if (s.Grip == grip && s.Axis == axis && s.Sign == sign) return PanelToScreen(s.At);
            return null;
        }

        int SizeHandleCount()
        {
            int n = 0;
            foreach (var s in spots) if (s.Grip is Grip.Corner or Grip.Edge or Grip.Top) n++;
            return n;
        }

        void StartHandleDrag(Pick p, (Grip grip, int axis, int sign) handle, Ray ray)
        {
            switch (handle.grip)
            {
                case Grip.Corner:
                case Grip.Edge:
                {
                    var f = SizedShape!;
                    var turn = Quaternion.Euler(f.RotX, f.RotY, f.RotZ);
                    var floor = WorldOf(new Vector3(f.X, f.Y, f.Z) + turn * new Vector3(0, -f.SizeY / 2, 0));
                    dragPlane = new Plane(robotAnchor.rotation * turn * Vector3.up, floor);
                    dragFrom = dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : floor;
                    break;
                }
                case Grip.Top:
                {
                    var f = SizedShape!;
                    dragDirection = robotAnchor.rotation * Quaternion.Euler(f.RotX, f.RotY, f.RotZ) * Vector3.up;
                    dragT0 = RayLineParameter(ray, dragPivot, dragDirection);
                    break;
                }
                case Grip.Lift:
                    dragDirection = robotAnchor.up;
                    dragT0 = RayLineParameter(ray, dragPivot, dragDirection);
                    dragLow0 = BoundsOf(p).min.y;
                    break;
                case Grip.Turn:
                {
                    dragDirection = AxisWorld(handle.axis);
                    dragFrom = new Plane(dragDirection, dragPivot).Raycast(ray, out float enter) ? ray.GetPoint(enter) - dragPivot : Vector3.zero;
                    dragAngle = 0;
                    var (min, max) = BoundsOf(p);
                    dragRadius = Mathf.Max((max - min).magnitude / 2 * StudioMm * 1.08f, 64 * WorldPerPixel(dragPivot));
                    break;
                }
            }
        }

        /// <summary>A handle's drag this frame; false for the grips the Studio drags itself (the item, by its body).</summary>
        bool UpdateHandleDrag(Pick p, Ray ray, Vector2 mouse, RobotDesign before)
        {
            bool fine = input.Ctrl;
            float step = fine ? 0.1f : studioSnap;
            switch (drag.grip)
            {
                case Grip.Corner:
                case Grip.Edge:
                {
                    var f = SizedShape;
                    var s = f == null ? null : before.Body.Feature(f.Id);
                    if (f == null || s == null || !dragPlane.Raycast(ray, out float enter) || enter > 5f) return true;
                    var turn = Quaternion.Euler(s.RotX, s.RotY, s.RotZ);
                    var moved = Quaternion.Inverse(turn) * (robotAnchor.InverseTransformVector(ray.GetPoint(enter) - dragFrom) / StudioMm);
                    int sx, sz;
                    if (drag.grip == Grip.Corner) (sx, sz) = ((drag.sign & 1) != 0 ? 1 : -1, (drag.sign & 2) != 0 ? 1 : -1);
                    else (sx, sz) = (drag.axis == 0 ? drag.sign : 0, drag.axis == 2 ? drag.sign : 0);
                    Resize(f, s, new Vector3(sx * moved.x, 0, sz * moved.z), sx, 0, sz, step);
                    return true;
                }
                case Grip.Top:
                {
                    var f = SizedShape;
                    var s = f == null ? null : before.Body.Feature(f.Id);
                    float t = RayLineParameter(ray, dragPivot, dragDirection);
                    if (f == null || s == null || float.IsNaN(t)) return true;
                    Resize(f, s, new Vector3(0, (t - dragT0) / StudioMm, 0), 0, 1, 0, step);
                    return true;
                }
                case Grip.Lift:
                {
                    float t = RayLineParameter(ray, dragPivot, dragDirection);
                    if (float.IsNaN(t)) return true;
                    float low = Snap(dragLow0 + (t - dragT0) / StudioMm, step);
                    MoveItem(p, new Vector3(0, low - dragLow0, 0), before);
                    return true;
                }
                case Grip.Turn:
                {
                    float angle;
                    var viewDirection = (dragPivot - view.transform.position).normalized;
                    if (dragFrom.sqrMagnitude > 1e-10f && Mathf.Abs(Vector3.Dot(viewDirection, dragDirection)) > 0.12f &&
                        new Plane(dragDirection, dragPivot).Raycast(ray, out float enter))
                        angle = Vector3.SignedAngle(dragFrom, ray.GetPoint(enter) - dragPivot, dragDirection);
                    else
                        angle = (mouse.x - dragMouse0.x) * 0.5f; // the plane is seen edge-on: turn with the mouse's sideways movement
                    dragAngle = Snap(angle, fine ? 1f : input.Shift ? 45f : 15f);
                    TurnItem(p, Quaternion.AngleAxis(dragAngle, Unit(drag.axis)), dragPivotMm, before);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sizes a shape from a handle: each dragged side grows by how far its handle moved outward (<paramref name="grow"/>,
        /// the shape's own frame) and the opposite side stays; Alt keeps the middle instead; Shift, or "Keep proportions",
        /// scales all three sizes together. The base stays where it is whenever the height changes (Alt too).
        /// </summary>
        void Resize(BodyFeature f, BodyFeature s, Vector3 grow, int sx, int sy, int sz, float step)
        {
            bool centred = input.Alt, uniform = input.Shift || keepProportions;
            var start = new Vector3(s.SizeX, s.SizeY, s.SizeZ);
            var size = start;
            float k = centred ? 2 : 1; // from the middle, both sides move: twice the growth
            if (sx != 0) size.x = Mathf.Max(MinShapeSize, Snap(start.x + k * grow.x, step));
            if (sy != 0) size.y = Mathf.Max(MinShapeSize, Snap(start.y + grow.y, step));
            if (sz != 0) size.z = Mathf.Max(MinShapeSize, Snap(start.z + k * grow.z, step));
            if (uniform)
            {
                float ratio = 1, most = -1;
                for (int a = 0; a < 3; a++)
                {
                    if ((a == 0 ? sx : a == 1 ? sy : sz) == 0) continue;
                    float r = size[a] / Mathf.Max(0.01f, start[a]);
                    if (Mathf.Abs(r - 1) > most)
                    {
                        most = Mathf.Abs(r - 1);
                        ratio = r;
                    }
                }
                size = Vector3.Max(start * ratio, Vector3.one * MinShapeSize);
            }
            (f.SizeX, f.SizeY, f.SizeZ) = (size.x, size.y, size.z);
            var shift = new Vector3(centred ? 0 : sx * (size.x - start.x) / 2, (size.y - start.y) / 2, centred ? 0 : sz * (size.z - start.z) / 2);
            var moved = Quaternion.Euler(s.RotX, s.RotY, s.RotZ) * shift;
            (f.X, f.Y, f.Z) = (s.X + moved.x, s.Y + moved.y, s.Z + moved.z);
        }

        // ------------------------------------------------------------------ screen and panel

        bool ToPanel(Vector3 world, out Vector2 panel)
        {
            panel = default;
            var s = view.WorldToScreenPoint(world);
            if (s.z < 0.01f || root.panel == null) return false;
            panel = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(s.x, Screen.height - s.y));
            return true;
        }

        Vector2 MousePanel(Vector2 mouse) => RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));

        Vector2 PanelToScreen(Vector2 panel)
        {
            float scale = PanelScale();
            return new Vector2(panel.x * scale, Screen.height - panel.y * scale);
        }

        /// <summary>Screen pixels per panel pixel.</summary>
        float PanelScale()
        {
            float height = root.panel?.visualTree.layout.height ?? float.NaN;
            return float.IsNaN(height) || height < 1 ? 1 : Screen.height / height;
        }

        /// <summary>How many metres one panel pixel spans at a point of the scene.</summary>
        float WorldPerPixel(Vector3 world)
        {
            float depth = Mathf.Max(0.01f, Vector3.Dot(world - view.transform.position, view.transform.forward));
            return 2 * depth * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height * PanelScale();
        }
    }
}
