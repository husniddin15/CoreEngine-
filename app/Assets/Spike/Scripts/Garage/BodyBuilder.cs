using System;
using System.Collections.Generic;
using System.IO;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>What Manifold made of a body, before Unity meshes exist; safe to make on a worker thread.</summary>
    public sealed class BodyData
    {
        internal MeshData Body = null!;                          // plates and Studio shapes, in the chassis frame (mm)
        internal readonly List<(string id, MeshData mesh, bool hole)> Features = new List<(string, MeshData, bool)>();
        internal readonly List<MeshData> Loose = new List<MeshData>(); // imported meshes that are not closed solids
        public double VolumeMm3;
        public int Holes;
        public float[] StlPositions = Array.Empty<float>();
        public int[] StlTriangles = Array.Empty<int>();
        public double BuildMs;
        public readonly List<string> NotClosed = new List<string>(); // ids of imported shapes shown as they are
    }

    /// <summary>The body as Unity meshes, with what the Body Studio shows and exports.</summary>
    public sealed class BodyMeshes
    {
        public Mesh Body = null!;                                 // in the chassis frame, metres
        public readonly List<(string id, Mesh mesh, bool hole)> Features = new List<(string, Mesh, bool)>();
        public readonly List<Mesh> Loose = new List<Mesh>();
        public double VolumeMm3;
        public int Holes;
        public float[] StlPositions = Array.Empty<float>();      // millimetres, for STL export
        public int[] StlTriangles = Array.Empty<int>();
        public double BuildMs;
        public List<string> NotClosed = new List<string>();

        public void Destroy()
        {
            UnityEngine.Object.Destroy(Body);
            foreach (var feature in Features) UnityEngine.Object.Destroy(feature.mesh);
            foreach (var mesh in Loose) UnityEngine.Object.Destroy(mesh);
        }
    }

    /// <summary>
    /// The Body Studio kernel (docs/08, ADR-0005): the chassis plates from a <see cref="BodyDesign"/> (outline,
    /// M3 hole grid clear of the edges and walls, side walls) joined with the Studio's solid shapes, and every
    /// hole shape cut through the lot, with Manifold booleans. Shapes: box, rounded box (hull of eight spheres),
    /// cylinder, cone, sphere, wedge, tube, an extruded outline and imported STL or OBJ meshes. Everything is
    /// built in the chassis frame, so the result sits on the robot as it is.
    /// </summary>
    public static class BodyBuilder
    {
        const float WallThickness = 3;
        const int Segments = 64;

        /// <summary>Manifold is used by one thread at a time: the Body Studio's worker or the main thread.</summary>
        static readonly object ManifoldLock = new object();

        /// <summary>Imported meshes by full path, centred and scaled to a 1 × 1 × 1 box (shapes scale them to their size).</summary>
        static readonly Dictionary<string, MeshFileData> Imports = new Dictionary<string, MeshFileData>();

        public static BodyMeshes Build(BodyDesign body, string importFolder) => ToMeshes(BuildData(body, importFolder));

        /// <summary>The Manifold part of a build: no Unity objects, so it may run on a worker thread.</summary>
        public static BodyData BuildData(BodyDesign body, string importFolder)
        {
            lock (ManifoldLock)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = new BodyData();
                var solids = new List<Solid>();
                var holes = new List<Solid>();
                try
                {
                    if (body.Decks >= 2)
                    {
                        using var top = Plate(body, walls: true, out int holeCount);
                        using var bottom = Plate(body, walls: false, out _);
                        solids.Add(top.Translate(0, DesignGeometry.TopDeckBottom, 0));
                        solids.Add(bottom.Translate(0, DesignGeometry.BottomPlateBottom(body), 0));
                        result.Holes = holeCount;
                    }
                    else
                    {
                        using var plate = Plate(body, walls: true, out int holeCount);
                        solids.Add(plate.Translate(0, DesignGeometry.BottomPlateBottom(body), 0));
                        result.Holes = holeCount;
                    }

                    foreach (var feature in body.Features)
                    {
                        var solid = FeatureSolid(feature, importFolder, out bool closed);
                        if (solid == null) continue;
                        if (!closed)
                        {
                            result.Loose.Add(solid.ToMeshData());
                            result.NotClosed.Add(feature.Id);
                            solid.Dispose();
                            continue;
                        }
                        result.Features.Add((feature.Id, solid.ToMeshData(), feature.Hole));
                        (feature.Hole ? holes : solids).Add(solid);
                    }

                    using var joined = Solid.Batch(solids, Native.OpAdd);
                    Solid final;
                    if (holes.Count > 0)
                    {
                        var cut = new List<Solid> { joined };
                        cut.AddRange(holes);
                        final = Solid.Batch(cut, Native.OpSubtract); // every hole cuts every solid, as in Tinkercad
                    }
                    else
                    {
                        final = joined.Translate(0, 0, 0);
                    }
                    using (final)
                    {
                        result.Body = final.ToMeshData();
                        result.VolumeMm3 = final.Volume;
                    }
                }
                finally
                {
                    foreach (var solid in solids) solid.Dispose();
                    foreach (var solid in holes) solid.Dispose();
                }

                // STL: the body and any open imported meshes, as they sit on the robot.
                var positions = new List<float>(result.Body.Positions());
                var triangles = new List<int>(result.Body.Triangles);
                foreach (var loose in result.Loose)
                {
                    int offset = positions.Count / 3;
                    positions.AddRange(loose.Positions());
                    foreach (int t in loose.Triangles) triangles.Add(t + offset);
                }
                result.StlPositions = positions.ToArray();
                result.StlTriangles = triangles.ToArray();
                result.BuildMs = watch.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        /// <summary>Unity meshes from Manifold's output (main thread), scaled from millimetres to metres.</summary>
        public static BodyMeshes ToMeshes(BodyData data)
        {
            var result = new BodyMeshes
            {
                VolumeMm3 = data.VolumeMm3,
                Holes = data.Holes,
                StlPositions = data.StlPositions,
                StlTriangles = data.StlTriangles,
                BuildMs = data.BuildMs,
                NotClosed = new List<string>(data.NotClosed),
            };
            result.Body = data.Body.ToUnityMesh(0.001f);
            result.Body.name = "Body";
            foreach (var (id, mesh, hole) in data.Features)
            {
                var unity = mesh.ToUnityMesh(0.001f);
                unity.name = "Feature " + id;
                result.Features.Add((id, unity, hole));
            }
            foreach (var loose in data.Loose)
            {
                var unity = loose.ToUnityMesh(0.001f);
                unity.name = "Imported";
                result.Loose.Add(unity);
            }
            return result;
        }

        // ------------------------------------------------------------------ Studio shapes

        /// <summary>One shape at its size, rotation and place in the chassis frame (mm); null when it cannot be made.</summary>
        static Solid? FeatureSolid(BodyFeature f, string importFolder, out bool closed)
        {
            closed = true;
            float sx = Math.Max(0.2f, f.SizeX), sy = Math.Max(0.2f, f.SizeY), sz = Math.Max(0.2f, f.SizeZ);
            Solid? shape = null;
            switch (f.Kind)
            {
                case FeatureKind.Box:
                    shape = Solid.Cube(sx, sy, sz, 0, 0, 0);
                    break;
                case FeatureKind.RoundedBox:
                {
                    float r = Mathf.Clamp(f.Detail, 0.5f, Math.Min(sx, Math.Min(sy, sz)) / 2 - 0.05f);
                    var corners = new List<Solid>();
                    foreach (float x in new[] { -1f, 1f })
                        foreach (float y in new[] { -1f, 1f })
                            foreach (float z in new[] { -1f, 1f })
                            {
                                using var ball = Solid.Sphere(r, 24);
                                corners.Add(ball.Translate(x * (sx / 2 - r), y * (sy / 2 - r), z * (sz / 2 - r)));
                            }
                    shape = Solid.Hull(corners);
                    foreach (var c in corners) c.Dispose();
                    break;
                }
                case FeatureKind.Cylinder:
                case FeatureKind.Cone:
                {
                    double top = f.Kind == FeatureKind.Cone ? 0.5 * Mathf.Clamp01(f.Detail) : 0.5;
                    shape = Upright(Solid.Cone(1, 0.5, Math.Max(top, 1e-4), Segments), sx, sy, sz);
                    break;
                }
                case FeatureKind.Sphere:
                {
                    using var ball = Solid.Sphere(0.5, Segments);
                    shape = ball.Scale(sx, sy, sz);
                    break;
                }
                case FeatureKind.Wedge: // a ramp: flat bottom, upright back face, slope down to the front edge
                    shape = Solid.HullOf(new[]
                    {
                        new Vector3(-sx / 2, -sy / 2, -sz / 2), new Vector3(sx / 2, -sy / 2, -sz / 2),
                        new Vector3(-sx / 2, -sy / 2, sz / 2), new Vector3(sx / 2, -sy / 2, sz / 2),
                        new Vector3(-sx / 2, sy / 2, -sz / 2), new Vector3(sx / 2, sy / 2, -sz / 2),
                    });
                    break;
                case FeatureKind.Tube:
                {
                    float wall = Mathf.Clamp(f.Detail, 0.4f, Math.Min(sx, sz) / 2 - 0.2f);
                    using var outer = Upright(Solid.Cone(1, 0.5, 0.5, Segments), sx, sy, sz);
                    using var inner = Upright(Solid.Cone(1, 0.5, 0.5, Segments), sx - 2 * wall, sy + 2, sz - 2 * wall);
                    shape = outer.Minus(inner);
                    break;
                }
                case FeatureKind.Extrusion:
                    shape = Extrusion(f.Outline, sx, sy, sz);
                    break;
                case FeatureKind.Imported:
                {
                    var mesh = LoadImport(Path.Combine(importFolder, f.MeshFile));
                    if (mesh == null) return null;
                    var positions = new float[mesh.Positions.Length];
                    for (int i = 0; i < positions.Length; i += 3)
                    {
                        positions[i] = mesh.Positions[i] * sx;
                        positions[i + 1] = mesh.Positions[i + 1] * sy;
                        positions[i + 2] = mesh.Positions[i + 2] * sz;
                    }
                    shape = Solid.FromMesh(positions, mesh.Triangles, out closed);
                    break;
                }
            }
            if (shape == null) return null;
            using (shape)
                return shape.Transform(Matrix4x4.TRS(new Vector3(f.X, f.Y, f.Z), Quaternion.Euler(f.RotX, f.RotY, f.RotZ), Vector3.one));
        }

        /// <summary>A unit shape made along +z (Manifold's cylinders), stood up along +y and scaled to its size.</summary>
        static Solid Upright(Solid alongZ, float sx, float sy, float sz)
        {
            using (alongZ)
            using (var up = alongZ.Rotate(-90, 0, 0)) // (x, y, z) -> (x, z, -y)
                return up.Scale(sx, sy, sz);
        }

        /// <summary>
        /// The outline (x, z pairs of a 1 × 1 square) scaled to the size and extruded upward. Manifold extrudes an xy
        /// polygon along +z; turning -90° about x maps (x, y, z) to (x, z, -y), so the outline goes in as (x, -z).
        /// </summary>
        static Solid? Extrusion(List<float> outline, float sx, float sy, float sz)
        {
            int n = outline.Count / 2;
            if (n < 3) return null;
            var polygon = new List<Vector2>(n);
            for (int i = 0; i < n; i++) polygon.Add(new Vector2(outline[2 * i] * sx, -outline[2 * i + 1] * sz));
            double area = 0;
            for (int i = 0; i < n; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            if (Math.Abs(area) < 1e-6) return null;
            if (area < 0) polygon.Reverse(); // Manifold fills counter-clockwise outlines
            using var extruded = Solid.Extrude(polygon, sy);
            using var centred = extruded.Translate(0, 0, -sy / 2);
            return centred.Rotate(-90, 0, 0);
        }

        /// <summary>An imported file, centred on its bounds and scaled to a 1 × 1 × 1 box; cached by path.</summary>
        static MeshFileData? LoadImport(string path)
        {
            if (Imports.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path)) return null;
            var mesh = MeshFile.Read(path);
            var min = mesh.Min;
            var max = mesh.Max;
            float cx = (min.x + max.x) / 2, cy = (min.y + max.y) / 2, cz = (min.z + max.z) / 2;
            float wx = Math.Max(1e-6f, max.x - min.x), wy = Math.Max(1e-6f, max.y - min.y), wz = Math.Max(1e-6f, max.z - min.z);
            for (int i = 0; i < mesh.Positions.Length; i += 3)
            {
                mesh.Positions[i] = (mesh.Positions[i] - cx) / wx;
                mesh.Positions[i + 1] = (mesh.Positions[i + 1] - cy) / wy;
                mesh.Positions[i + 2] = (mesh.Positions[i + 2] - cz) / wz;
            }
            Imports[path] = mesh;
            return mesh;
        }

        /// <summary>Reads a model file for a new imported shape: its size in the file's units and its triangles.</summary>
        public static MeshFileData ReadModel(string path)
        {
            lock (ManifoldLock) return MeshFile.Read(path);
        }

        // ------------------------------------------------------------------ plates

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
