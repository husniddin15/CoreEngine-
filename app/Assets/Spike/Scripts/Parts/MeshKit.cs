using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// Builds a part's mesh in millimetres from boxes with rounded edges, turned profiles, extruded outlines and
    /// swept tubes, one submesh per material slot, under a stack of transforms (<see cref="Push(Vector3, Quaternion)"/>,
    /// <see cref="Pop"/>). Every triangle is wound from its normals, so a mirrored transform cannot turn a face
    /// inside out. <see cref="ToMesh"/> hands the result over in metres, with tangents for normal maps.
    /// </summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<List<int>> slots = new List<List<int>>();
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        Matrix4x4 m = Matrix4x4.identity, nm = Matrix4x4.identity;

        public MeshKit(int slotCount = 0)
        {
            for (int i = 0; i < slotCount; i++) slots.Add(new List<int>());
        }

        public int VertexCount => vertices.Count;

        /// <summary>Makes room for this many slots, so a slot with no triangles still gets its (empty) submesh.</summary>
        public void EnsureSlots(int count)
        {
            while (slots.Count < count) slots.Add(new List<int>());
        }

        // ------------------------------------------------------------------ transforms

        public void Push(Vector3 position, Quaternion rotation) => Push(Matrix4x4.TRS(position, rotation, Vector3.one));

        public void Push(Vector3 position) => Push(Matrix4x4.Translate(position));

        public void Push(Matrix4x4 local)
        {
            stack.Push(m);
            m *= local;
            nm = m.inverse.transpose;
        }

        public void Pop()
        {
            m = stack.Pop();
            nm = m.inverse.transpose;
        }

        // ------------------------------------------------------------------ vertices and faces

        public int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            vertices.Add(m.MultiplyPoint3x4(position));
            normals.Add(nm.MultiplyVector(normal).normalized);
            uvs.Add(uv);
            return vertices.Count - 1;
        }

        /// <summary>A triangle, turned so that its front faces the way its vertices' normals point.</summary>
        public void Triangle(int slot, int a, int b, int c)
        {
            var face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (face.sqrMagnitude < 1e-14f) return;
            while (slots.Count <= slot) slots.Add(new List<int>());
            var list = slots[slot];
            if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) >= 0)
            {
                list.Add(a);
                list.Add(b);
                list.Add(c);
            }
            else
            {
                list.Add(a);
                list.Add(c);
                list.Add(b);
            }
        }

        public void Quad(int slot, int a, int b, int c, int d)
        {
            Triangle(slot, a, b, c);
            Triangle(slot, a, c, d);
        }

        /// <summary>A flat quad with one normal; corners in order round its edge; uv from 0 to 1 unless given.</summary>
        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Rect? uv = null)
        {
            var r = uv ?? new Rect(0, 0, 1, 1);
            Quad(slot, Vertex(a, normal, new Vector2(r.xMin, r.yMin)), Vertex(b, normal, new Vector2(r.xMax, r.yMin)),
                Vertex(c, normal, new Vector2(r.xMax, r.yMax)), Vertex(d, normal, new Vector2(r.xMin, r.yMax)));
        }

        // ------------------------------------------------------------------ boxes

        /// <summary>A face of a box, by the way it looks.</summary>
        public enum Face { MinusX, PlusX, MinusY, PlusY, MinusZ, PlusZ }

        /// <summary>
        /// A box with its edges rounded to <paramref name="radius"/> (0: sharp), in the current frame. The texture
        /// is laid on every face at <paramref name="uvPerMm"/>; <paramref name="topUv"/> maps one face (the +y one
        /// unless <paramref name="markFace"/> says otherwise) onto a rectangle of the texture instead, upright as
        /// seen from outside: a chip's marking, a sticker. That face goes to <paramref name="topSlot"/> when given.
        /// </summary>
        public void Box(int slot, Vector3 centre, Vector3 size, float radius = 0, float uvPerMm = 0.1f, Rect? topUv = null, int topSlot = -1,
            Face markFace = Face.PlusY)
        {
            var h = size / 2;
            radius = Mathf.Clamp(radius, 0, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f);
            var inner = h - Vector3.one * radius;
            for (int axis = 0; axis < 3; axis++)
            {
                foreach (int sign in Signs)
                {
                    int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                    float[] us = Lines(h[ua], radius), vs = Lines(h[va], radius);
                    bool top = (int)markFace == axis * 2 + (sign > 0 ? 1 : 0);
                    int faceSlot = top && topSlot >= 0 ? topSlot : slot;
                    var index = new int[us.Length, vs.Length];
                    for (int i = 0; i < us.Length; i++)
                    {
                        for (int j = 0; j < vs.Length; j++)
                        {
                            var p = Vector3.zero;
                            p[axis] = sign * h[axis];
                            p[ua] = us[i];
                            p[va] = vs[j];
                            var n = Vector3.zero;
                            n[axis] = sign;
                            var position = p;
                            if (radius > 0)
                            {
                                var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                                var outward = p - core;
                                if (outward.sqrMagnitude > 1e-12f)
                                {
                                    n = outward.normalized;
                                    position = core + n * radius;
                                }
                            }
                            Vector2 uv;
                            if (top && topUv is Rect r)
                            {
                                var f = FaceUv(markFace, p, h);
                                uv = new Vector2(Mathf.Lerp(r.xMin, r.xMax, f.x), Mathf.Lerp(r.yMin, r.yMax, f.y));
                            }
                            else
                                uv = new Vector2(position[ua], position[va]) * uvPerMm;
                            index[i, j] = Vertex(centre + position, n, uv);
                        }
                    }
                    for (int i = 0; i + 1 < us.Length; i++)
                        for (int j = 0; j + 1 < vs.Length; j++)
                            Quad(faceSlot, index[i, j], index[i + 1, j], index[i + 1, j + 1], index[i, j + 1]);
                }
            }
        }

        static readonly int[] Signs = { -1, 1 };

        /// <summary>Where a point of a face is on it from 0 to 1, left to right and bottom to top as seen from outside.</summary>
        static Vector2 FaceUv(Face face, Vector3 p, Vector3 h)
        {
            float U(float value, float half) => (value + half) / (2 * half);
            return face switch
            {
                Face.PlusY => new Vector2(U(p.x, h.x), U(p.z, h.z)),
                Face.MinusY => new Vector2(U(p.x, h.x), U(-p.z, h.z)),
                Face.MinusZ => new Vector2(U(p.x, h.x), U(p.y, h.y)),
                Face.PlusZ => new Vector2(U(-p.x, h.x), U(p.y, h.y)),
                Face.MinusX => new Vector2(U(-p.z, h.z), U(p.y, h.y)),
                _ => new Vector2(U(p.z, h.z), U(p.y, h.y)),
            };
        }

        static float[] Lines(float half, float radius) =>
            radius > 0 && radius < half * 0.999f ? new[] { -half, -half + radius, half - radius, half } : new[] { -half, half };

        // ------------------------------------------------------------------ turned shapes

        /// <summary>
        /// A surface of revolution about <paramref name="axis"/> through <paramref name="origin"/>: the profile's x is the
        /// radius and y the height along the axis. Go from the bottom middle outward, up the side and back in on top,
        /// so normals face out. Corners sharper than <paramref name="crease"/> degrees stay sharp; the rest is smooth.
        /// </summary>
        public void Lathe(int slot, Vector3 origin, Vector3 axis, IList<Vector2> profile, int segments, float crease = 35,
            float startDegrees = 0, float sweepDegrees = 360, float uRepeat = 1, float vPerMm = 0.1f)
        {
            axis.Normalize();
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            int count = profile.Count;
            var segmentNormals = new Vector2[count - 1];
            for (int i = 0; i + 1 < count; i++)
            {
                var t = profile[i + 1] - profile[i];
                segmentNormals[i] = t.sqrMagnitude < 1e-12f ? Vector2.zero : new Vector2(t.y, -t.x).normalized;
            }
            float cosCrease = Mathf.Cos(crease * Mathf.Deg2Rad);
            float travelled = 0;
            for (int i = 0; i + 1 < count; i++)
            {
                var n0 = EndNormal(segmentNormals, i, i - 1, cosCrease);
                var n1 = EndNormal(segmentNormals, i, i + 1, cosCrease);
                if (segmentNormals[i] == Vector2.zero) continue;
                float length = Vector2.Distance(profile[i], profile[i + 1]);
                for (int k = 0; k <= segments; k++)
                {
                    float angle = (startDegrees + sweepDegrees * k / segments) * Mathf.Deg2Rad;
                    var radial = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    float s = uRepeat * k / segments;
                    int a = Vertex(origin + radial * profile[i].x + axis * profile[i].y, radial * n0.x + axis * n0.y, new Vector2(s, travelled * vPerMm));
                    Vertex(origin + radial * profile[i + 1].x + axis * profile[i + 1].y, radial * n1.x + axis * n1.y, new Vector2(s, (travelled + length) * vPerMm));
                    if (k > 0) Quad(slot, a - 2, a, a + 1, a - 1);
                }
                travelled += length;
            }
        }

        static Vector2 EndNormal(Vector2[] normals, int i, int neighbour, float cosCrease)
        {
            var own = normals[i];
            if (neighbour < 0 || neighbour >= normals.Length || normals[neighbour] == Vector2.zero) return own;
            return Vector2.Dot(own, normals[neighbour]) >= cosCrease ? (own + normals[neighbour]).normalized : own;
        }

        /// <summary>A cylinder from <paramref name="a"/> to <paramref name="b"/>, its rims rounded by <paramref name="bevel"/>.</summary>
        public void Cylinder(int slot, Vector3 a, Vector3 b, float radius, int segments = 24, float bevel = 0, bool caps = true)
        {
            float length = Vector3.Distance(a, b);
            if (length < 1e-5f) return;
            bevel = Mathf.Min(bevel, radius * 0.5f, length * 0.5f);
            var profile = new List<Vector2>();
            if (caps) profile.Add(new Vector2(0, 0));
            if (bevel > 0)
            {
                profile.Add(new Vector2(radius - bevel, 0));
                profile.Add(new Vector2(radius - bevel * 0.29f, bevel * 0.29f));
                profile.Add(new Vector2(radius, bevel));
                profile.Add(new Vector2(radius, length - bevel));
                profile.Add(new Vector2(radius - bevel * 0.29f, length - bevel * 0.29f));
                profile.Add(new Vector2(radius - bevel, length));
            }
            else
            {
                profile.Add(new Vector2(radius, 0));
                profile.Add(new Vector2(radius, length));
            }
            if (caps) profile.Add(new Vector2(0, length));
            Lathe(slot, a, b - a, profile, segments, bevel > 0 ? 50 : 35);
        }

        public void Sphere(int slot, Vector3 centre, float radius, int segments = 24)
        {
            var profile = new List<Vector2>();
            int rings = Mathf.Max(4, segments / 2);
            for (int i = 0; i <= rings; i++)
            {
                float t = Mathf.PI * i / rings - Mathf.PI / 2;
                profile.Add(new Vector2(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius));
            }
            Lathe(slot, centre, Vector3.up, profile, segments, 80);
        }

        // ------------------------------------------------------------------ extrusions

        /// <summary>
        /// An outline in the x-z plane (mm, either winding, no holes) pushed up from <paramref name="y0"/> to
        /// <paramref name="y1"/>: a top face on <paramref name="topSlot"/> with uv from <paramref name="topUv"/>
        /// (plan uv of 0.1 per mm when null), a bottom face and side walls smoothed where the outline bends less
        /// than <paramref name="crease"/> degrees.
        /// </summary>
        public void Extrude(IList<Vector2> outline, float y0, float y1, int topSlot, int sideSlot, int bottomSlot,
            System.Func<Vector2, Vector2>? topUv = null, float crease = 30, System.Func<Vector2, Vector2>? bottomUv = null)
        {
            var points = new List<Vector2>(outline);
            if (SignedArea(points) < 0) points.Reverse(); // counter-clockwise in (x, z)
            int n = points.Count;
            var triangles = Triangulate(points);
            if (topSlot >= 0) Cap(points, triangles, y1, Vector3.up, topSlot, topUv);
            if (bottomSlot >= 0) Cap(points, triangles, y0, Vector3.down, bottomSlot, bottomUv);
            if (sideSlot < 0) return;
            float cosCrease = Mathf.Cos(crease * Mathf.Deg2Rad);
            var edgeNormals = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var t = points[(i + 1) % n] - points[i];
                edgeNormals[i] = new Vector2(t.y, -t.x).normalized; // outward for a counter-clockwise outline
            }
            float along = 0;
            for (int i = 0; i < n; i++)
            {
                var p0 = points[i];
                var p1 = points[(i + 1) % n];
                var own = edgeNormals[i];
                var before = edgeNormals[(i + n - 1) % n];
                var after = edgeNormals[(i + 1) % n];
                var n0 = Vector2.Dot(own, before) >= cosCrease ? (own + before).normalized : own;
                var n1 = Vector2.Dot(own, after) >= cosCrease ? (own + after).normalized : own;
                float length = Vector2.Distance(p0, p1);
                int a = Vertex(new Vector3(p0.x, y0, p0.y), new Vector3(n0.x, 0, n0.y), new Vector2(along, y0) * 0.1f);
                int b = Vertex(new Vector3(p1.x, y0, p1.y), new Vector3(n1.x, 0, n1.y), new Vector2(along + length, y0) * 0.1f);
                int c = Vertex(new Vector3(p1.x, y1, p1.y), new Vector3(n1.x, 0, n1.y), new Vector2(along + length, y1) * 0.1f);
                int d = Vertex(new Vector3(p0.x, y1, p0.y), new Vector3(n0.x, 0, n0.y), new Vector2(along, y1) * 0.1f);
                Quad(sideSlot, a, b, c, d);
                along += length;
            }
        }

        void Cap(List<Vector2> points, List<int> triangles, float y, Vector3 normal, int slot, System.Func<Vector2, Vector2>? uv)
        {
            int start = VertexCount;
            foreach (var p in points) Vertex(new Vector3(p.x, y, p.y), normal, uv?.Invoke(p) ?? p * 0.1f);
            for (int i = 0; i < triangles.Count; i += 3) Triangle(slot, start + triangles[i], start + triangles[i + 1], start + triangles[i + 2]);
        }

        static float SignedArea(List<Vector2> points)
        {
            float area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area / 2;
        }

        /// <summary>Ear clipping of a simple counter-clockwise polygon; indices into the list, three per triangle.</summary>
        static List<int> Triangulate(List<Vector2> points)
        {
            var result = new List<int>();
            var remaining = new List<int>();
            for (int i = 0; i < points.Count; i++) remaining.Add(i);
            int guard = points.Count * points.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ia = remaining[(i + remaining.Count - 1) % remaining.Count], ib = remaining[i], ic = remaining[(i + 1) % remaining.Count];
                    Vector2 a = points[ia], b = points[ib], c = points[ic];
                    if (Cross(b - a, c - b) <= 1e-9f) continue; // a reflex corner is no ear
                    bool empty = true;
                    foreach (int k in remaining)
                    {
                        if (k == ia || k == ib || k == ic) continue;
                        if (Inside(points[k], a, b, c))
                        {
                            empty = false;
                            break;
                        }
                    }
                    if (!empty) continue;
                    result.Add(ia);
                    result.Add(ib);
                    result.Add(ic);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // not simple: give up on the rest
            }
            if (remaining.Count == 3)
            {
                result.Add(remaining[0]);
                result.Add(remaining[1]);
                result.Add(remaining[2]);
            }
            return result;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;

        // ------------------------------------------------------------------ tubes

        /// <summary>A round tube along a path (a lead, a cable), <paramref name="sides"/> faces round.</summary>
        public void Sweep(int slot, IList<Vector3> path, float radius, int sides = 8, bool caps = true)
        {
            if (path.Count < 2) return;
            var up = Vector3.up;
            int previous = -1;
            float along = 0;
            var ring = new Vector3[sides];
            Vector3 side0 = Vector3.zero;
            for (int i = 0; i < path.Count; i++)
            {
                var tangent = (i == 0 ? path[1] - path[0] : i == path.Count - 1 ? path[i] - path[i - 1] : path[i + 1] - path[i - 1]).normalized;
                if (i == 0)
                {
                    side0 = Vector3.Cross(tangent, Mathf.Abs(Vector3.Dot(tangent, up)) > 0.9f ? Vector3.right : up).normalized;
                }
                else
                {
                    side0 = Vector3.ProjectOnPlane(side0, tangent).normalized; // carried along without twisting
                    along += Vector3.Distance(path[i], path[i - 1]);
                }
                var side1 = Vector3.Cross(tangent, side0);
                int start = VertexCount;
                for (int k = 0; k <= sides; k++)
                {
                    float angle = Mathf.PI * 2 * k / sides;
                    var normal = side0 * Mathf.Cos(angle) + side1 * Mathf.Sin(angle);
                    Vertex(path[i] + normal * radius, normal, new Vector2((float)k / sides, along * 0.1f));
                }
                if (previous >= 0)
                    for (int k = 0; k < sides; k++)
                        Quad(slot, previous + k, previous + k + 1, start + k + 1, start + k);
                previous = start;
                if (caps && (i == 0 || i == path.Count - 1))
                {
                    var outward = i == 0 ? -tangent : tangent;
                    int centre = Vertex(path[i], outward, new Vector2(0.5f, 0.5f));
                    int rim = VertexCount;
                    for (int k = 0; k <= sides; k++)
                    {
                        float angle = Mathf.PI * 2 * k / sides;
                        Vertex(path[i] + (side0 * Mathf.Cos(angle) + side1 * Mathf.Sin(angle)) * radius, outward, new Vector2(0.5f, 0.5f));
                    }
                    for (int k = 0; k < sides; k++) Triangle(slot, centre, rim + k, rim + k + 1);
                }
            }
        }

        // ------------------------------------------------------------------ result

        /// <summary>The mesh in metres (millimetres × <paramref name="scale"/>), one submesh per slot.</summary>
        public Mesh ToMesh(string name, float scale = 0.001f)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            var scaled = new List<Vector3>(vertices.Count);
            foreach (var v in vertices) scaled.Add(v * scale);
            mesh.SetVertices(scaled);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = slots.Count;
            for (int i = 0; i < slots.Count; i++) mesh.SetTriangles(slots[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
