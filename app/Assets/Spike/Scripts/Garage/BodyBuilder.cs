using System;
using System.Collections.Generic;
using System.IO;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>One solid piece of the body: a top-level shape or group, in one material and colour.</summary>
    public sealed class BodySolid
    {
        public string Id = "";              // the top-level shape or group it comes from
        public BodyMaterial Material;
        public string Colour = "";
        public Mesh Mesh = null!;           // chassis frame, metres
        public double VolumeMm3;
    }

    /// <summary>What Manifold made of a body, before Unity meshes exist; safe to make on a worker thread.</summary>
    public sealed class BodyData
    {
        internal readonly List<(string id, BodyMaterial material, string colour, MeshData mesh, double volume)> Solids = new List<(string, BodyMaterial, string, MeshData, double)>();
        internal readonly List<(string id, MeshData mesh, bool hole)> Features = new List<(string, MeshData, bool)>(); // every shape's own mesh
        internal readonly List<MeshData> Loose = new List<MeshData>();  // imported meshes that are not closed solids
        public readonly List<string> NotClosed = new List<string>();    // their ids, in the same order
        public double VolumeMm3, MassG;
        public float[] StlPositions = Array.Empty<float>();
        public int[] StlTriangles = Array.Empty<int>();
        public double BuildMs;
        public BodyDesign Source = null!;                               // the design snapshot this was built from
    }

    /// <summary>The body as Unity meshes, with what the Body Studio shows and exports.</summary>
    public sealed class BodyMeshes
    {
        public readonly List<BodySolid> Solids = new List<BodySolid>();
        public readonly List<(string id, Mesh mesh, bool hole)> Features = new List<(string, Mesh, bool)>();
        public readonly List<Mesh> Loose = new List<Mesh>();
        public List<string> NotClosed = new List<string>();             // the ids of the Loose meshes, in the same order
        public double VolumeMm3, MassG;
        public float[] StlPositions = Array.Empty<float>();             // millimetres, for STL export
        public int[] StlTriangles = Array.Empty<int>();
        public double BuildMs;
        public BodyDesign Source = null!;

        public void Destroy()
        {
            foreach (var solid in Solids) UnityEngine.Object.Destroy(solid.Mesh);
            foreach (var feature in Features) UnityEngine.Object.Destroy(feature.mesh);
            foreach (var mesh in Loose) UnityEngine.Object.Destroy(mesh);
        }
    }

    /// <summary>
    /// The Body Studio kernel (docs/08, ADR-0005). Every shape is made at its size by Manifold and placed in the
    /// chassis frame. As in Tinkercad, a group joins its solids and cuts its holes out of them; a shape or hole on
    /// its own stays as it is. Each top-level shape or group becomes one solid per material and colour, so a
    /// cardboard plate and a PLA bracket keep their own look and weight. A perforated plate is a sheet with an
    /// M3 hole grid clear of its edges.
    /// </summary>
    public static class BodyBuilder
    {
        const int Segments = 64;
        const int CacheSize = 96;

        /// <summary>Manifold is used by one thread at a time: the Body Studio's worker or the main thread.</summary>
        static readonly object ManifoldLock = new object();

        /// <summary>
        /// Shapes at their size before they are placed, by recipe (kind, size and parameters, never place or turn):
        /// a shape dragged over the deck is not rebuilt, and a plate's hole grid (most of the work) is made once.
        /// </summary>
        static readonly Dictionary<string, Solid> recipes = new Dictionary<string, Solid>();
        static readonly List<string> recipeOrder = new List<string>();

        /// <summary>Imported meshes by full path, centred and scaled to a 1 × 1 × 1 box (shapes scale them to their size).</summary>
        static readonly Dictionary<string, MeshFileData> Imports = new Dictionary<string, MeshFileData>();

        public static BodyMeshes Build(BodyDesign body, string importFolder) => ToMeshes(BuildData(body.Clone(), importFolder));

        /// <summary>
        /// The Manifold part of a build: no Unity objects, so it may run on a worker thread. The body must not
        /// change meanwhile (pass a snapshot); it is kept as the result's <see cref="BodyData.Source"/>.
        /// </summary>
        public static BodyData BuildData(BodyDesign body, string importFolder)
        {
            lock (ManifoldLock)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = new BodyData { Source = body };
                var own = new Dictionary<string, Solid>();
                var made = new List<Solid>(); // everything built here, freed at the end
                try
                {
                    foreach (var feature in body.Features)
                    {
                        if (feature.Kind == FeatureKind.Group) continue;
                        var solid = PlacedShape(feature, importFolder, out bool closed);
                        if (!closed)
                        {
                            // Manifold cannot use an open mesh (it has no inside): show it as the file has it.
                            solid?.Dispose();
                            var open = OpenMesh(feature, importFolder);
                            if (open == null) continue;
                            result.Loose.Add(open);
                            result.NotClosed.Add(feature.Id);
                            continue;
                        }
                        if (solid == null) continue;
                        made.Add(solid);
                        own[feature.Id] = solid;
                        result.Features.Add((feature.Id, solid.ToMeshData(), feature.Hole));
                    }

                    foreach (var item in body.Members(null))
                    {
                        if (item.Hole) continue; // a hole on its own cuts nothing (Tinkercad's rule)
                        var pieces = item.Kind == FeatureKind.Group
                            ? GroupSolids(body, item, own, made)
                            : own.TryGetValue(item.Id, out var single) ? new Dictionary<(BodyMaterial, string), Solid> { [(item.Material, item.Colour)] = single } : null;
                        if (pieces == null) continue;
                        foreach (var piece in pieces)
                        {
                            double volume = piece.Value.Volume;
                            if (volume <= 1e-6) continue;
                            result.Solids.Add((item.Id, piece.Key.Item1, piece.Key.Item2, piece.Value.ToMeshData(), volume));
                            result.VolumeMm3 += volume;
                            result.MassG += volume / 1000.0 * BodyDesign.DensityGPerCm3(piece.Key.Item1);
                        }
                    }
                }
                finally
                {
                    foreach (var solid in made) solid.Dispose();
                }

                // STL: every solid piece and any open imported mesh, as they sit on the robot.
                var positions = new List<float>();
                var triangles = new List<int>();
                void Append(MeshData mesh)
                {
                    int offset = positions.Count / 3;
                    positions.AddRange(mesh.Positions());
                    foreach (int t in mesh.Triangles) triangles.Add(t + offset);
                }
                foreach (var solid in result.Solids) Append(solid.mesh);
                foreach (var loose in result.Loose) Append(loose);
                result.StlPositions = positions.ToArray();
                result.StlTriangles = triangles.ToArray();
                result.BuildMs = watch.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        /// <summary>
        /// A group's solids by material and colour, each with every hole of the group cut out. A group inside it
        /// counts as its solids, or as one hole when it is a hole group.
        /// </summary>
        static Dictionary<(BodyMaterial, string), Solid> GroupSolids(BodyDesign body, BodyFeature group, Dictionary<string, Solid> own, List<Solid> made)
        {
            var byLook = new Dictionary<(BodyMaterial, string), List<Solid>>();
            var holes = new List<Solid>();
            void AddSolid((BodyMaterial, string) look, Solid solid)
            {
                if (!byLook.TryGetValue(look, out var list)) byLook[look] = list = new List<Solid>();
                list.Add(solid);
            }
            foreach (var member in body.Members(group))
            {
                if (member.Kind == FeatureKind.Group)
                {
                    var inner = GroupSolids(body, member, own, made);
                    if (member.Hole)
                    {
                        if (inner.Count == 0) continue;
                        var joined = Solid.Batch(new List<Solid>(inner.Values), Native.OpAdd);
                        made.Add(joined);
                        holes.Add(joined);
                    }
                    else
                    {
                        foreach (var piece in inner) AddSolid(piece.Key, piece.Value);
                    }
                }
                else if (own.TryGetValue(member.Id, out var solid))
                {
                    if (member.Hole) holes.Add(solid);
                    else AddSolid((member.Material, member.Colour), solid);
                }
            }
            var result = new Dictionary<(BodyMaterial, string), Solid>();
            foreach (var look in byLook)
            {
                var joined = look.Value.Count == 1 ? look.Value[0] : Solid.Batch(look.Value, Native.OpAdd);
                if (look.Value.Count > 1) made.Add(joined);
                if (holes.Count > 0)
                {
                    var cut = new List<Solid> { joined };
                    cut.AddRange(holes);
                    joined = Solid.Batch(cut, Native.OpSubtract);
                    made.Add(joined);
                }
                result[look.Key] = joined;
            }
            return result;
        }

        /// <summary>Unity meshes from Manifold's output (main thread), scaled from millimetres to metres.</summary>
        public static BodyMeshes ToMeshes(BodyData data)
        {
            var result = new BodyMeshes
            {
                VolumeMm3 = data.VolumeMm3,
                MassG = data.MassG,
                StlPositions = data.StlPositions,
                StlTriangles = data.StlTriangles,
                BuildMs = data.BuildMs,
                NotClosed = new List<string>(data.NotClosed),
                Source = data.Source,
            };
            foreach (var (id, material, colour, mesh, volume) in data.Solids)
            {
                var unity = mesh.ToUnityMesh(0.001f, withUv: true);
                unity.name = "Body " + id;
                result.Solids.Add(new BodySolid { Id = id, Material = material, Colour = colour, Mesh = unity, VolumeMm3 = volume });
            }
            foreach (var (id, mesh, hole) in data.Features)
            {
                var unity = mesh.ToUnityMesh(0.001f);
                unity.name = "Feature " + id;
                result.Features.Add((id, unity, hole));
            }
            foreach (var loose in data.Loose)
            {
                var unity = loose.ToUnityMesh(0.001f, withUv: true);
                unity.name = "Imported";
                result.Loose.Add(unity);
            }
            return result;
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>One shape at its size, turned and placed in the chassis frame (mm); null when it cannot be made.</summary>
        static Solid? PlacedShape(BodyFeature f, string importFolder, out bool closed)
        {
            var local = ShapeAtSize(f, importFolder, out closed);
            if (local == null) return null;
            return local.Transform(Matrix4x4.TRS(new Vector3(f.X, f.Y, f.Z), Quaternion.Euler(f.RotX, f.RotY, f.RotZ), Vector3.one));
        }

        /// <summary>The shape at its size about its own centre, from the recipe cache; the cache owns it.</summary>
        static Solid? ShapeAtSize(BodyFeature f, string importFolder, out bool closed)
        {
            closed = true;
            string key = Recipe(f, importFolder);
            if (recipes.TryGetValue(key, out var cached))
            {
                recipeOrder.Remove(key);
                recipeOrder.Add(key);
                return cached;
            }
            var made = MakeShape(f, importFolder, out closed);
            if (made == null || !closed)
            {
                made?.Dispose();
                return null;
            }
            _ = made.Status; // evaluate now, once
            recipes[key] = made;
            recipeOrder.Add(key);
            while (recipeOrder.Count > CacheSize)
            {
                recipes[recipeOrder[0]].Dispose();
                recipes.Remove(recipeOrder[0]);
                recipeOrder.RemoveAt(0);
            }
            return made;
        }

        static string Recipe(BodyFeature f, string importFolder)
        {
            var key = new System.Text.StringBuilder();
            key.Append(FormattableString.Invariant($"{f.Kind}|{f.SizeX}|{f.SizeY}|{f.SizeZ}|{f.Detail}|{f.Pitch}|{f.HoleSize}|{f.MirrorX}"));
            if (f.Kind == FeatureKind.Imported) key.Append('|').Append(Path.Combine(importFolder, f.MeshFile));
            if (f.Kind == FeatureKind.Extrusion) foreach (float v in f.Outline) key.Append('|').Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return key.ToString();
        }

        static Solid? MakeShape(BodyFeature f, string importFolder, out bool closed)
        {
            closed = true;
            float sx = Math.Max(0.2f, f.SizeX), sy = Math.Max(0.2f, f.SizeY), sz = Math.Max(0.2f, f.SizeZ);
            switch (f.Kind)
            {
                case FeatureKind.Box:
                    return Solid.Cube(sx, sy, sz, 0, 0, 0);
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
                    var shape = Solid.Hull(corners);
                    foreach (var c in corners) c.Dispose();
                    return shape;
                }
                case FeatureKind.Cylinder:
                case FeatureKind.Cone:
                {
                    double top = f.Kind == FeatureKind.Cone ? 0.5 * Mathf.Clamp01(f.Detail) : 0.5;
                    return Upright(Solid.Cone(1, 0.5, Math.Max(top, 1e-4), Segments), sx, sy, sz);
                }
                case FeatureKind.Sphere:
                {
                    using var ball = Solid.Sphere(0.5, Segments);
                    return ball.Scale(sx, sy, sz);
                }
                case FeatureKind.Wedge: // a ramp: flat bottom, upright back face, slope down to the front edge
                    return Solid.HullOf(new[]
                    {
                        new Vector3(-sx / 2, -sy / 2, -sz / 2), new Vector3(sx / 2, -sy / 2, -sz / 2),
                        new Vector3(-sx / 2, -sy / 2, sz / 2), new Vector3(sx / 2, -sy / 2, sz / 2),
                        new Vector3(-sx / 2, sy / 2, -sz / 2), new Vector3(sx / 2, sy / 2, -sz / 2),
                    });
                case FeatureKind.Tube:
                {
                    float wall = Mathf.Clamp(f.Detail, 0.4f, Math.Min(sx, sz) / 2 - 0.2f);
                    using var outer = Upright(Solid.Cone(1, 0.5, 0.5, Segments), sx, sy, sz);
                    using var inner = Upright(Solid.Cone(1, 0.5, 0.5, Segments), sx - 2 * wall, sy + 2, sz - 2 * wall);
                    return outer.Minus(inner);
                }
                case FeatureKind.Extrusion:
                    return Extrusion(f.Outline, sx, sy, sz);
                case FeatureKind.Plate:
                    return Plate(sx, sy, sz, f.Detail, f.Pitch, f.HoleSize);
                case FeatureKind.Imported:
                {
                    var mesh = LoadImport(Path.Combine(importFolder, f.MeshFile));
                    if (mesh == null) return null;
                    var (positions, triangles) = Sized(mesh, f.MirrorX ? -sx : sx, sy, sz);
                    return Solid.FromMesh(positions, triangles, out closed);
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// An imported mesh at its size (a negative x mirrors it; the triangles then turn the other way so their
        /// front faces stay outside).
        /// </summary>
        static (float[] positions, int[] triangles) Sized(MeshFileData mesh, float sx, float sy, float sz)
        {
            var positions = new float[mesh.Positions.Length];
            for (int i = 0; i < positions.Length; i += 3)
            {
                positions[i] = mesh.Positions[i] * sx;
                positions[i + 1] = mesh.Positions[i + 1] * sy;
                positions[i + 2] = mesh.Positions[i + 2] * sz;
            }
            var triangles = mesh.Triangles;
            if (sx < 0)
            {
                triangles = (int[])triangles.Clone();
                for (int t = 0; t < triangles.Length; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            }
            return (positions, triangles);
        }

        /// <summary>
        /// An imported mesh that is not a closed solid, placed like any shape but without Manifold: every
        /// triangle with its own flat normal (the file's triangles face outward, as Manifold's do).
        /// </summary>
        static MeshData? OpenMesh(BodyFeature f, string importFolder)
        {
            if (f.Kind != FeatureKind.Imported) return null;
            var mesh = LoadImport(Path.Combine(importFolder, f.MeshFile));
            if (mesh == null) return null;
            float sx = Math.Max(0.2f, f.SizeX), sy = Math.Max(0.2f, f.SizeY), sz = Math.Max(0.2f, f.SizeZ);
            var (positions, triangles) = Sized(mesh, f.MirrorX ? -sx : sx, sy, sz);
            var m = Matrix4x4.TRS(new Vector3(f.X, f.Y, f.Z), Quaternion.Euler(f.RotX, f.RotY, f.RotZ), Vector3.one);
            var data = new MeshData { NumProp = 6, Properties = new float[triangles.Length * 6], Triangles = new int[triangles.Length] };
            for (int t = 0; t < triangles.Length; t += 3)
            {
                var a = m.MultiplyPoint3x4(Point(positions, triangles[t]));
                var b = m.MultiplyPoint3x4(Point(positions, triangles[t + 1]));
                var c = m.MultiplyPoint3x4(Point(positions, triangles[t + 2]));
                var n = Vector3.Cross(b - a, c - a).normalized;
                int k = 0;
                foreach (var p in new[] { a, b, c })
                {
                    int v = t + k++, o = v * 6;
                    data.Properties[o] = p.x;
                    data.Properties[o + 1] = p.y;
                    data.Properties[o + 2] = p.z;
                    data.Properties[o + 3] = n.x;
                    data.Properties[o + 4] = n.y;
                    data.Properties[o + 5] = n.z;
                    data.Triangles[v] = v;
                }
            }
            return data;
        }

        static Vector3 Point(float[] positions, int index) => new Vector3(positions[3 * index], positions[3 * index + 1], positions[3 * index + 2]);

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

        // ------------------------------------------------------------------ perforated plate

        /// <summary>
        /// A sheet about its centre: a rectangle whose corners are rounded by <paramref name="corner"/> (a disc or
        /// stadium when that is half its width), with holes of <paramref name="holeSize"/> every
        /// <paramref name="pitch"/> mm on a grid through its middle, each at least 3 mm of material from the edge.
        /// </summary>
        static Solid Plate(float width, float thickness, float length, float corner, float pitch, float holeSize)
        {
            float r = Mathf.Clamp(corner, 0, Math.Min(width, length) / 2);
            var outline = Outline(width, thickness, length, r);
            if (pitch < 5 || holeSize <= 0) return outline;
            var holes = new List<Solid>();
            float hr = holeSize / 2, margin = hr + 3;
            int segments = Math.Max(8, Native.manifold_get_circular_segments(hr));
            int nx = (int)(width / 2 / pitch), nz = (int)(length / 2 / pitch);
            for (int i = -nx; i <= nx; i++)
            {
                for (int j = -nz; j <= nz; j++)
                {
                    float x = i * pitch, z = j * pitch;
                    if (!Inside(width, length, r, x, z, margin)) continue;
                    using var cylinder = Solid.Cylinder(thickness + 2, hr, segments);
                    using var upright = cylinder.Rotate(90, 0, 0);
                    holes.Add(upright.Translate(x, 0, z));
                }
            }
            if (holes.Count == 0) return outline;
            var cut = new List<Solid> { outline };
            cut.AddRange(holes);
            var result = Solid.Batch(cut, Native.OpSubtract); // the plate minus every hole
            foreach (var solid in cut) solid.Dispose();
            return result;
        }

        /// <summary>A sheet about its centre with rounded corners; a disc when the corners meet.</summary>
        static Solid Outline(float width, float thickness, float length, float r)
        {
            if (r < 0.5f) return Solid.Cube(width, thickness, length, 0, 0, 0);
            float x = width / 2 - r, z = length / 2 - r;
            int segments = Math.Max(16, Native.manifold_get_circular_segments(r));
            if (x < 0.01f && z < 0.01f)
            {
                using var disc = Solid.Cylinder(thickness, r, Math.Max(segments, 96));
                return disc.Rotate(90, 0, 0);
            }
            var corners = new List<Solid>();
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    using var cylinder = Solid.Cylinder(thickness, r, segments);
                    using var upright = cylinder.Rotate(90, 0, 0);
                    corners.Add(upright.Translate(sx * Math.Max(0, x), 0, sz * Math.Max(0, z)));
                }
            }
            var hull = Solid.Hull(corners);
            foreach (var c in corners) c.Dispose();
            return hull;
        }

        /// <summary>True when a circle of radius <paramref name="margin"/> at (x, z) lies inside the rounded outline.</summary>
        static bool Inside(float width, float length, float r, float x, float z, float margin)
        {
            float hw = width / 2, hl = length / 2;
            if (Math.Abs(x) > hw - margin || Math.Abs(z) > hl - margin) return false;
            float cx = Math.Abs(x) - (hw - r), cz = Math.Abs(z) - (hl - r);
            return cx <= 0 || cz <= 0 || cx * cx + cz * cz <= (r - margin) * (r - margin);
        }
    }
}
