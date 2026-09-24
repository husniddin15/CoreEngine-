using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreEngine.Spike
{
    // Manifold v3.5.3 (Apache-2.0) through its C API, manifoldc.dll (native/manifold, ADR-0005).
    // Shared by the CSG spike and the Body Studio.

    /// <summary>Vertex properties (x, y, z, then normal x, y, z) and triangle indices of a Manifold mesh.</summary>
    internal sealed class MeshData
    {
        public int NumProp;
        public float[] Properties = Array.Empty<float>();
        public int[] Triangles = Array.Empty<int>();

        /// <summary>x, y, z per vertex in the solid's own units (millimetres in the Body Studio).</summary>
        public float[] Positions()
        {
            int count = Properties.Length / NumProp;
            var positions = new float[count * 3];
            for (int i = 0, o = 0; i < count; i++, o += NumProp)
            {
                positions[3 * i] = Properties[o];
                positions[3 * i + 1] = Properties[o + 1];
                positions[3 * i + 2] = Properties[o + 2];
            }
            return positions;
        }

        public Mesh ToUnityMesh(float scale)
        {
            // Manifold keeps triangles counter-clockwise about outward normals in its right-handed
            // frame. Read unchanged in Unity's left-handed frame they appear clockwise from outside,
            // which is Unity's front face, so positions, normals and indices are used as they are.
            int count = Properties.Length / NumProp;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            for (int i = 0, o = 0; i < count; i++, o += NumProp)
            {
                vertices[i] = new Vector3(Properties[o], Properties[o + 1], Properties[o + 2]) * scale;
                normals[i] = new Vector3(Properties[o + 3], Properties[o + 4], Properties[o + 5]);
            }
            var mesh = new Mesh { indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Owns one ManifoldManifold. Operations are lazy until a result is read.</summary>
    internal sealed class Solid : IDisposable
    {
        IntPtr handle;

        Solid(IntPtr handle) => this.handle = handle;

        public static Solid Cube(double x, double y, double z) =>
            new Solid(Native.manifold_cube(Native.manifold_alloc_manifold(), x, y, z, 1));

        public static Solid Cube(double x, double y, double z, double cx, double cy, double cz)
        {
            using var cube = Cube(x, y, z);
            return cube.Translate(cx, cy, cz);
        }

        public static Solid Cylinder(double height, double radius, int segments) =>
            new Solid(Native.manifold_cylinder(Native.manifold_alloc_manifold(), height, radius, radius, segments, 1));

        public Solid Translate(double x, double y, double z) =>
            new Solid(Native.manifold_translate(Native.manifold_alloc_manifold(), handle, x, y, z));

        public Solid Rotate(double xDeg, double yDeg, double zDeg) =>
            new Solid(Native.manifold_rotate(Native.manifold_alloc_manifold(), handle, xDeg, yDeg, zDeg));

        public Solid Union(Solid other) =>
            new Solid(Native.manifold_union(Native.manifold_alloc_manifold(), handle, other.handle));

        public Solid Minus(Solid other) =>
            new Solid(Native.manifold_difference(Native.manifold_alloc_manifold(), handle, other.handle));

        public static Solid Batch(IReadOnlyList<Solid> solids, int op)
        {
            IntPtr vec = Native.manifold_manifold_empty_vec(Native.manifold_alloc_manifold_vec());
            try
            {
                foreach (var s in solids) Native.manifold_manifold_vec_push_back(vec, s.handle); // copies
                return new Solid(Native.manifold_batch_boolean(Native.manifold_alloc_manifold(), vec, op));
            }
            finally
            {
                Native.manifold_delete_manifold_vec(vec);
            }
        }

        /// <summary>The convex hull of the solids, for example four corner cylinders make a rounded plate.</summary>
        public static Solid Hull(IReadOnlyList<Solid> solids)
        {
            IntPtr vec = Native.manifold_manifold_empty_vec(Native.manifold_alloc_manifold_vec());
            try
            {
                foreach (var s in solids) Native.manifold_manifold_vec_push_back(vec, s.handle);
                return new Solid(Native.manifold_batch_hull(Native.manifold_alloc_manifold(), vec));
            }
            finally
            {
                Native.manifold_delete_manifold_vec(vec);
            }
        }

        public double Volume => Native.manifold_volume(handle);
        public int Genus => Native.manifold_genus(handle);
        public int Status => Native.manifold_status(handle);

        /// <summary>Vertex positions and normals (sharp edges above 52.5 degrees stay sharp).</summary>
        public MeshData ToMeshData()
        {
            IntPtr withNormals = Native.manifold_calculate_normals(Native.manifold_alloc_manifold(), handle, 0, 52.5);
            IntPtr gl = Native.manifold_get_meshgl(Native.manifold_alloc_meshgl(), withNormals);
            try
            {
                var data = new MeshData
                {
                    NumProp = (int)Native.manifold_meshgl_num_prop(gl),
                    Properties = new float[(int)Native.manifold_meshgl_vert_properties_length(gl)],
                    Triangles = new int[(int)Native.manifold_meshgl_tri_length(gl)],
                };
                if (data.NumProp < 6) throw new InvalidOperationException($"expected normals, got {data.NumProp} properties");
                Native.manifold_meshgl_vert_properties(data.Properties, gl);
                Native.manifold_meshgl_tri_verts(data.Triangles, gl);
                return data;
            }
            finally
            {
                Native.manifold_delete_meshgl(gl);
                Native.manifold_delete_manifold(withNormals);
            }
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            Native.manifold_delete_manifold(handle);
            handle = IntPtr.Zero;
        }
    }

    /// <summary>The subset of bindings/c/include/manifold/manifoldc.h used by the spikes.</summary>
    internal static class Native
    {
        const string Lib = "manifoldc";
        public const int OpAdd = 0, OpSubtract = 1;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_alloc_manifold();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_alloc_manifold_vec();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_alloc_meshgl();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void manifold_delete_manifold(IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void manifold_delete_manifold_vec(IntPtr ms);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void manifold_delete_meshgl(IntPtr m);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_cube(IntPtr mem, double x, double y, double z, int center);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_cylinder(IntPtr mem, double height, double radiusLow, double radiusHigh, int circularSegments, int center);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_translate(IntPtr mem, IntPtr m, double x, double y, double z);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_rotate(IntPtr mem, IntPtr m, double x, double y, double z);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_union(IntPtr mem, IntPtr a, IntPtr b);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_difference(IntPtr mem, IntPtr a, IntPtr b);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_manifold_empty_vec(IntPtr mem);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void manifold_manifold_vec_push_back(IntPtr ms, IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_batch_boolean(IntPtr mem, IntPtr ms, int op);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_batch_hull(IntPtr mem, IntPtr ms);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_calculate_normals(IntPtr mem, IntPtr m, int normalIdx, double minSharpAngle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int manifold_status(IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int manifold_genus(IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern double manifold_volume(IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int manifold_get_circular_segments(double radius);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_get_meshgl(IntPtr mem, IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nuint manifold_meshgl_num_prop(IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nuint manifold_meshgl_vert_properties_length(IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nuint manifold_meshgl_tri_length(IntPtr m);
        // The native side copies into the caller's memory; blittable arrays are pinned and passed as pointers.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_meshgl_vert_properties([Out] float[] mem, IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_meshgl_tri_verts([Out] int[] mem, IntPtr m);
    }
}
