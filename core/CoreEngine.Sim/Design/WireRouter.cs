using System;
using System.Collections.Generic;
using System.Numerics;

namespace CoreEngine.Sim.Design
{
    /// <summary>A wire laid by <see cref="WireRouter.Path"/>: its points about 2 mm apart, and where it passes the player's points.</summary>
    public sealed class WirePath
    {
        public WirePath(List<(float x, float y, float z)> points, List<PathAnchor> anchors)
        {
            Points = points;
            Anchors = anchors;
        }

        /// <summary>From the first pin to the second (chassis frame, mm).</summary>
        public List<(float x, float y, float z)> Points { get; }

        /// <summary>The player's points the wire passes, in order.</summary>
        public List<PathAnchor> Anchors { get; }
    }

    /// <summary>Where a wire passes one of the player's points (chassis frame, mm).</summary>
    public readonly struct PathAnchor
    {
        public PathAnchor(int point, (float x, float y, float z) at, (float x, float y, float z) along, (float x, float y, float z) normal, bool glued, int index)
        {
            Point = point;
            At = at;
            Along = along;
            Normal = normal;
            Glued = glued;
            Index = index;
        }

        /// <summary>Its place in the wire's list of points.</summary>
        public int Point { get; }

        /// <summary>The wire's middle there: a free point pushed out of anything it was in, or just off a glued surface.</summary>
        public (float x, float y, float z) At { get; }

        /// <summary>The way the wire runs through it, from the first pin toward the second.</summary>
        public (float x, float y, float z) Along { get; }

        /// <summary>The outward normal of the surface a glued point is on.</summary>
        public (float x, float y, float z) Normal { get; }

        public bool Glued { get; }

        /// <summary>The index of the path's point there.</summary>
        public int Index { get; }
    }

    /// <summary>What a ray meets first on the robot (chassis frame, mm): a part or a body shape.</summary>
    public readonly struct SurfaceHit
    {
        public SurfaceHit((float x, float y, float z) point, (float x, float y, float z) normal, string owner, bool wheel)
        {
            Point = point;
            Normal = normal;
            Owner = owner;
            Wheel = wheel;
        }

        public (float x, float y, float z) Point { get; }

        /// <summary>The surface's outward normal there (unit length).</summary>
        public (float x, float y, float z) Normal { get; }

        /// <summary>The part's id, or the body shape's.</summary>
        public string Owner { get; }

        /// <summary>A motor's wheel: it turns, so nothing is glued to it.</summary>
        public bool Wheel { get; }
    }

    /// <summary>
    /// Lays jumper wires the way a builder does (docs/03 §6): out of a header or a terminal, then over the parts,
    /// round the edges of the plates or through a hole wide enough, never through a part or solid material.
    /// The robot is a distance field: how far a point is from the nearest part block (<see cref="PartDef.Solids"/>,
    /// a motor's wheel), body shape (a hole takes material only from the solids of its group, as Manifold cuts
    /// them) or the floor. A* crosses a grid of 5 mm cells, each move checked against the field; a step costs more
    /// the less room it has under 4 mm, so a route keeps room for round bends where it can and still takes a
    /// narrow way, such as a hole, rather than a long way round. The path is pulled straight where it can be
    /// without coming closer to anything than it was, short jogs are taken out, every corner becomes an arc of up
    /// to 16 mm radius (two bends close together share the side between them, so that neither is a kink; smaller
    /// only where something is in the way), and the wire gets some slack upward: a real jumper cannot fold, it
    /// bends and arches. The player may give a wire points (<see cref="WirePoint"/>): it passes through each, and
    /// lies flat on the surface under each glued one. Everything is in millimetres in the chassis frame.
    /// </summary>
    public sealed class WireRouter
    {
        /// <summary>How close a wire's centre line may come to anything: a jumper's radius and a little air.</summary>
        public const float Clearance = 1.4f;

        /// <summary>
        /// How close the finished wire's bends and slack may come: its radius and a hair. A wire may rest on a part
        /// as a real one does, never go into it; the path it follows keeps <see cref="Clearance"/>.
        /// </summary>
        const float Touch = 0.9f;

        /// <summary>The room a route keeps where it can, so its bends have space to be round.</summary>
        const float Comfort = 4f;

        /// <summary>How much dearer a step is with no room to spare than with <see cref="Comfort"/>.</summary>
        const float TightCost = 3f;

        /// <summary>The widest bend: a jumper's wire bends smoothly, with a radius of a centimetre or two.</summary>
        const float MaxBendRadius = 16f;

        /// <summary>Points along a finished wire, every this many millimetres, so its slack is a smooth bow.</summary>
        const float Spacing = 2f;

        /// <summary>A side of the path shorter than this between two bends is a jog, taken out where it can be.</summary>
        const float Jog = 6f;

        /// <summary>How long a glued stretch lies flat on its surface, under the glue (mm).</summary>
        public const float GlueLength = 8f;

        /// <summary>A glued wire's middle above its surface: its radius and a hair (mm).</summary>
        const float GlueHeight = 0.85f;

        /// <summary>How steeply a wire rises off its glue: tan 15°.</summary>
        const float GlueRise = 0.27f;

        /// <summary>How far out of its glue a wire's path starts (mm).</summary>
        const float GlueLead = 6f;

        /// <summary>How far a wire runs straight on through one of the player's points, at most (mm).</summary>
        const float PointLead = 4f;

        const float Far = 24f;        // the field is only worked out this far; beyond it, "far" is enough
        const float Margin = 30f;     // room round the robot for wires going round it
        const float Headroom = 45f;   // and above it, for wires arching over the parts
        const int MaxCells = 400_000;
        const int MaxExpansions = 80_000;
        const float Greed = 1.25f;    // A*'s weight on the distance still to go: a little greedy, much faster
        const float BucketSize = 20f; // the field looks only at what is listed in the point's 20 mm bucket

        static readonly int[] Dx = new int[26], Dy = new int[26], Dz = new int[26];
        static readonly float[] Steps = new float[26];

        static WireRouter()
        {
            int k = 0;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        Dx[k] = x;
                        Dy[k] = y;
                        Dz[k] = z;
                        Steps[k] = MathF.Sqrt(x * x + y * y + z * z);
                        k++;
                    }
        }

        readonly List<Node> top = new List<Node>();
        readonly float floor;
        readonly float size, x0, y0, z0;
        readonly int nx, ny, nz;
        readonly float[] field;
        readonly int bx, by, bz;
        readonly Node[][] buckets; // by bucket: every shape whose box comes within Far of it

        // A*'s scratch: cost so far, the cell before, and which search last opened or closed each cell.
        float[]? g;
        int[]? came, opened, closed;
        int search;
        readonly Heap heap = new Heap();

        /// <summary>Changes whenever anything the routes depend on changes: the same key and ends give the same route.</summary>
        public string Key { get; }

        /// <summary>How many times the distance field was worked out (for measuring).</summary>
        public int Evaluations { get; private set; }

        /// <summary>How many cells A* has taken from its queue (for measuring).</summary>
        public int Expansions { get; private set; }

        public WireRouter(RobotDesign design)
        {
            var hash = new Hasher();
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                hash.Add(part.Part).Add(part.X).Add(part.Y).Add(part.Z).Add(part.Rotation).Add(part.RotX).Add(part.RotZ);
                var turn = part.Turn;
                foreach (var block in def.Solids) top.Add(Node.Leaf(Shape.Block(turn, part, block)));
                if (def.Kind == PartKind.Motor) top.Add(Node.Leaf(Shape.Wheel(turn, part)));
            }
            var body = design.Body;
            foreach (var item in body.Members(null))
            {
                if (item.Hole) continue; // a hole on its own cuts nothing (Tinkercad's rule)
                var node = BodyNode(body, item, hash);
                if (node != null) top.Add(node);
            }
            floor = DesignGeometry.LowestPoint(design);
            hash.Add(floor);
            Key = hash.ToString();

