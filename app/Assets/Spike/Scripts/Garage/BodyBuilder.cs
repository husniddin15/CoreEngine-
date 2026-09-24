using System;
using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>What Manifold made of a body, before Unity meshes exist; safe to make on a worker thread.</summary>
    public sealed class BodyData
    {
        internal MeshData Top = null!;
        internal MeshData? Bottom;
        public double VolumeMm3;
        public int Holes;
        public float[] StlPositions = Array.Empty<float>();
        public int[] StlTriangles = Array.Empty<int>();
        public double BuildMs;
    }

    /// <summary>The chassis plates as meshes, with what the Body Studio shows and exports.</summary>
    public sealed class BodyMeshes
    {
        public Mesh Top = null!;       // the deck the parts stand on (with the walls); bottom at y = 0, metres
        public Mesh? Bottom;           // the lower plate when there are two decks
        public double VolumeMm3;       // every plate
        public int Holes;              // per plate
        public float[] StlPositions = Array.Empty<float>(); // every plate laid out side by side, millimetres
        public int[] StlTriangles = Array.Empty<int>();
        public double BuildMs;

        public void Destroy()
        {
            UnityEngine.Object.Destroy(Top);
            if (Bottom != null) UnityEngine.Object.Destroy(Bottom);
        }
    }

    /// <summary>
    /// First Body Studio kernel (docs/08, ADR-0005): a plate from a <see cref="BodyDesign"/> with Manifold
    /// booleans. The outline is a box, a hull of four corner cylinders (rounded) or a disc; the M3 hole grid
    /// keeps clear of the edges and walls; walls stand on the top deck's sides.
    /// </summary>
    public static class BodyBuilder
    {
        const float WallThickness = 3;

        /// <summary>Manifold is used by one thread at a time: the Body Studio's worker or the main thread.</summary>
        static readonly object ManifoldLock = new object();

        public static BodyMeshes Build(BodyDesign body) => ToMeshes(BuildData(body));

        /// <summary>The Manifold part of a build: no Unity calls, so it may run on a worker thread.</summary>
        public static BodyData BuildData(BodyDesign body)
        {
            lock (ManifoldLock)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = new BodyData();
                using var top = Plate(body, walls: true, out int holes);
                result.Top = top.ToMeshData();
                result.VolumeMm3 = top.Volume;
                result.Holes = holes;
                var positions = new List<float>(result.Top.Positions());
                var triangles = new List<int>(result.Top.Triangles);
                if (body.Decks >= 2)
                {
                    using var bottom = Plate(body, walls: false, out _);
                    result.Bottom = bottom.ToMeshData();
                    result.VolumeMm3 += bottom.Volume;
                    // Lay the second plate beside the first for printing or laser cutting.
                    int offset = positions.Count / 3;
                    float[] p = result.Bottom.Positions();
                    for (int i = 0; i < p.Length; i += 3)
                    {
                        positions.Add(p[i] + body.WidthMm + 10);
                        positions.Add(p[i + 1]);
                        positions.Add(p[i + 2]);
                    }
                    foreach (int t in result.Bottom.Triangles) triangles.Add(t + offset);
                }
                result.StlPositions = positions.ToArray();
                result.StlTriangles = triangles.ToArray();
                result.BuildMs = watch.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        /// <summary>Unity meshes from Manifold's output (main thread).</summary>
        public static BodyMeshes ToMeshes(BodyData data)
        {
            var result = new BodyMeshes
            {
                VolumeMm3 = data.VolumeMm3,
                Holes = data.Holes,
                StlPositions = data.StlPositions,
                StlTriangles = data.StlTriangles,
                BuildMs = data.BuildMs,
            };
            result.Top = data.Top.ToUnityMesh(0.001f);
            result.Top.name = "BodyTop";
            if (data.Bottom != null)
            {
                result.Bottom = data.Bottom.ToUnityMesh(0.001f);
                result.Bottom.name = "BodyBottom";
            }
            return result;
        }

        static Solid Plate(BodyDesign b, bool walls, out int holeCount)
        {
            float t = b.ThicknessMm;
            var pieces = new List<Solid> { Outline(b, t) };
            bool hasWalls = walls && b.WallHeightMm > 0 && b.Shape != BodyShape.Round;
            if (hasWalls)
            {
                float inset = b.Shape == BodyShape.Rounded ? Radius(b) : 0;
                float length = b.EffectiveLength - 2 * inset;
                float height = t + b.WallHeightMm;
                foreach (float side in new[] { -1f, 1f })
                    pieces.Add(Solid.Cube(WallThickness, height, length, side * (b.WidthMm / 2 - WallThickness / 2), height / 2, 0));
            }
            // Batch operations copy their inputs (Manifold shares the nodes), so the pieces can be freed at once.
            var plate = pieces.Count == 1 ? pieces[0] : Solid.Batch(pieces, Native.OpAdd);
            if (pieces.Count > 1) foreach (var piece in pieces) piece.Dispose();

            var holes = new List<Solid>();
            if (b.HoleGrid && b.HolePitchMm >= 5)
            {
                float r = b.HoleDiameterMm / 2;
                float margin = r + 3; // at least 3 mm of material to the edge
                float wallClear = hasWalls ? WallThickness + r + 2 : 0;
                int segments = Math.Max(8, Native.manifold_get_circular_segments(r));
                int nx = (int)(b.WidthMm / 2 / b.HolePitchMm), nz = (int)(b.EffectiveLength / 2 / b.HolePitchMm);
                for (int i = -nx; i <= nx; i++)
                {
                    for (int j = -nz; j <= nz; j++)
                    {
                        float x = i * b.HolePitchMm, z = j * b.HolePitchMm;
                        if (!Inside(b, x, z, margin)) continue;
                        if (hasWalls && Math.Abs(x) > b.WidthMm / 2 - wallClear) continue;
                        using var cylinder = Solid.Cylinder(t + 2, r, segments);
                        using var upright = cylinder.Rotate(90, 0, 0);
                        holes.Add(upright.Translate(x, t / 2, z));
                    }
                }
            }
            holeCount = holes.Count;
            if (holes.Count == 0) return plate;
            var cut = new List<Solid> { plate };
            cut.AddRange(holes);
            var result = Solid.Batch(cut, Native.OpSubtract); // the plate minus every hole
            foreach (var solid in cut) solid.Dispose();
            return result;
        }

        static float Radius(BodyDesign b) => Mathf.Clamp(b.CornerRadiusMm, 1, Math.Min(b.WidthMm, b.EffectiveLength) / 2 - 0.5f);

        static Solid Outline(BodyDesign b, float height)
        {
            switch (b.Shape)
            {
                case BodyShape.Round:
                {
                    using var disc = Solid.Cylinder(height, b.WidthMm / 2, 96);
                    using var upright = disc.Rotate(90, 0, 0);
                    return upright.Translate(0, height / 2, 0);
                }
                case BodyShape.Rounded:
                {
                    float r = Radius(b), x = b.WidthMm / 2 - r, z = b.EffectiveLength / 2 - r;
                    int segments = Math.Max(16, Native.manifold_get_circular_segments(r));
                    var corners = new List<Solid>();
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        foreach (float sz in new[] { -1f, 1f })
                        {
                            using var cylinder = Solid.Cylinder(height, r, segments);
                            using var upright = cylinder.Rotate(90, 0, 0);
                            corners.Add(upright.Translate(sx * x, height / 2, sz * z));
                        }
                    }
                    var hull = Solid.Hull(corners);
                    foreach (var c in corners) c.Dispose();
                    return hull;
                }
                default:
                    return Solid.Cube(b.WidthMm, height, b.EffectiveLength, 0, height / 2, 0);
            }
        }

        /// <summary>True when a circle of radius <paramref name="margin"/> at (x, z) lies inside the outline.</summary>
        static bool Inside(BodyDesign b, float x, float z, float margin)
        {
            float hw = b.WidthMm / 2, hl = b.EffectiveLength / 2;
            switch (b.Shape)
            {
                case BodyShape.Round:
                    return x * x + z * z <= (hw - margin) * (hw - margin);
                case BodyShape.Rounded:
                {
                    float r = Radius(b);
                    if (Math.Abs(x) > hw - margin || Math.Abs(z) > hl - margin) return false;
                    float cx = Math.Abs(x) - (hw - r), cz = Math.Abs(z) - (hl - r);
                    return cx <= 0 || cz <= 0 || cx * cx + cz * cz <= (r - margin) * (r - margin);
                }
                default:
                    return Math.Abs(x) <= hw - margin && Math.Abs(z) <= hl - margin;
            }
        }
    }
}
