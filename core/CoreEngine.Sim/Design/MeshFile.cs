using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CoreEngine.Sim.Design
{
    /// <summary>A triangle mesh read from a file: positions (x, y, z per vertex, y up) and triangles (three indices each).</summary>
    public sealed class MeshFileData
    {
        public float[] Positions = Array.Empty<float>();
        public int[] Triangles = Array.Empty<int>();

        public int VertexCount => Positions.Length / 3;
        public int TriangleCount => Triangles.Length / 3;

        /// <summary>Smallest and largest corner of the bounds.</summary>
        public (float x, float y, float z) Min, Max;

        public void ComputeBounds()
        {
            float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < Positions.Length; i++)
            {
                min[i % 3] = Math.Min(min[i % 3], Positions[i]);
                max[i % 3] = Math.Max(max[i % 3], Positions[i]);
            }
            Min = (min[0], min[1], min[2]);
            Max = (max[0], max[1], max[2]);
        }
    }

    /// <summary>
    /// Reads the files players bring into the Body Studio (docs/08 §3, import): STL, binary or ASCII, and
    /// Wavefront OBJ. STL is z-up by the 3D-printing convention and is turned to y-up, the inverse of
    /// <see cref="StlWriter"/>, so an exported body comes back where it was. Faces with more than three corners
    /// are split into triangles. Vertices are welded, because STL repeats every corner of every triangle.
    /// </summary>
    public static class MeshFile
    {
        public static MeshFileData Read(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            var data = extension == ".obj" ? ReadObj(File.ReadAllText(path)) : ReadStl(File.ReadAllBytes(path));
            data = Weld(data, 1e-4f);
            data.ComputeBounds();
            return data;
        }

        public static MeshFileData ReadStl(byte[] bytes)
        {
            bool ascii = bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5).Equals("solid", StringComparison.OrdinalIgnoreCase)
                         && !(bytes.Length >= 84 && 84 + 50L * BitConverter.ToUInt32(bytes, 80) == bytes.Length);
            var positions = new List<float>();
            if (ascii)
            {
                foreach (string raw in Encoding.ASCII.GetString(bytes).Split('\n'))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    AddZUp(positions, Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
                }
            }
            else
            {
                if (bytes.Length < 84) throw new InvalidDataException("the STL file is too short");
                uint count = BitConverter.ToUInt32(bytes, 80);
                if (84 + 50L * count > bytes.Length) throw new InvalidDataException("the STL file is cut short");
                for (int t = 0; t < count; t++)
                {
                    int offset = 84 + 50 * t + 12; // skip the facet normal
                    for (int v = 0; v < 3; v++)
                        AddZUp(positions, BitConverter.ToSingle(bytes, offset + 12 * v), BitConverter.ToSingle(bytes, offset + 12 * v + 4),
                               BitConverter.ToSingle(bytes, offset + 12 * v + 8));
                }
            }
            var triangles = new int[positions.Count / 3];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            return new MeshFileData { Positions = positions.ToArray(), Triangles = triangles };
        }

        /// <summary>STL is z up: (x, y, z) in the file is (x, z, -y) in the game's y-up frame.</summary>
        static void AddZUp(List<float> positions, float x, float y, float z)
        {
            positions.Add(x);
            positions.Add(z);
            positions.Add(-y);
        }

        public static MeshFileData ReadObj(string text)
        {
            var positions = new List<float>();
            var triangles = new List<int>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    positions.Add(Parse(parts[1]));
                    positions.Add(Parse(parts[2]));
                    positions.Add(Parse(parts[3]));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    var corners = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string index = parts[i].Split('/')[0];
                        if (!int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k)) continue;
                        corners.Add(k > 0 ? k - 1 : positions.Count / 3 + k); // OBJ counts from 1; negative counts back
                    }
                    for (int i = 1; i + 1 < corners.Count; i++) // a fan splits a polygon face into triangles
                    {
                        triangles.Add(corners[0]);
                        triangles.Add(corners[i]);
                        triangles.Add(corners[i + 1]);
                    }
                }
            }
            return new MeshFileData { Positions = positions.ToArray(), Triangles = triangles.ToArray() };
        }

        /// <summary>Joins vertices closer than <paramref name="tolerance"/> and drops triangles that collapse.</summary>
        public static MeshFileData Weld(MeshFileData mesh, float tolerance)
        {
            var map = new Dictionary<(long, long, long), int>();
            var positions = new List<float>();
            var remap = new int[mesh.VertexCount];
            double cell = Math.Max(tolerance, 1e-9);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                float x = mesh.Positions[3 * v], y = mesh.Positions[3 * v + 1], z = mesh.Positions[3 * v + 2];
                var key = ((long)Math.Round(x / cell), (long)Math.Round(y / cell), (long)Math.Round(z / cell));
                if (!map.TryGetValue(key, out int index))
                {
                    index = positions.Count / 3;
                    positions.Add(x);
                    positions.Add(y);
                    positions.Add(z);
                    map[key] = index;
                }
                remap[v] = index;
            }
            var triangles = new List<int>(mesh.Triangles.Length);
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                int a = remap[mesh.Triangles[t]], b = remap[mesh.Triangles[t + 1]], c = remap[mesh.Triangles[t + 2]];
                if (a == b || b == c || a == c) continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
            var result = new MeshFileData { Positions = positions.ToArray(), Triangles = triangles.ToArray() };
            result.ComputeBounds();
            return result;
        }

        static float Parse(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