            var bounds = Box3.Empty;
            foreach (var node in top) bounds = bounds.Union(node.Bounds);
            if (bounds.IsEmpty) bounds = new Box3(-50, floor, -50, 50, floor + 50, 50);
            float minX = bounds.MinX - Margin, minY = Math.Max(floor, bounds.MinY - Margin), minZ = bounds.MinZ - Margin;
            float maxX = bounds.MaxX + Margin, maxY = bounds.MaxY + Headroom, maxZ = bounds.MaxZ + Margin;
            float cell = 5f;
            while ((long)Count(maxX - minX, cell) * Count(maxY - minY, cell) * Count(maxZ - minZ, cell) > MaxCells) cell *= 1.25f;
            size = cell;
            nx = Count(maxX - minX, cell);
            ny = Count(maxY - minY, cell);
            nz = Count(maxZ - minZ, cell);
            x0 = minX;
            y0 = minY;
            z0 = minZ;
            field = new float[nx * ny * nz];
            for (int i = 0; i < field.Length; i++) field[i] = float.NaN;

            bx = Count(nx * size, BucketSize);
            by = Count(ny * size, BucketSize);
            bz = Count(nz * size, BucketSize);
            var lists = new List<Node>?[bx * by * bz];
            foreach (var node in top)
            {
                var b = node.Bounds;
                int ax0 = BucketOf(b.MinX - Far, x0, bx), ax1 = BucketOf(b.MaxX + Far, x0, bx);
                int ay0 = BucketOf(b.MinY - Far, y0, by), ay1 = BucketOf(b.MaxY + Far, y0, by);
                int az0 = BucketOf(b.MinZ - Far, z0, bz), az1 = BucketOf(b.MaxZ + Far, z0, bz);
                for (int x = ax0; x <= ax1; x++)
                    for (int y = ay0; y <= ay1; y++)
                        for (int z = az0; z <= az1; z++)
                            (lists[(x * by + y) * bz + z] ??= new List<Node>()).Add(node);
            }
            buckets = new Node[lists.Length][];
            for (int i = 0; i < lists.Length; i++) buckets[i] = lists[i]?.ToArray() ?? Array.Empty<Node>();
        }

        static int BucketOf(float v, float origin, int count) => Math.Min(count - 1, Math.Max(0, (int)Math.Floor((v - origin) / BucketSize)));

        static int Count(float extent, float cell) => Math.Max(1, (int)Math.Ceiling(extent / cell));

        /// <summary>A body shape, or a group with its solids and holes; null for a group with no solid in it.</summary>
        static Node? BodyNode(BodyDesign body, BodyFeature item, Hasher hash)
        {
            hash.Add((int)item.Kind).Add(item.Hole ? 1 : 0).Add(item.X).Add(item.Y).Add(item.Z).Add(item.RotX).Add(item.RotY).Add(item.RotZ)
                .Add(item.SizeX).Add(item.SizeY).Add(item.SizeZ).Add(item.Detail).Add(item.Pitch).Add(item.HoleSize);
            foreach (float v in item.Outline) hash.Add(v);
            if (item.Kind != FeatureKind.Group) return Node.Leaf(Shape.Feature(item));
            var solids = new List<Node>();
            var holes = new List<Node>();
            foreach (var member in body.Members(item))
            {
                var node = BodyNode(body, member, hash);
                if (node != null) (member.Hole ? holes : solids).Add(node);
            }
            return solids.Count == 0 ? null : Node.Group(solids, holes);
        }

        // ------------------------------------------------------------------ the distance field

        /// <summary>
        /// How far a point is from the nearest solid, up to <see cref="Far"/>; below <see cref="Clearance"/> only
        /// that it is too close counts. Never more than the true distance, and it changes by no more than a point
        /// moves, so a segment whose ends are each farther than half its length from everything is clear.
        /// </summary>
        public float Distance(float x, float y, float z)
        {
            Evaluations++;
            float d = Math.Min(Far, y - floor);
            if (d < Clearance) return d;
            var nodes = buckets[(BucketOf(x, x0, bx) * by + BucketOf(y, y0, by)) * bz + BucketOf(z, z0, bz)];
            foreach (var node in nodes)
            {
                if (node.Bounds.DistanceSquared(x, y, z) >= d * d) continue;
                d = Math.Min(d, NodeDistance(node, x, y, z, d));
                if (d < Clearance) return d;
            }
            return d;
        }

        float Distance(Vector3 p) => Distance(p.X, p.Y, p.Z);

        static float NodeDistance(Node node, float x, float y, float z, float cap)
        {
            if (node.Shape != null) return node.Shape.Distance(x, y, z);
            float solid = cap;
            foreach (var s in node.Solids!)
                if (s.Bounds.Distance(x, y, z) <= Math.Max(solid, 0)) solid = Math.Min(solid, NodeDistance(s, x, y, z, solid));
            if (solid >= cap) return cap;
            // The group's holes take its material away: max(solid, -hole), where only a hole deeper than -solid counts.
            float need = -solid, deepest = float.MaxValue;
            foreach (var h in node.Holes!)
            {
                float outside = h.Bounds.Distance(x, y, z);
                if (outside > 0 && outside >= need) continue;
                deepest = Math.Min(deepest, NodeDistance(h, x, y, z, Far));
            }
            return deepest == float.MaxValue ? solid : Math.Max(solid, -deepest);
        }

        /// <summary>
        /// True when every point of the segment is at least <see cref="Clearance"/> from everything, given the
        /// distances at its ends: the field changes no faster than the point moves, so the halves are checked only
        /// where the ends are too close to be sure.
        /// </summary>
        bool SegmentFree(Vector3 a, float da, Vector3 b, float db, float length, float room = Clearance)
        {
            if (da < room || db < room) return false;
            if (Math.Min(da, db) - length / 2 >= room || length < 0.5f) return true;
            var m = (a + b) * 0.5f;
            float dm = Distance(m);
            return SegmentFree(a, da, m, dm, length / 2, room) && SegmentFree(m, dm, b, db, length / 2, room);
        }

        bool SegmentFree(Vector3 a, Vector3 b, float room = Clearance) => SegmentFree(a, Distance(a), b, Distance(b), Vector3.Distance(a, b), room);

        /// <summary>
        /// How far a point is from the nearest part or body shape (not the floor), up to <see cref="Far"/>, and
        /// which shape that is: every shape near it is looked at, so the distance is never more than the true one.
        /// </summary>
        float SolidDistance(Vector3 p, out Shape? nearest)
        {
            nearest = null;
            float d = Far;
            var nodes = buckets[(BucketOf(p.X, x0, bx) * by + BucketOf(p.Y, y0, by)) * bz + BucketOf(p.Z, z0, bz)];
            foreach (var node in nodes)
            {
                if (node.Bounds.DistanceSquared(p.X, p.Y, p.Z) >= d * d) continue;
                float nd = NodeDistance(node, p.X, p.Y, p.Z, d);
                if (nd >= d) continue;
                d = nd;
                nearest = NearestShape(node, p);
            }
            return d;
        }

        /// <summary>The solid shape of a node nearest a point (a group's nearest member).</summary>
        static Shape? NearestShape(Node node, Vector3 p)
        {
            if (node.Shape != null) return node.Shape;
            Shape? best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in node.Solids!)
            {
                float d = NodeDistance(s, p.X, p.Y, p.Z, float.MaxValue);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = NearestShape(s, p);
            }
            return best;
        }

        /// <summary>The field with the floor and nothing left out, for pushing points and finding slopes.</summary>
        float FullDistance(Vector3 p) => Math.Min(p.Y - floor, SolidDistance(p, out _));

        /// <summary>The way the field rises fastest at a point (unit length): away from the nearest surface.</summary>
        Vector3 Gradient(Vector3 p)
        {
            const float h = 0.25f;
            var g = new Vector3(
                FullDistance(p + Vector3.UnitX * h) - FullDistance(p - Vector3.UnitX * h),
                FullDistance(p + Vector3.UnitY * h) - FullDistance(p - Vector3.UnitY * h),
                FullDistance(p + Vector3.UnitZ * h) - FullDistance(p - Vector3.UnitZ * h));
            return g.LengthSquared() > 1e-10f ? Vector3.Normalize(g) : Vector3.Zero;
        }

        /// <summary>
        /// The first part or body shape a ray meets (chassis frame, mm), within <paramref name="reach"/>: for gluing
        /// a wire where the player clicks. It walks the ray by the distance to the nearest solid, so it never steps
        /// into one. Null when it meets nothing, or the floor first.
        /// </summary>
        public SurfaceHit? Pick((float x, float y, float z) from, (float x, float y, float z) direction, float reach)
        {
            var o = V(from);
            var d = Unit(direction);
            float t = 0;
            for (int step = 0; step < 512 && t <= reach; step++)
            {
                var p = o + d * t;
                if (p.Y <= floor) return null;
                float distance = SolidDistance(p, out var shape);
                if (distance < 0.02f && shape != null)
                {
                    var n = SolidGradient(p);
                    if (n == Vector3.Zero) n = -d;
                    return new SurfaceHit(T(p), T(n), shape.Owner, shape.IsWheel);
                }
                t += Math.Max(distance, 0.02f);
            }
            return null;
        }

        /// <summary>
        /// The part or body shape nearest a point, within <paramref name="reach"/> (at most <see cref="Far"/>), and
        /// the spot on it nearest the point: for gluing a wire's point to what it is beside.
        /// </summary>
        public SurfaceHit? NearestSurface((float x, float y, float z) point, float reach)
        {
            var p = V(point);
            if (SolidDistance(p, out _) > Math.Min(reach, Far - 1)) return null;
            var g = SolidGradient(p);
            return g == Vector3.Zero ? null : Pick(point, T(-g), reach + 1);
        }

        Vector3 SolidGradient(Vector3 p)
        {
            const float h = 0.2f;
            var g = new Vector3(
                SolidDistance(p + Vector3.UnitX * h, out _) - SolidDistance(p - Vector3.UnitX * h, out _),
                SolidDistance(p + Vector3.UnitY * h, out _) - SolidDistance(p - Vector3.UnitY * h, out _),
                SolidDistance(p + Vector3.UnitZ * h, out _) - SolidDistance(p - Vector3.UnitZ * h, out _));
            return g.LengthSquared() > 1e-10f ? Vector3.Normalize(g) : Vector3.Zero;
        }

        /// <summary>
        /// Where a wire could pass nearest a point (chassis frame, mm): the point itself when it has room, else
        /// pushed out of what it is in or too near, up the field's slope.
        /// </summary>
        public (float x, float y, float z) Clear((float x, float y, float z) point)
        {
            var p = V(point);
            TryClear(ref p);
            return T(p);
        }

        /// <summary>Pushes a point out to <see cref="Clearance"/> and a little more; false when it cannot get that far out.</summary>
        bool TryClear(ref Vector3 p)
        {
            const float wanted = Clearance + 0.6f;
            for (int i = 0; i < 32; i++)
            {
                float d = FullDistance(p);
                if (d >= wanted) return true;
                var g = Gradient(p);
                if (g == Vector3.Zero) g = Vector3.UnitY;
                p += g * Math.Max(0.3f, wanted - d);
            }
            return FullDistance(p) >= Clearance;
        }

        // ------------------------------------------------------------------ routing

        /// <summary>
        /// Where a jumper's bare wire starts at a pin, along its exit (mm): a male end stands in a female header
        /// with its 14 mm housing above it, a female end covers a male pin to 8 mm above it, a stripped end leaves
        /// a screw terminal 2.5 mm out, and a part's own lead starts at its end.
        /// </summary>
        public static float StartOffset(PinStyle style) => style switch
        {
            PinStyle.Header => 14f,
            PinStyle.Pin => 8f,
            PinStyle.Terminal => 2.5f,
            _ => 0f,
        };

        /// <summary>
        /// How far out along its pin's exit a route's path starts (mm), clear of the pin's own part. A jumper may
        /// bend as soon as it is out of its housing or terminal, so the first bend may take all of it.
        /// </summary>
        const float Lead = 8f;

        /// <summary>A way for one of the design's wires, from its first pin through its points to its second (see <see cref="Path"/>).</summary>
        public List<(float x, float y, float z)>? Route(RobotDesign design, WireInstance wire) => Path(design, wire)?.Points;

        /// <summary>
        /// A way for one of the design's wires, from its first pin through the points the player gave it to its
        /// second, and where it passes each point; null when a pin is gone, or a wire without points has no way
        /// (the caller then draws its plain arc). Each stretch between two pins or points is laid as a wire of its
        /// own, so it goes round the parts as any wire does, and the stretches meet at a point running the same
        /// way, so the wire bends smoothly through it. Under a glued point the wire lies flat on the surface for
        /// <see cref="GlueLength"/> and rises off it at each end. A stretch with no way round (a point shut in)
        /// is a plain curve.
        /// </summary>
        public WirePath? Path(RobotDesign design, WireInstance wire)
        {
            var a = End(design, wire.FromPart, wire.FromPin);
            var b = End(design, wire.ToPart, wire.ToPin);
            if (a == null || b == null) return null;
            var first = new Tip(V(a.Value.start), Unit(a.Value.exit), a.Value.lead, guarded: true, point: false);
            var last = new Tip(V(b.Value.start), Unit(b.Value.exit), b.Value.lead, guarded: true, point: false);
            var anchors = Anchors(design, wire, first.At + first.Exit * first.Lead, last.At + last.Exit * last.Lead);

            var curve = new List<Vector3>();
            var placed = new List<PathAnchor>();
            var from = first;
            for (int k = 0; k <= anchors.Count; k++)
            {
                var to = k < anchors.Count ? Arrival(anchors[k]) : last;
                var stretch = Stretch(from, to);
                if (stretch == null)
                {
                    if (anchors.Count == 0) return null;
                    stretch = PlainStretch(from, to);
                }
                if (k > 0)
                {
                    var anchor = anchors[k - 1];
                    int index;
                    if (anchor.Glued)
                    {
                        // Flat on the surface under the glue, from where the last stretch ended to where this one starts.
                        var e1 = curve[curve.Count - 1];
                        AddLine(curve, e1, anchor.At);
                        index = curve.Count - 1;
                        AddLine(curve, anchor.At, stretch[0]);
                        curve.RemoveAt(curve.Count - 1); // the stretch starts there
                    }
                    else
                    {
                        index = curve.Count - 1; // the last stretch ended at the point, and this one starts there
                        stretch.RemoveAt(0);
                    }
                    placed.Add(new PathAnchor(anchor.Point, T(anchor.At), T(anchor.Along), T(anchor.Normal), anchor.Glued, index));
                }
                curve.AddRange(stretch);
                if (k < anchors.Count) from = Departure(anchors[k]);
            }
            var points = new List<(float x, float y, float z)>(curve.Count);
            foreach (var p in curve) points.Add(T(p));
            return new WirePath(points, placed);
        }

        /// <summary>The same as the route's cache key: the router's <see cref="Key"/>, the wire's pins and its points.</summary>
        public string KeyFor(WireInstance wire)
        {
            var hash = new Hasher();
            hash.Add(Key).Add(wire.FromPart).Add(wire.FromPin).Add(wire.ToPart).Add(wire.ToPin);
            if (wire.Points != null)
                foreach (var p in wire.Points)
                    hash.Add(p.Glued ? 1 : 0).Add(p.Part).Add(p.Shape).Add(p.X).Add(p.Y).Add(p.Z).Add(p.NX).Add(p.NY).Add(p.NZ);
            return hash.ToString();
        }

        /// <summary>One end of a stretch of wire: a pin, or one side of a player's point.</summary>
        readonly struct Tip
        {
            public Tip(Vector3 at, Vector3 exit, float lead, bool guarded, bool point)
            {
                At = at;
                Exit = exit;
                Lead = lead;
                Guarded = guarded;
                Point = point;
            }

            public Vector3 At { get; }

            /// <summary>The way the wire leaves it for the stretch.</summary>
            public Vector3 Exit { get; }

            /// <summary>How far out along the exit the stretch's path starts.</summary>
            public float Lead { get; }

            /// <summary>The wire leaves a part or its glue there, so its first millimetres are not checked.</summary>
            public bool Guarded { get; }

            /// <summary>One of the player's points: the slack has no slope there, so the wire runs on smoothly.</summary>
            public bool Point { get; }
        }

        /// <summary>A player's point as the wire passes it.</summary>
        struct Anchor
        {
            public int Point;
            public Vector3 At, Normal, Along;
            public bool Glued;
        }

        /// <summary>The end of the stretch that comes to a point: the wire arrives running along it.</summary>
        static Tip Arrival(Anchor anchor) => anchor.Glued
            ? new Tip(anchor.At - anchor.Along * (GlueLength / 2), Vector3.Normalize(-anchor.Along + anchor.Normal * GlueRise), GlueLead, guarded: true, point: true)
            : new Tip(anchor.At, -anchor.Along, PointLead, guarded: false, point: true);

        /// <summary>The start of the stretch that leaves a point.</summary>
        static Tip Departure(Anchor anchor) => anchor.Glued
            ? new Tip(anchor.At + anchor.Along * (GlueLength / 2), Vector3.Normalize(anchor.Along + anchor.Normal * GlueRise), GlueLead, guarded: true, point: true)
            : new Tip(anchor.At, anchor.Along, PointLead, guarded: false, point: true);

        /// <summary>
        /// The wire's points where it passes them: a free point pushed out of anything it is in (left out when it
        /// cannot be), a glued one just off its surface; the way through each is halfway between the way in and the
        /// way out, along the surface when it is glued or near a surface.
        /// </summary>
        List<Anchor> Anchors(RobotDesign design, WireInstance wire, Vector3 before, Vector3 after)
        {
            var list = new List<Anchor>();
            if (wire.Points == null) return list;
            for (int i = 0; i < wire.Points.Count; i++)
            {
                var place = DesignGeometry.PointPlace(design, wire.Points[i]);
                if (place == null) continue;
                bool glued = wire.Points[i].Glued;
                var normal = Unit(place.Value.normal);
                var at = V(place.Value.at);
                if (glued) at += normal * GlueHeight;
                else if (!TryClear(ref at)) continue;
                list.Add(new Anchor { Point = i, At = at, Normal = normal, Glued = glued });
            }
            for (int k = 0; k < list.Count; k++)
            {
                var anchor = list[k];
                var prev = k == 0 ? before : list[k - 1].At;
                var next = k + 1 == list.Count ? after : list[k + 1].At;
                var along = Direction(anchor.At - prev) + Direction(next - anchor.At);
                var across = anchor.Normal;
                if (!anchor.Glued)
                {
                    // Near a surface a wire runs along it: one of its two sides would otherwise point into it.
                    across = Gradient(anchor.At);
                    if (FullDistance(anchor.At) > 6 || across.LengthSquared() < 1e-6f) across = Vector3.Zero;
                }
                along -= across * Vector3.Dot(along, across);
                if (along.LengthSquared() < 1e-4f)
                {
                    along = next - prev;
                    along -= across * Vector3.Dot(along, across);
                }
                if (along.LengthSquared() < 1e-4f) along = Perpendicular(anchor.Glued || across.LengthSquared() > 0 ? across : Vector3.UnitY);
                anchor.Along = Vector3.Normalize(along);
                list[k] = anchor;
            }
            return list;
        }

        static Vector3 Direction(Vector3 v) => v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : Vector3.Zero;

        static Vector3 Perpendicular(Vector3 n)
        {
            var other = Math.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            return Vector3.Normalize(Vector3.Cross(n, other));
        }

        /// <summary>
        /// One stretch of a wire, laid as a wire of its own: slack as a jumper's between two pins, half of it at a
        /// point, and with no slope at a point, so the wire runs on smoothly there. Points a few millimetres apart
        /// are simply joined.
        /// </summary>
        List<Vector3>? Stretch(Tip from, Tip to)
        {
            float gap = Vector3.Distance(from.At, to.At);
            if ((from.Point || to.Point) && gap < 3) return Resample(new List<Vector3> { from.At, to.At }, Spacing);
            float leadFrom = from.Guarded ? from.Lead : Math.Min(from.Lead, 0.3f * gap);
            float leadTo = to.Guarded ? to.Lead : Math.Min(to.Lead, 0.3f * gap);
            float slack = (from.Point ? 0.5f : 1f) * (to.Point ? 0.5f : 1f);
            return Leg(from.At, from.Exit, leadFrom, from.Guarded, to.At, to.Exit, leadTo, to.Guarded, slack, from.Point || to.Point);
        }

        /// <summary>A stretch with no way round: a plain curve leaving each end along its exit.</summary>
        static List<Vector3> PlainStretch(Tip from, Tip to)
        {
            float reach = Math.Max(2f, Vector3.Distance(from.At, to.At) / 3);
            Vector3 a = from.At, p1 = from.At + from.Exit * reach, p2 = to.At + to.Exit * reach, b = to.At;
            var curve = new List<Vector3>(25);
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f, u = 1 - t;
                curve.Add(u * u * u * a + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * b);
            }
            return Resample(curve, Spacing);
        }

        /// <summary>Points from <paramref name="a"/> (already in the list) to <paramref name="b"/>, about <see cref="Spacing"/> apart.</summary>
        static void AddLine(List<Vector3> curve, Vector3 a, Vector3 b)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(a, b) / Spacing));
            for (int i = 1; i <= n; i++) curve.Add(Vector3.Lerp(a, b, i / (float)n));
        }

        static Vector3 V((float x, float y, float z) v) => new Vector3(v.x, v.y, v.z);

        static (float x, float y, float z) T(Vector3 v) => (v.X, v.Y, v.Z);

        static ((float x, float y, float z) start, (float x, float y, float z) exit, float lead)? End(RobotDesign design, string partId, string pinId)
        {
            var part = design.Find(partId);
            var pin = part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
            var position = DesignGeometry.PinPosition(design, partId, pinId);
            var exit = DesignGeometry.PinExit(design, partId, pinId);
            if (pin == null || position == null || exit == null) return null;
            var e = Unit(exit.Value);
            float offset = StartOffset(pin.Style);
            var p = position.Value;
            return ((p.x + e.X * offset, p.y + e.Y * offset, p.z + e.Z * offset), exit.Value, Lead);
        }

        /// <summary>
        /// A way for a wire from <paramref name="from"/>, leaving it along <paramref name="fromExit"/> (its path
        /// starts <paramref name="fromLead"/> mm out, or further until it is clear), to <paramref name="to"/>,
        /// arriving along the reverse of <paramref name="toExit"/>: points about 2 mm apart from one end to the
        /// other, or null when there is no way (the caller then draws its plain arc).
        /// </summary>
        public List<(float x, float y, float z)>? Route((float x, float y, float z) from, (float x, float y, float z) fromExit, float fromLead,
            (float x, float y, float z) to, (float x, float y, float z) toExit, float toLead)
        {
            var curve = Leg(V(from), Unit(fromExit), fromLead, true, V(to), Unit(toExit), toLead, true, 1f, false);
            if (curve == null) return null;
            var result = new List<(float x, float y, float z)>(curve.Count);
            foreach (var p in curve) result.Add(T(p));
            return result;
        }

        /// <summary>
        /// One wire or stretch of wire from <paramref name="a"/> to <paramref name="b"/>: out along each exit, A*,
        /// pulled straight, jogs out, the corners made arcs, a point every 2 mm, and <paramref name="slack"/> times
        /// a jumper's slack (with no slope at the ends when <paramref name="flatEnds"/>). A guarded end leaves a part
        /// or its glue: its first millimetres are not checked. An unguarded end (a player's point, in the open) runs
        /// straight on only as far as it is clear.
        /// </summary>
        List<Vector3>? Leg(Vector3 a, Vector3 ea, float fromLead, bool guardFrom, Vector3 b, Vector3 eb, float toLead, bool guardTo, float slack, bool flatEnds)
        {
            float leadA, leadB;
            var pa = guardFrom ? LeadOut(a, ea, fromLead, out leadA) : OpenLead(a, ea, fromLead, out leadA);
            var pb = guardTo ? LeadOut(b, eb, toLead, out leadB) : OpenLead(b, eb, toLead, out leadB);
            int start = NearestFreeCell(pa), goal = NearestFreeCell(pb);
            if (start < 0 || goal < 0) return null;
            var cells = Search(start, goal);
            if (cells == null) return null;

            var path = new List<Vector3>(cells.Count + 2) { pa };
            foreach (int c in cells) path.Add(Centre(c));
            path.Add(pb);
            path = Unjog(Pull(path));
            var curve = new List<Vector3>(path.Count + 2) { a };
            foreach (var p in path) if (Vector3.DistanceSquared(p, curve[curve.Count - 1]) > 1e-4f) curve.Add(p);
            if (Vector3.DistanceSquared(b, curve[curve.Count - 1]) > 1e-4f) curve.Add(b);
            float guardA = guardFrom ? leadA + 2 : 0, guardB = guardTo ? leadB + 2 : 0;
            curve = Fillet(curve, guardA, guardB);
            curve = Resample(curve, Spacing);
            return slack > 0 ? Slacken(curve, guardA, guardB, slack, flatEnds) : curve;
        }

        /// <summary>From a point in the open: straight on along the exit as far as the lead, or less where that is not clear.</summary>
        Vector3 OpenLead(Vector3 end, Vector3 exit, float lead, out float used)
        {
            for (used = lead; used >= 0.5f; used *= 0.5f)
                if (SegmentFree(end, end + exit * used)) return end + exit * used;
            used = 0;
            return end;
        }

        static Vector3 Unit((float x, float y, float z) v)
        {
            var u = new Vector3(v.x, v.y, v.z);
            float length = u.Length();
            return length > 1e-6f ? u / length : Vector3.UnitY;
        }

        /// <summary>Where the wire has left its pin's part: along the exit, at least the lead and on until it is clear.</summary>
        Vector3 LeadOut(Vector3 end, Vector3 exit, float lead, out float used)
        {
            for (float t = Math.Max(lead, 1f); t <= lead + 30; t += 1f)
            {
                var p = end + exit * t;
                if (Distance(p) >= Clearance + 0.5f)
                {
                    used = t;
                    return p;
                }
            }
            used = lead;
            return end + exit * lead;
        }

        Vector3 Centre(int cell)
        {
            int ix = cell / (ny * nz), rest = cell % (ny * nz), iy = rest / nz, iz = rest % nz;
            return new Vector3(x0 + (ix + 0.5f) * size, y0 + (iy + 0.5f) * size, z0 + (iz + 0.5f) * size);
        }

        float Field(int cell)
        {
            float d = field[cell];
            if (float.IsNaN(d)) field[cell] = d = Distance(Centre(cell));
            return d;
        }

        /// <summary>The clear cell nearest a point, within three cells of the one it is in; -1 when there is none.</summary>
        int NearestFreeCell(Vector3 p)
        {
            int cx = Math.Min(nx - 1, Math.Max(0, (int)((p.X - x0) / size)));
            int cy = Math.Min(ny - 1, Math.Max(0, (int)((p.Y - y0) / size)));
            int cz = Math.Min(nz - 1, Math.Max(0, (int)((p.Z - z0) / size)));
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int x = Math.Max(0, cx - 3); x <= Math.Min(nx - 1, cx + 3); x++)
                for (int y = Math.Max(0, cy - 3); y <= Math.Min(ny - 1, cy + 3); y++)
                    for (int z = Math.Max(0, cz - 3); z <= Math.Min(nz - 1, cz + 3); z++)
                    {
                        int cell = (x * ny + y) * nz + z;
                        float away = Vector3.DistanceSquared(Centre(cell), p);
                        if (away >= bestDistance || Field(cell) < Clearance) continue;
                        best = cell;
                        bestDistance = away;
                    }
            return best;
        }

        /// <summary>
        /// A* over the cells, 26 neighbours, every move checked against the field; a step with less than
        /// <see cref="Comfort"/> of room costs up to <see cref="TightCost"/> times its length.
        /// </summary>
        List<int>? Search(int start, int goal)
        {
            int cells = field.Length;
            if (g == null || g.Length < cells)
            {
                g = new float[cells];
                came = new int[cells];
                opened = new int[cells];
                closed = new int[cells];
            }
            var cost = g;
            var before = came!;
            var open = opened!;
            var done = closed!;
            search++;
            heap.Clear();
            var target = Centre(goal);
            cost[start] = 0;
            before[start] = -1;
            open[start] = search;
            heap.Push(start, Greed * Vector3.Distance(Centre(start), target));
            int expansions = 0;
            while (heap.Count > 0)
            {
                int cell = heap.Pop();
                if (done[cell] == search) continue;
                done[cell] = search;
                if (cell == goal) return Trace(goal, before);
                Expansions++;
                if (++expansions > MaxExpansions) return null;
                int ix = cell / (ny * nz), rest = cell % (ny * nz), iy = rest / nz, iz = rest % nz;
                var here = Centre(cell);
                float clear = Field(cell);
                for (int k = 0; k < 26; k++)
                {
                    int jx = ix + Dx[k], jy = iy + Dy[k], jz = iz + Dz[k];
                    if ((uint)jx >= (uint)nx || (uint)jy >= (uint)ny || (uint)jz >= (uint)nz) continue;
                    int next = (jx * ny + jy) * nz + jz;
                    if (done[next] == search) continue;
                    float step = size * Steps[k];
                    float clearNext = Field(next);
                    if (clearNext < Clearance) continue;
                    float tight = Math.Max(0, Comfort - Math.Min(clear, clearNext)) / (Comfort - Clearance);
                    float reached = cost[cell] + step * (1 + (TightCost - 1) * Math.Min(1, tight));
                    if (open[next] == search && reached >= cost[next]) continue;
                    var there = Centre(next);
                    if (!SegmentFree(here, clear, there, clearNext, step)) continue;
                    open[next] = search;
                    cost[next] = reached;
                    before[next] = cell;
                    heap.Push(next, reached + Greed * Vector3.Distance(there, target));
                }
            }
            return null;
        }

        static List<int> Trace(int goal, int[] before)
        {
            var cells = new List<int>();
            for (int cell = goal; cell >= 0; cell = before[cell]) cells.Add(cell);
            cells.Reverse();
            return cells;
        }

        /// <summary>
        /// Straightens the path: from each point on to the farthest one it can see without coming closer to anything
        /// than the stretch it replaces (or than <see cref="Comfort"/>), so it keeps its room for round bends.
        /// </summary>
        List<Vector3> Pull(List<Vector3> path)
        {
            var clear = new float[path.Count];
            for (int k = 0; k < path.Count; k++) clear[k] = Distance(path[k]);
            var result = new List<Vector3> { path[0] };
            int i = 0;
            while (i < path.Count - 1)
            {
                int best = i + 1, misses = 0;
                float room = Math.Min(Comfort, Math.Min(clear[i], clear[i + 1]));
                for (int j = i + 2; j < path.Count && misses < 8; j++)
                {
                    room = Math.Max(Clearance, Math.Min(room, clear[j]));
                    if (SegmentFree(path[i], clear[i], path[j], clear[j], Vector3.Distance(path[i], path[j]), room))
                    {
                        best = j;
                        misses = 0;
                    }
                    else
                    {
                        misses++;
                    }
                }
                result.Add(path[best]);
                i = best;
            }
            return result;
        }

        /// <summary>
        /// Takes out a point that makes a jog, a side shorter than <see cref="Jog"/>, where the wire can go straight
        /// past it: a jog leaves neither of its two bends room to be round.
        /// </summary>
        List<Vector3> Unjog(List<Vector3> path)
        {
            int i = 1;
            while (i + 1 < path.Count)
            {
                float shorter = Math.Min(Vector3.Distance(path[i - 1], path[i]), Vector3.Distance(path[i], path[i + 1]));
                if (shorter < Jog && SegmentFree(path[i - 1], path[i + 1])) path.RemoveAt(i);
                else i++;
            }
            return path;
        }

        /// <summary>
        /// Makes every corner an arc, as a wire bends: up to <see cref="MaxBendRadius"/>, as far as its two sides
        /// allow, and smaller only where the arc would touch something. A side between two bends is shared so that
        /// both get the same radius, and what one of them cannot use goes to the other; a side from a pin goes
        /// wholly to its bend, so a wire may start to bend as soon as it is out of its housing or terminal. Near
        /// the ends, where the wire leaves its own part, the arcs are not checked.
        /// </summary>
        List<Vector3> Fillet(List<Vector3> curve, float guardA, float guardB)
        {
            int n = curve.Count;
            var a = curve[0];
            var b = curve[n - 1];
            var side = new float[n - 1];
            for (int i = 0; i + 1 < n; i++) side[i] = Vector3.Distance(curve[i], curve[i + 1]);
            var turn = new float[n];
            var slope = new float[n]; // tan(turn / 2): how much of its sides a bend of radius 1 takes
            for (int i = 1; i + 1 < n; i++)
            {
                if (side[i - 1] < 1e-3f || side[i] < 1e-3f) continue;
                var u = (curve[i] - curve[i - 1]) / side[i - 1];
                var w = (curve[i + 1] - curve[i]) / side[i];
                turn[i] = MathF.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(u, w))));
                // Straight on, or a fold no wire makes: it stays a point.
                if (turn[i] >= 0.03f && turn[i] <= 3.1f) slope[i] = MathF.Tan(turn[i] / 2);
            }
            var radius = new float[n];
            for (int i = 1; i + 1 < n; i++)
                if (slope[i] > 0)
                    radius[i] = Math.Min(MaxBendRadius, Math.Min(side[i - 1] / (slope[i - 1] + slope[i]), side[i] / (slope[i] + slope[i + 1])));
            for (int i = 1; i + 1 < n; i++)
                if (slope[i] > 0)
                    radius[i] = Math.Min(MaxBendRadius, Math.Min(side[i - 1] - slope[i - 1] * radius[i - 1], side[i] - slope[i + 1] * radius[i + 1]) / slope[i]);

            var result = new List<Vector3>(n * 6) { a };
            for (int i = 1; i + 1 < n; i++)
            {
                var corner = curve[i];
                if (slope[i] <= 0)
                {
                    result.Add(corner);
                    continue;
                }
                var u = (corner - curve[i - 1]) / side[i - 1];
                var w = (curve[i + 1] - corner) / side[i];
                float reach = slope[i] * radius[i];
                List<Vector3>? arc = null;
                for (int attempt = 0; attempt < 8 && arc == null; attempt++, reach *= 0.7f)
                {
                    var candidate = Arc(corner, u, w, turn[i], reach);
                    if (CurveFree(candidate, a, guardA, b, guardB, Touch)) arc = candidate;
                }
                if (arc != null) result.AddRange(arc);
                else result.Add(corner);
            }
            result.Add(b);
            return result;
        }

        /// <summary>
        /// The arc that turns from direction <paramref name="u"/> into <paramref name="w"/> at a corner, touching
        /// each side <paramref name="reach"/> from the corner, in steps of at most 10°.
        /// </summary>
        static List<Vector3> Arc(Vector3 corner, Vector3 u, Vector3 w, float turn, float reach)
        {
            var inward = w - u * Vector3.Dot(u, w); // across u, toward the inside of the bend
            float across = inward.Length();
            if (across < 1e-5f) return new List<Vector3> { corner };
            inward /= across;
            float radius = reach / MathF.Tan(turn / 2);
            var centre = corner - u * reach + inward * radius;
            int steps = Math.Max(2, (int)MathF.Ceiling(turn / (10f * MathF.PI / 180f)));
            var points = new List<Vector3>(steps + 1);
            for (int k = 0; k <= steps; k++)
            {
                float angle = turn * k / steps;
                points.Add(centre - inward * (radius * MathF.Cos(angle)) + u * (radius * MathF.Sin(angle)));
            }
            return points;
        }

        /// <summary>The same line with a point every <paramref name="spacing"/> mm along it (and at its two ends).</summary>
        static List<Vector3> Resample(List<Vector3> curve, float spacing)
        {
            var result = new List<Vector3> { curve[0] };
            float carried = 0; // length since the last point put down
            for (int i = 0; i + 1 < curve.Count; i++)
            {
                var p = curve[i];
                var q = curve[i + 1];
                float length = Vector3.Distance(p, q);
                float at = spacing - carried;
                while (at < length)
                {
                    result.Add(p + (q - p) * (at / length));
                    at += spacing;
                }
                carried = length - (at - spacing);
            }
            if (Vector3.DistanceSquared(result[result.Count - 1], curve[curve.Count - 1]) > 1e-6f) result.Add(curve[curve.Count - 1]);
            return result;
        }

        /// <summary>
        /// Gives the wire some slack: its middle rises by up to a tenth of its length (2 to 16 mm) times
        /// <paramref name="scale"/>, as a jumper arches, or by less where that would touch something. With
        /// <paramref name="flatEnds"/> the rise starts with no slope (sin²), so a stretch meets the next one at a
        /// point smoothly.
        /// </summary>
        List<Vector3> Slacken(List<Vector3> curve, float guardA, float guardB, float scale = 1, bool flatEnds = false)
        {
            var along = new float[curve.Count];
            for (int i = 1; i < curve.Count; i++) along[i] = along[i - 1] + Vector3.Distance(curve[i - 1], curve[i]);
            float length = along[curve.Count - 1];
            if (length < 1) return curve;
            float rise = Math.Min(16f, Math.Max(2f, 0.1f * length)) * scale;
            for (float share = 1; share > 0.2f; share /= 2)
            {
                var lifted = new List<Vector3>(curve.Count);
                for (int i = 0; i < curve.Count; i++)
                {
                    float wave = MathF.Sin(MathF.PI * along[i] / length);
                    lifted.Add(curve[i] + Vector3.UnitY * (rise * share * (flatEnds ? wave * wave : wave)));
                }
                if (CurveFree(lifted, guardA, guardB, Touch)) return lifted;
            }
            return curve;
        }

        /// <summary>
        /// True when the curve is clear, except within the guards round its two ends, where the wire leaves its own
        /// part: a segment reaching into a guard is checked from where it comes out of it.
        /// </summary>
        bool CurveFree(List<Vector3> curve, float guardA, float guardB, float room) => CurveFree(curve, curve[0], guardA, curve[curve.Count - 1], guardB, room);

        /// <summary>As the other overload, for a piece of a wire whose ends are <paramref name="a"/> and <paramref name="b"/>.</summary>
        bool CurveFree(List<Vector3> curve, Vector3 a, float guardA, Vector3 b, float guardB, float room)
        {
            for (int i = 0; i + 1 < curve.Count; i++)
            {
                var p = curve[i];
                var q = curve[i + 1];
                float t0 = 0, t1 = 1;
                OutsideOf(p, q, a, guardA, ref t0, ref t1);
                OutsideOf(p, q, b, guardB, ref t0, ref t1);
                if (t1 - t0 < 1e-4f) continue;
                if (!SegmentFree(p + (q - p) * t0, p + (q - p) * t1, room)) return false;
            }
            return true;
        }

        /// <summary>Narrows [t0, t1] of the segment p–q to the part outside a ball, when one end of it is inside.</summary>
        static void OutsideOf(Vector3 p, Vector3 q, Vector3 centre, float radius, ref float t0, ref float t1)
        {
            bool pIn = Vector3.Distance(p, centre) < radius, qIn = Vector3.Distance(q, centre) < radius;
            if (!pIn && !qIn) return;
            if (pIn && qIn)
            {
                t0 = 1;
                t1 = 0;
                return;
            }
            var d = q - p;
            var f = p - centre;
            float a = Vector3.Dot(d, d), b = 2 * Vector3.Dot(f, d), c = Vector3.Dot(f, f) - radius * radius;
            float root = MathF.Sqrt(Math.Max(0, b * b - 4 * a * c));
            if (pIn) t0 = Math.Max(t0, (-b + root) / (2 * a)); // where it leaves the ball
            else t1 = Math.Min(t1, (-b - root) / (2 * a));      // where it enters it
        }

        // ------------------------------------------------------------------ shapes

        readonly struct Box3
        {
            public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

            public Box3(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            {
                MinX = minX;
                MinY = minY;
                MinZ = minZ;
                MaxX = maxX;
                MaxY = maxY;
                MaxZ = maxZ;
            }

            public static Box3 Empty => new Box3(float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue);

            public bool IsEmpty => MinX > MaxX;

            public Box3 Union(Box3 o) => new Box3(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Min(MinZ, o.MinZ),
                Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY), Math.Max(MaxZ, o.MaxZ));

            public Box3 Include(float x, float y, float z) => new Box3(Math.Min(MinX, x), Math.Min(MinY, y), Math.Min(MinZ, z),
                Math.Max(MaxX, x), Math.Max(MaxY, y), Math.Max(MaxZ, z));

            /// <summary>How far a point is outside the box; 0 inside it.</summary>
            public float Distance(float x, float y, float z) => MathF.Sqrt(DistanceSquared(x, y, z));

            public float DistanceSquared(float x, float y, float z)
            {
                float dx = Math.Max(0, Math.Max(MinX - x, x - MaxX));
                float dy = Math.Max(0, Math.Max(MinY - y, y - MaxY));
                float dz = Math.Max(0, Math.Max(MinZ - z, z - MaxZ));
                return dx * dx + dy * dy + dz * dz;
            }
        }

        /// <summary>A body shape or group record as the field sees it.</summary>
        sealed class Node
        {
            public Shape? Shape;
            public List<Node>? Solids, Holes;
            public Box3 Bounds; // round its solid material

            public static Node Leaf(Shape shape) => new Node { Shape = shape, Bounds = shape.Bounds };

            public static Node Group(List<Node> solids, List<Node> holes)
            {
                var bounds = Box3.Empty;
                foreach (var s in solids) bounds = bounds.Union(s.Bounds);
                return new Node { Solids = solids, Holes = holes, Bounds = bounds };
            }
        }

        /// <summary>
        /// One solid, in its own frame: centre, turn and half sizes. Its distance is exact for boxes and round
        /// cylinders and a safe underestimate for the others (an ellipse, a cone's slope, a wedge's edges).
        /// </summary>
        sealed class Shape
        {
            FeatureKind kind;
            bool alongX;                                          // a wheel: its axis is its x, not its y
            float cx, cy, cz;                                     // centre, chassis frame
            float r00, r01, r02, r10, r11, r12, r20, r21, r22;    // chassis frame to its own
            float hx, hy, hz;                                     // half sizes
            float detail;
            float[]? outline;                                     // an extrusion's outline, x and z in mm
            public Box3 Bounds;
            public string Owner = "";                             // the part's id, or the body shape's
            public bool IsWheel;

            public static Shape Block(Rot3 turn, PartInstance part, PartBlock block)
            {
                var c = turn.Apply(block.Centre);
                var s = Make(FeatureKind.Box, turn, part.X + c.x, part.Y + c.y, part.Z + c.z, block.Size.x / 2, block.Size.y / 2, block.Size.z / 2);
                s.Owner = part.Id;
                return s;
            }

            /// <summary>A motor's 65 mm wheel on its shaft (the motor's x axis).</summary>
            public static Shape Wheel(Rot3 turn, PartInstance part)
            {
                var c = DesignGeometry.WheelCentre(part);
                var s = Make(FeatureKind.Cylinder, turn, c.x, c.y, c.z, DesignGeometry.WheelRadius, DesignGeometry.WheelWidth / 2, DesignGeometry.WheelRadius, alongX: true);
                s.Owner = part.Id;
                s.IsWheel = true;
                return s;
            }

            public static Shape Feature(BodyFeature f)
            {
                var kind = f.Kind == FeatureKind.Imported ? FeatureKind.Box : f.Kind; // an imported mesh counts as its box
                var s = Make(kind, Rot3.Euler(f.RotX, f.RotY, f.RotZ), f.X, f.Y, f.Z,
                    Math.Max(0.1f, f.SizeX) / 2, Math.Max(0.1f, f.SizeY) / 2, Math.Max(0.1f, f.SizeZ) / 2);
                s.detail = f.Detail;
                s.Owner = f.Id;
                if (kind == FeatureKind.Extrusion)
                {
                    int n = f.Outline.Count / 2;
                    if (n >= 3)
                    {
                        s.outline = new float[2 * n];
                        for (int i = 0; i < n; i++)
                        {
                            s.outline[2 * i] = f.Outline[2 * i] * f.SizeX;
                            s.outline[2 * i + 1] = f.Outline[2 * i + 1] * f.SizeZ;
                        }
                    }
                    else
                    {
                        s.kind = FeatureKind.Box;
                    }
                }
                return s;
            }

            static Shape Make(FeatureKind kind, Rot3 turn, float cx, float cy, float cz, float hx, float hy, float hz, bool alongX = false)
            {
                var back = turn.Inverse();
                var s = new Shape
                {
                    kind = kind,
                    alongX = alongX,
                    cx = cx,
                    cy = cy,
                    cz = cz,
                    r00 = (float)back.M00, r01 = (float)back.M01, r02 = (float)back.M02,
                    r10 = (float)back.M10, r11 = (float)back.M11, r12 = (float)back.M12,
                    r20 = (float)back.M20, r21 = (float)back.M21, r22 = (float)back.M22,
                    hx = hx,
                    hy = hy,
                    hz = hz,
                };
                // Its box in its own frame (a wheel's half width lies along x), turned into the chassis frame.
                float ex = alongX ? hy : hx, ey = alongX ? hx : hy;
                var bounds = Box3.Empty;
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sy in new[] { -1f, 1f })
                        foreach (float sz in new[] { -1f, 1f })
                        {
                            var p = turn.Apply(sx * ex, sy * ey, sz * hz);
                            bounds = bounds.Include(cx + p.x, cy + p.y, cz + p.z);
                        }
                s.Bounds = bounds;
                return s;
            }

            public float Distance(float x, float y, float z)
            {
                float px = x - cx, py = y - cy, pz = z - cz;
                float lx = r00 * px + r01 * py + r02 * pz;
                float ly = r10 * px + r11 * py + r12 * pz;
                float lz = r20 * px + r21 * py + r22 * pz;
                if (alongX) (lx, ly) = (ly, lx);
                switch (kind)
                {
                    case FeatureKind.RoundedBox:
                    {
                        float r = Math.Max(0, Math.Min(detail, Math.Min(hx, Math.Min(hy, hz)) - 0.05f));
                        return BoxDistance(lx, ly, lz, hx - r, hy - r, hz - r) - r;
                    }
                    case FeatureKind.Plate:
                    {
                        float r = Math.Max(0, Math.Min(detail, Math.Min(hx, hz)));
                        return Combine(RoundedRect(lx, lz, hx, hz, r), Math.Abs(ly) - hy);
                    }
                    case FeatureKind.Cylinder:
                        return Combine(Ellipse(lx, lz, hx, hz), Math.Abs(ly) - hy);
                    case FeatureKind.Cone:
                    {
                        // Base (radius 1) at the bottom, the top radius a share of it; the side's slope normalised.
                        float t = Math.Max(0, Math.Min(1, detail)), m = Math.Min(hx, hz);
                        float radial = MathF.Sqrt(lx * lx / (hx * hx) + lz * lz / (hz * hz));
                        float k = 1 + (t - 1) * (ly + hy) / (2 * hy);
                        float slope = m * (1 - t) / (2 * hy);
                        return Math.Max((radial - k) * m / MathF.Sqrt(1 + slope * slope), Math.Abs(ly) - hy);
                    }
                    case FeatureKind.Sphere:
                    {
                        float m = Math.Min(hx, Math.Min(hy, hz));
                        return (MathF.Sqrt(lx * lx / (hx * hx) + ly * ly / (hy * hy) + lz * lz / (hz * hz)) - 1) * m;
                    }
                    case FeatureKind.Wedge:
                    {
                        // A ramp: flat bottom, upright back (−z), the slope down to the bottom front edge.
                        float a = 1 / (2 * hy), b = 1 / (2 * hz);
                        float slope = (a * (ly + hy) + b * (lz + hz) - 1) / MathF.Sqrt(a * a + b * b);
                        return Math.Max(Math.Max(-(ly + hy), -(lz + hz)), Math.Max(Math.Abs(lx) - hx, slope));
                    }
                    case FeatureKind.Tube:
                    {
                        float wall = Math.Max(0.4f, Math.Min(detail, Math.Min(hx, hz) - 0.2f));
                        float outer = Combine(Ellipse(lx, lz, hx, hz), Math.Abs(ly) - hy);
                        if (hx - wall < 0.05f || hz - wall < 0.05f) return outer;
                        float inner = Combine(Ellipse(lx, lz, hx - wall, hz - wall), Math.Abs(ly) - hy - 1);
                        return Math.Max(outer, -inner);
                    }
                    case FeatureKind.Extrusion:
                        return Combine(Polygon(lx, lz, outline!), Math.Abs(ly) - hy);
                    default: // boxes, part blocks, imported meshes
                        return BoxDistance(lx, ly, lz, hx, hy, hz);
                }
            }

            static float BoxDistance(float x, float y, float z, float hx, float hy, float hz)
            {
                float qx = Math.Abs(x) - hx, qy = Math.Abs(y) - hy, qz = Math.Abs(z) - hz;
                float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0), oz = Math.Max(qz, 0);
                return MathF.Sqrt(ox * ox + oy * oy + oz * oz) + Math.Min(Math.Max(qx, Math.Max(qy, qz)), 0);
            }

            /// <summary>A flat distance across (in x, z) and one along y, combined as a box's are.</summary>
            static float Combine(float across, float along)
            {
                float a = Math.Max(across, 0), b = Math.Max(along, 0);
                return MathF.Sqrt(a * a + b * b) + Math.Min(Math.Max(across, along), 0);
            }

            static float RoundedRect(float x, float z, float hx, float hz, float r)
            {
                float qx = Math.Abs(x) - hx + r, qz = Math.Abs(z) - hz + r;
                float ox = Math.Max(qx, 0), oz = Math.Max(qz, 0);
                return MathF.Sqrt(ox * ox + oz * oz) + Math.Min(Math.Max(qx, qz), 0) - r;
            }

            static float Ellipse(float x, float z, float a, float b)
            {
                a = Math.Max(a, 0.05f);
                b = Math.Max(b, 0.05f);
                return (MathF.Sqrt(x * x / (a * a) + z * z / (b * b)) - 1) * Math.Min(a, b);
            }

            static float Polygon(float x, float z, float[] points)
            {
                float best = float.MaxValue;
                bool inside = false;
                int n = points.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float ax = points[2 * i], az = points[2 * i + 1], bx = points[2 * j], bz = points[2 * j + 1];
                    float ex = bx - ax, ez = bz - az, wx = x - ax, wz = z - az;
                    float t = Math.Max(0, Math.Min(1, (wx * ex + wz * ez) / (ex * ex + ez * ez + 1e-12f)));
                    float dx = wx - ex * t, dz = wz - ez * t;
                    best = Math.Min(best, dx * dx + dz * dz);
                    if ((az > z) != (bz > z) && x < (bx - ax) * (z - az) / (bz - az) + ax) inside = !inside;
                }
                float d = MathF.Sqrt(best);
                return inside ? -d : d;
            }
        }

        /// <summary>A binary heap of cells by their estimated cost.</summary>
        sealed class Heap
        {
            float[] keys = new float[256];
            int[] values = new int[256];

            public int Count { get; private set; }

            public void Clear() => Count = 0;

            public void Push(int value, float key)
            {
                if (Count == keys.Length)
                {
                    Array.Resize(ref keys, Count * 2);
                    Array.Resize(ref values, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (keys[parent] <= key) break;
                    keys[i] = keys[parent];
                    values[i] = values[parent];
                    i = parent;
                }
                keys[i] = key;
                values[i] = value;
            }

            public int Pop()
            {
                int top = values[0];
                float key = keys[--Count];
                int value = values[Count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && keys[child + 1] < keys[child]) child++;
                    if (keys[child] >= key) break;
                    keys[i] = keys[child];
                    values[i] = values[child];
                    i = child;
                }
                keys[i] = key;
                values[i] = value;
                return top;
            }
        }

        /// <summary>FNV-1a over the numbers and names the routes depend on.</summary>
        sealed class Hasher
        {
            ulong hash = 14695981039346656037UL;

            public Hasher Add(int value)
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (byte)(value >> (8 * i));
                    hash *= 1099511628211UL;
                }
                return this;
            }

            public Hasher Add(float value) => Add(BitConverter.SingleToInt32Bits(value));

            public Hasher Add(string value)
            {
                foreach (char c in value) Add(c);
                return Add(value.Length);
            }

            public override string ToString() => hash.ToString("x16");
        }
    }
}
