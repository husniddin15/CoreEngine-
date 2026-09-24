using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoreEngine.Sim.Design
{
    /// <summary>Binary STL for 3D printing (docs/08 §3 Export): millimetres, one facet normal per triangle.</summary>
    public static class StlWriter
    {
        /// <param name="positions">x, y, z per vertex in millimetres.</param>
        /// <param name="triangles">Three vertex indices per triangle, counter-clockwise seen from outside.</param>
        /// <param name="yUp">True converts a y-up model (Unity, the Body Studio) to z-up, as slicers expect.</param>
        public static void Write(Stream stream, IReadOnlyList<float> positions, IReadOnlyList<int> triangles, string name, bool yUp = true)
        {
            if (triangles.Count % 3 != 0) throw new ArgumentException("Triangle indices must come in threes.", nameof(triangles));
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            var header = new byte[80];
            byte[] text = Encoding.ASCII.GetBytes("CoreEngine Body Studio: " + name);
            Array.Copy(text, header, Math.Min(text.Length, header.Length));
            writer.Write(header);
            writer.Write((uint)(triangles.Count / 3));
            for (int t = 0; t < triangles.Count; t += 3)
            {
                var a = Vertex(positions, triangles[t], yUp);
                var b = Vertex(positions, triangles[t + 1], yUp);
                var c = Vertex(positions, triangles[t + 2], yUp);
                var n = Normal(a, b, c);
                foreach (var v in new[] { n, a, b, c })
                {
                    writer.Write(v.x);
                    writer.Write(v.y);
                    writer.Write(v.z);
                }
                writer.Write((ushort)0);
            }
        }

        /// <summary>Turns a y-up model to z-up with a quarter turn about x, as slicers expect; a rotation keeps every facet facing outwards.</summary>
        static (float x, float y, float z) Vertex(IReadOnlyList<float> p, int i, bool yUp)
        {
            float x = p[3 * i], y = p[3 * i + 1], z = p[3 * i + 2];
            return yUp ? (x, -z, y) : (x, y, z);
        }

        static (float x, float y, float z) Normal((float x, float y, float z) a, (float x, float y, float z) b, (float x, float y, float z) c)
        {
            float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            float vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float length = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return length > 0 ? (nx / length, ny / length, nz / length) : (0, 0, 0);
        }
    }
}
