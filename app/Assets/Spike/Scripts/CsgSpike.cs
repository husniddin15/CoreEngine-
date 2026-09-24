using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreEngine.Spike
{
    /// <summary>
    /// Phase 0.5 spike (docs/11-roadmap.md §3, ADR-0005): mesh booleans with Manifold v3.5.3 through
    /// its C API (manifoldc.dll, built by native/manifold/build.ps1) and P/Invoke, turned into a
    /// Unity mesh. Shapes are built in millimetres, like the Body Studio (docs/08).
    /// Throwaway prototype, not the Phase 1 geometry service.
    /// </summary>
    public sealed class CsgSpike : MonoBehaviour
    {
        public Material? bodyMaterial;

        /// <summary>The chassis from the last benchmark, shown outside the arena.</summary>
        public GameObject? Body { get; private set; }

        const int Segments = 32;

        public string RunBenchmark()
        {
            var report = new StringBuilder();
            report.AppendLine("csg (Manifold v3.5.3, manifoldc.dll via P/Invoke, main thread):");
            try
            {
                var watch = Stopwatch.StartNew();
                using (var probe = Solid.Cube(1, 1, 1)) _ = probe.Volume;
                report.AppendLine($"  first call (loads the DLL): {watch.Elapsed.TotalMilliseconds:F1} ms");

                // Phase 0.5 exit criterion: two boxes united, minus a cylinder hole, in < 50 ms.
                double plusVolume = 40 * 10 * 20 + 20 * 10 * 40 - 20 * 10 * 20 - PolygonArea(5, Segments) * 10;
                Measure(report, "two boxes + cylinder hole (3 shapes)", BuildPlusWithHole, plusVolume, 1, 20, keep: false);

                // docs/08 target: a group of up to 50 shapes in < 100 ms. Manifold's own segment count for
                // a 3 mm radius (20) is what the Body Studio would use by default.
                int defaultSegments = Native.manifold_get_circular_segments(3);
                foreach (int segments in new[] { defaultSegments, Segments })
                {
                    double volume = 120 * 3 * 160 + 2 * (3 * 25 * 160) - 2 * (3 * 3 * 160) - 48 * PolygonArea(3, segments) * 3;
                    Measure(report, $"chassis: plate + 2 walls - 48 holes (51 shapes), {segments}-sided holes",
                            () => BuildChassis(segments), volume, 48, 20, keep: segments == Segments);
                }
            }
            catch (Exception e)
            {
                report.AppendLine("  FAILED: " + e);
            }
            return report.ToString();
        }

        /// <summary>
        /// The 48-hole chassis plate (Manifold's default 20-sided holes) as a Unity mesh in metres, bottom at
        /// y = 0; the Garage uses it as a Body Studio result. Null when manifoldc.dll cannot be loaded.
        /// </summary>
        public static Mesh? ChassisMesh()
        {
            try
            {
                using var solid = BuildChassis(Native.manifold_get_circular_segments(3));
                var mesh = solid.ToMeshData().ToUnityMesh(0.001f);
                mesh.name = "HoledChassis";
                return mesh;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                UnityEngine.Debug.LogWarning("CsgSpike: Manifold is not available: " + e.Message);
                return null;
            }
        }

        void Measure(StringBuilder report, string name, Func<Solid> build, double expectedVolume, int expectedGenus, int runs, bool keep)
        {
            var times = new List<double>();
            var booleanTimes = new List<double>();
            var normalTimes = new List<double>();
            var uploadTimes = new List<double>();
            Mesh? mesh = null;
            double volume = 0;
            int genus = 0, status = 0;
            for (int run = 0; run < runs; run++)
            {
                if (mesh != null) Destroy(mesh);
                var watch = Stopwatch.StartNew();
                using var solid = build();
                status = solid.Status; // evaluates the lazy boolean tree
                double t1 = watch.Elapsed.TotalMilliseconds;
                var data = solid.ToMeshData();
                double t2 = watch.Elapsed.TotalMilliseconds;
                mesh = data.ToUnityMesh(0.001f); // mm -> m
                double t3 = watch.Elapsed.TotalMilliseconds;
                times.Add(t3);
                booleanTimes.Add(t1);
                normalTimes.Add(t2 - t1);
                uploadTimes.Add(t3 - t2);
                volume = solid.Volume;
                genus = solid.Genus;
            }

            double first = times[0];
            foreach (var list in new[] { times, booleanTimes, normalTimes, uploadTimes })
            {
                list.RemoveAt(0);
                list.Sort();
            }
            static double Median(List<double> list) => list[list.Count / 2];
            double error = Math.Abs(volume - expectedVolume) / expectedVolume;
            bool ok = status == 0 && genus == expectedGenus && error < 1e-6;
            report.AppendLine($"  {name}:");
            report.AppendLine($"    first run {first:F2} ms, then median {Median(times):F2} ms " +
                              $"(min {times[0]:F2}, max {times[times.Count - 1]:F2}) over {times.Count} runs");
            report.AppendLine($"    median split: shapes + booleans {Median(booleanTimes):F2} ms, normals + mesh copy " +
                              $"{Median(normalTimes):F2} ms, Unity mesh {Median(uploadTimes):F2} ms");
            report.AppendLine($"    {mesh!.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, genus {genus} (expected {expectedGenus}), " +
                              $"volume {volume:F2} mm3 (expected {expectedVolume:F2}, error {error:E1}), status {status} -> {(ok ? "OK" : "WRONG")}");

            if (keep) ShowBody(mesh);
            else Destroy(mesh);
        }

        void ShowBody(Mesh mesh)
        {
            if (Body != null) Destroy(Body);
            Body = new GameObject("CsgChassis");
            Body.AddComponent<MeshFilter>().sharedMesh = mesh;
            Body.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            Body.transform.SetPositionAndRotation(new Vector3(0, 0, -2.2f), Quaternion.Euler(0, 30f, 0));
        }

        /// <summary>Two crossed boxes (a plus sign, 10 mm thick) with a 10 mm hole through the middle.</summary>
        static Solid BuildPlusWithHole()
        {
            using var a = Solid.Cube(40, 10, 20);
            using var b = Solid.Cube(20, 10, 40);
            using var both = a.Union(b);
            using var hole = VerticalHole(0, 0, 0, 5, 20, Segments);
            return both.Minus(hole);
        }

        /// <summary>A 120 x 160 mm, 3 mm plate with 25 mm side walls and a 6 x 8 grid of 6 mm holes.</summary>
        static Solid BuildChassis(int segments)
        {
            var solids = new List<Solid>
            {
                Solid.Cube(120, 3, 160, 0, 1.5, 0),
                Solid.Cube(3, 25, 160, -58.5, 12.5, 0),
                Solid.Cube(3, 25, 160, 58.5, 12.5, 0),
            };
            using var body = Solid.Batch(solids, Native.OpAdd);
            foreach (var s in solids) s.Dispose();

            var parts = new List<Solid> { body };
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 8; j++)
                    parts.Add(VerticalHole(-37.5 + 15 * i, 1.5, -63 + 18 * j, 3, 10, segments));
            var result = Solid.Batch(parts, Native.OpSubtract); // the first minus all the others
            for (int k = 1; k < parts.Count; k++) parts[k].Dispose();
            return result;
        }

        static Solid VerticalHole(double x, double y, double z, double radius, double height, int segments)
        {
            using var cylinder = Solid.Cylinder(height, radius, segments); // along Z, centred
            using var upright = cylinder.Rotate(90, 0, 0);                 // along Y
            return upright.Translate(x, y, z);
        }

        /// <summary>Area of the regular polygon Manifold uses for a cylinder.</summary>
        static double PolygonArea(double radius, int segments) => 0.5 * segments * radius * radius * Math.Sin(2 * Math.PI / segments);
    }
}
