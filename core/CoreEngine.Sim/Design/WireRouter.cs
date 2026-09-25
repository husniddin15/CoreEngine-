using System;
using System.Collections.Generic;
using System.Numerics;

namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// Lays jumper wires the way a builder does (docs/03 §6): out of a header or a terminal, then over the parts,
    /// round the edges of the plates or through a hole wide enough, never through a part or solid material.
    /// The robot is a distance field: how far a point is from the nearest part block (<see cref="PartDef.Solids"/>,
    /// a motor's wheel), body shape (a hole takes material only from the solids of its group, as Manifold cuts
    /// them) or the floor. A* crosses a grid of 5 mm cells, each move checked against the field; the path is
    /// then pulled straight where it can be, its corners are rounded, and it gets some slack upward, as a real
    /// jumper arches. Everything is in millimetres in the chassis frame.
    /// </summary>
    public sealed class WireRouter
    {
        /// <summary>How close a wire's centre line may come to anything: a jumper's radius and a little air.</summary>
        public const float Clearance = 1.4f;

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
        bool SegmentFree(Vector3 a, float da, Vector3 b, float db, float length)
        {
            if (da < Clearance || db < Clearance) return false;
            if (Math.Min(da, db) - length / 2 >= Clearance || length < 0.5f) return true;
            var m = (a + b) * 0.5f;
            float dm = Distance(m);
            return SegmentFree(a, da, m, dm, length / 2) && SegmentFree(m, dm, b, db, length / 2);
        }

        bool SegmentFree(Vector3 a, Vector3 b) => SegmentFree(a, Distance(a), b, Distance(b), Vector3.Distance(a, b));

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

        /// <summary>How far a wire runs straight on out of its housing or terminal before it bends (mm).</summary>
        public static float StraightOut(PinStyle style) => style == PinStyle.Header || style == PinStyle.Pin ? 8f : 4f;

        /// <summary>A way for one of the design's wires, from its first pin to its second (see the other overload).</summary>
        public List<(float x, float y, float z)>? Route(RobotDesign design, WireInstance wire)
        {
            var a = End(design, wire.FromPart, wire.FromPin);
            var b = End(design, wire.ToPart, wire.ToPin);
            if (a == null || b == null) return null;
            return Route(a.Value.start, a.Value.exit, a.Value.lead, b.Value.start, b.Value.exit, b.Value.lead);
        }

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
            return ((p.x + e.X * offset, p.y + e.Y * offset, p.z + e.Z * offset), exit.Value, StraightOut(pin.Style));
        }

        /// <summary>
        /// A way for a wire from <paramref name="from"/>, leaving it along <paramref name="fromExit"/> for at least
        /// <paramref name="fromLead"/> mm, to <paramref name="to"/>, arriving along the reverse of
        /// <paramref name="toExit"/>: points from one end to the other, or null when there is no way (the caller
        /// then draws its plain arc).
        /// </summary>
        public List<(float x, float y, float z)>? Route((float x, float y, float z) from, (float x, float y, float z) fromExit, float fromLead,
            (float x, float y, float z) to, (float x, float y, float z) toExit, float toLead)
        {
            var a = new Vector3(from.x, from.y, from.z);
            var b = new Vector3(to.x, to.y, to.z);
            var pa = LeadOut(a, Unit(fromExit), fromLead, out float leadA);
            var pb = LeadOut(b, Unit(toExit), toLead, out float leadB);
            int start = NearestFreeCell(pa), goal = NearestFreeCell(pb);
            if (start < 0 || goal < 0) return null;
            var cells = Search(start, goal);
            if (cells == null) return null;

            var path = new List<Vector3>(cells.Count + 2) { pa };
            foreach (int c in cells) path.Add(Centre(c));
            path.Add(pb);
            path = Pull(path);
            var curve = new List<Vector3>(path.Count + 2) { a };
            foreach (var p in path) if (Vector3.DistanceSquared(p, curve[curve.Count - 1]) > 1e-4f) curve.Add(p);
            if (Vector3.DistanceSquared(b, curve[curve.Count - 1]) > 1e-4f) curve.Add(b);
            float guardA = leadA + 2, guardB = leadB + 2;
            curve = Round(curve, guardA, guardB);
            curve = Slacken(curve, guardA, guardB);

            var result = new List<(float x, float y, float z)>(curve.Count);
            foreach (var p in curve) result.Add((p.X, p.Y, p.Z));
            return result;
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

        /// <summary>A* over the cells, 26 neighbours, every move checked against the field.</summary>
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
                    float reached = cost[cell] + step;
                    if (open[next] == search && reached >= cost[next]) continue;
                    float clearNext = Field(next);
                    if (clearNext < Clearance) continue;
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

        /// <summary>Straightens the path: from each point on to the farthest one it can see.</summary>
        List<Vector3> Pull(List<Vector3> path)
        {
            var clear = new float[path.Count];
            for (int k = 0; k < path.Count; k++) clear[k] = Distance(path[k]);
            var result = new List<Vector3> { path[0] };
            int i = 0;
            while (i < path.Count - 1)
            {
                int best = i + 1, misses = 0;
                for (int j = i + 2; j < path.Count && misses < 8; j++)
                {
                    if (SegmentFree(path[i], clear[i], path[j], clear[j], Vector3.Distance(path[i], path[j])))
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
        /// Rounds the corners (Chaikin's corner cutting, up to three times), keeping both ends; a pass that would
        /// touch something is not taken. Near the ends, where the wire leaves its own part, it is not checked.
        /// </summary>
        List<Vector3> Round(List<Vector3> curve, float guardA, float guardB)
        {
            for (int pass = 0; pass < 3 && curve.Count > 2; pass++)
            {
                var next = new List<Vector3>(curve.Count * 2) { curve[0] };
                for (int i = 0; i + 1 < curve.Count; i++)
                {
                    var p = curve[i];
                    var q = curve[i + 1];
                    if (i > 0) next.Add(p * 0.75f + q * 0.25f);
                    if (i + 2 < curve.Count) next.Add(p * 0.25f + q * 0.75f);
                }
                next.Add(curve[curve.Count - 1]);
                if (!CurveFree(next, guardA, guardB)) break;
                curve = next;
            }
            return curve;
        }

        /// <summary>
        /// Gives the wire some slack: its middle rises by up to a tenth of its length (2 to 16 mm), as a jumper
        /// arches, or by less where that would touch something.
        /// </summary>
        List<Vector3> Slacken(List<Vector3> curve, float guardA, float guardB)
        {
            var along = new float[curve.Count];
            for (int i = 1; i < curve.Count; i++) along[i] = along[i - 1] + Vector3.Distance(curve[i - 1], curve[i]);
            float length = along[curve.Count - 1];
            if (length < 1) return curve;
            float rise = Math.Min(16f, Math.Max(2f, 0.1f * length));
            for (float share = 1; share > 0.2f; share /= 2)
            {
                var lifted = new List<Vector3>(curve.Count);
                for (int i = 0; i < curve.Count; i++)
                    lifted.Add(curve[i] + Vector3.UnitY * (rise * share * MathF.Sin(MathF.PI * along[i] / length)));
                if (CurveFree(lifted, guardA, guardB)) return lifted;
            }
            return curve;
        }

        /// <summary>
        /// True when the curve is clear, except within the guards round its two ends, where the wire leaves its own
        /// part: a segment reaching into a guard is checked from where it comes out of it.
        /// </summary>
        bool CurveFree(List<Vector3> curve, float guardA, float guardB)
        {
            var a = curve[0];
            var b = curve[curve.Count - 1];
            for (int i = 0; i + 1 < curve.Count; i++)
            {
                var p = curve[i];
                var q = curve[i + 1];
                float t0 = 0, t1 = 1;
                OutsideOf(p, q, a, guardA, ref t0, ref t1);
                OutsideOf(p, q, b, guardB, ref t0, ref t1);
                if (t1 - t0 < 1e-4f) continue;
                if (!SegmentFree(p + (q - p) * t0, p + (q - p) * t1)) return false;
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

            public static Shape Block(Rot3 turn, PartInstance part, PartBlock block)
            {
                var c = turn.Apply(block.Centre);
                return Make(FeatureKind.Box, turn, part.X + c.x, part.Y + c.y, part.Z + c.z, block.Size.x / 2, block.Size.y / 2, block.Size.z / 2);
            }

            /// <summary>A motor's 65 mm wheel on its shaft (the motor's x axis).</summary>
            public static Shape Wheel(Rot3 turn, PartInstance part)
            {
                var c = DesignGeometry.WheelCentre(part);
                return Make(FeatureKind.Cylinder, turn, c.x, c.y, c.z, DesignGeometry.WheelRadius, DesignGeometry.WheelWidth / 2, DesignGeometry.WheelRadius, alongX: true);
            }

            public static Shape Feature(BodyFeature f)
            {
                var kind = f.Kind == FeatureKind.Imported ? FeatureKind.Box : f.Kind; // an imported mesh counts as its box
                var s = Make(kind, Rot3.Euler(f.RotX, f.RotY, f.RotZ), f.X, f.Y, f.Z,
                    Math.Max(0.1f, f.SizeX) / 2, Math.Max(0.1f, f.SizeY) / 2, Math.Max(0.1f, f.SizeZ) / 2);
                s.detail = f.Detail;
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
