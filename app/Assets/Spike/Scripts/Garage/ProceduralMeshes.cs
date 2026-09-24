using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Smooth meshes made in code, in metres: tubes along curves (jumper wires and leads) and shapes turned
    /// about the x axis like on a lathe (tyres and hubs). Unity's primitive cylinder has 20 sides, which shows
    /// as flat facets on a 65 mm wheel; these have 64 or more.
    /// </summary>
    public static class ProceduralMeshes
    {
        static Mesh? tyre, hub;

        /// <summary>The TT wheel's tyre: 65 mm across, 26 mm wide, 42 mm inside, with rounded shoulders.</summary>
        public static Mesh Tyre => tyre ??= MakeTyre();

        /// <summary>The wheel's hub: a disc 42 mm across and 24 mm wide.</summary>
        public static Mesh Hub => hub ??= Lathe(new[]
        {
            new[] { new Vector2(-0.012f, 0), new Vector2(-0.012f, 0.021f) },
            new[] { new Vector2(-0.012f, 0.021f), new Vector2(0.012f, 0.021f) },
            new[] { new Vector2(0.012f, 0.021f), new Vector2(0.012f, 0) },
        }, new[] { false, false, false }, 64, "Hub");

        static Mesh? arrow, ring, square;

        /// <summary>The Body Studio's move handle: an arrow one unit long along +x, a thin shaft and a cone tip.</summary>
        public static Mesh Arrow => arrow ??= Lathe(new[]
        {
            new[] { new Vector2(0.14f, 0), new Vector2(0.14f, 0.016f) },
            new[] { new Vector2(0.14f, 0.016f), new Vector2(0.76f, 0.016f) },
            new[] { new Vector2(0.76f, 0.016f), new Vector2(0.76f, 0.06f) },
            new[] { new Vector2(0.76f, 0.06f), new Vector2(1f, 0) },
        }, new[] { false, false, false, false }, 24, "GizmoArrow");

        /// <summary>The Body Studio's turn handle: a thin ring of radius 1 about the x axis.</summary>
        public static Mesh Ring => ring ??= MakeRing();

        /// <summary>A flat square in the xz plane, 0.4 m across, facing up (the Body Studio's grid).</summary>
        public static Mesh Square => square ??= Build(
            new List<Vector3> { new Vector3(-0.2f, 0, -0.2f), new Vector3(-0.2f, 0, 0.2f), new Vector3(0.2f, 0, 0.2f), new Vector3(0.2f, 0, -0.2f) },
            new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
            new List<int> { 0, 1, 2, 0, 2, 3 }, "StudioGrid");

        static Mesh MakeRing()
        {
            const int points = 10;
            const float radius = 0.014f;
            var loop = new Vector2[points];
            for (int i = 0; i < points; i++)
            {
                float theta = -2 * Mathf.PI * i / points;
                loop[i] = new Vector2(radius * Mathf.Cos(theta), 1f + radius * Mathf.Sin(theta));
            }
            return Lathe(new[] { loop }, new[] { true }, 96, "GizmoRing");
        }

        static Mesh MakeTyre()
        {
            // The cross-section is a superellipse (squarish with round corners), traversed so that the
            // outward normal (-dr, dx) points away from the rubber.
            const int points = 40;
            const float centreRadius = 0.02675f, halfThickness = 0.00575f, halfWidth = 0.013f, power = 0.45f;
            var loop = new Vector2[points];
            for (int i = 0; i < points; i++)
            {
                float theta = -2 * Mathf.PI * i / points;
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                float x = halfWidth * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power);
                float r = centreRadius + halfThickness * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power);
                loop[i] = new Vector2(x, r);
            }
            return Lathe(new[] { loop }, new[] { true }, 96, "Tyre");
        }

        /// <summary>
        /// Turns profiles about the x axis. Each strip is a polyline of (x, radius) points; normals are smooth along a
        /// strip and hard between strips. A closed strip joins its last point to its first.
        /// </summary>
        public static Mesh Lathe(IReadOnlyList<Vector2[]> strips, IReadOnlyList<bool> closed, int segments, string name)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int k = 0; k < strips.Count; k++)
            {
                var strip = strips[k];
                bool loop = closed[k];
                int count = strip.Length + (loop ? 1 : 0);
                Vector2 At(int j) => strip[((j % strip.Length) + strip.Length) % strip.Length];
                var profile = new Vector2[count];
                var normal = new Vector2[count];
                for (int j = 0; j < count; j++)
                {
                    profile[j] = At(j);
                    Vector2 d = loop ? At(j + 1) - At(j - 1) : strip[Math.Min(j + 1, strip.Length - 1)] - strip[Math.Max(j - 1, 0)];
                    normal[j] = new Vector2(-d.y, d.x).normalized; // (dx, dr) turned to (-dr, dx): outward
                }
                int start = vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float phi = i * 2 * Mathf.PI / segments;
                    float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
                    for (int j = 0; j < count; j++)
                    {
                        vertices.Add(new Vector3(profile[j].x, profile[j].y * c, profile[j].y * s));
                        normals.Add(new Vector3(normal[j].x, normal[j].y * c, normal[j].y * s));
                    }
                }
                for (int i = 0; i < segments; i++)
                {
                    for (int j = 0; j < count - 1; j++)
                    {
                        int v00 = start + i * count + j, v10 = v00 + 1, v01 = v00 + count, v11 = v01 + 1;
                        triangles.Add(v00);
                        triangles.Add(v01);
                        triangles.Add(v10);
                        triangles.Add(v10);
                        triangles.Add(v01);
                        triangles.Add(v11);
                    }
                }
            }
            return Build(vertices, normals, triangles, name);
        }

        /// <summary>A round tube along a polyline, with rings that follow the curve without twisting (a jumper wire).</summary>
        public static Mesh Tube(IReadOnlyList<Vector3> points, float radius, int sides)
        {
            int rings = points.Count;
            var vertices = new List<Vector3>(rings * (sides + 1));
            var normals = new List<Vector3>(rings * (sides + 1));
            var triangles = new List<int>((rings - 1) * sides * 6);
            Vector3 tangent = (points[1] - points[0]).normalized;
            Vector3 side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            for (int i = 0; i < rings; i++)
            {
                Vector3 along = i == 0 ? points[1] - points[0]
                    : i == rings - 1 ? points[i] - points[i - 1]
                    : points[i + 1] - points[i - 1];
                Vector3 t = along.sqrMagnitude > 1e-12f ? along.normalized : tangent;
                side = (Quaternion.FromToRotation(tangent, t) * side).normalized; // parallel transport
                tangent = t;
                Vector3 up = Vector3.Cross(t, side);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * 2 * Mathf.PI / sides;
                    Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    vertices.Add(points[i] + n * radius);
                    normals.Add(n);
                }
            }
            for (int i = 0; i < rings - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i * (sides + 1) + s, b = a + sides + 1;
                    triangles.Add(a);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(b + 1);
                }
            }
            return Build(vertices, normals, triangles, "Tube");
        }

        static readonly Dictionary<(int, int, int, int), Mesh> roundedBoxes = new Dictionary<(int, int, int, int), Mesh>();

        /// <summary>
        /// A box with rounded edges and corners, centred, in metres; one mesh per size, kept for reuse. Sharp edges
        /// catch no light, which is what makes simple shapes look drawn; a real board or plug has a small round.
        /// Each face is a grid whose outer rows follow the rounding at even angles.
        /// </summary>
        public static Mesh RoundedBox(Vector3 size, float radius)
        {
            var key = ((int)Mathf.Round(size.x * 1e5f), (int)Mathf.Round(size.y * 1e5f), (int)Mathf.Round(size.z * 1e5f), (int)Mathf.Round(radius * 1e6f));
            if (roundedBoxes.TryGetValue(key, out var cached) && cached != null) return cached;
            var half = size / 2;
            radius = Mathf.Min(radius, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.999f);
            var inner = half - Vector3.one * radius;
            const int steps = 3;
            float[] Axis(float h, float r)
            {
                // x = (h - r) + r tan(phi): the points land on the rounding at even angles up to 45°.
                var list = new List<float>();
                for (int k = steps; k >= 1; k--) list.Add(-((h - r) + r * Mathf.Tan(Mathf.PI / 4 * k / steps)));
                list.Add(-(h - r));
                list.Add(h - r);
                for (int k = 1; k <= steps; k++) list.Add((h - r) + r * Mathf.Tan(Mathf.PI / 4 * k / steps));
                return list.ToArray();
            }
            var coords = new[] { Axis(half.x, radius), Axis(half.y, radius), Axis(half.z, radius) };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            // (normal axis, sign, u axis, v axis), chosen so that cross(v, u) points out of the face.
            var faces = new[] { (1, 1, 0, 2), (1, -1, 2, 0), (0, 1, 2, 1), (0, -1, 1, 2), (2, 1, 1, 0), (2, -1, 0, 1) };
            foreach (var (n, sign, u, v) in faces)
            {
                int start = vertices.Count;
                var us = coords[u];
                var vs = coords[v];
                for (int i = 0; i < us.Length; i++)
                {
                    for (int j = 0; j < vs.Length; j++)
                    {
                        var p = Vector3.zero;
                        p[n] = sign * half[n];
                        p[u] = us[i];
                        p[v] = vs[j];
                        var clamped = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        var d = p - clamped;
                        Vector3 normal;
                        if (d.sqrMagnitude > 1e-14f)
                        {
                            normal = d.normalized;
                            p = clamped + normal * radius;
                        }
                        else
                        {
                            normal = Vector3.zero;
                            normal[n] = sign;
                        }
                        vertices.Add(p);
                        normals.Add(normal);
                    }
                }
                int rows = vs.Length;
                for (int i = 0; i + 1 < us.Length; i++)
                {
                    for (int j = 0; j + 1 < vs.Length; j++)
                    {
                        int a = start + i * rows + j;
                        triangles.Add(a);
                        triangles.Add(a + 1);
                        triangles.Add(a + rows);
                        triangles.Add(a + rows);
                        triangles.Add(a + 1);
                        triangles.Add(a + rows + 1);
                    }
                }
            }
            var mesh = Build(vertices, normals, triangles, "RoundedBox");
            roundedBoxes[key] = mesh;
            return mesh;
        }

        static Mesh Build(List<Vector3> vertices, List<Vector3> normals, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
