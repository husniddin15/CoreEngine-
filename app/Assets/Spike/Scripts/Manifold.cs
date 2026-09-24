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

        /// <summary>A cylinder or cone along +z, centred, from radius <paramref name="low"/> at the bottom to <paramref name="high"/> at the top.</summary>
        public static Solid Cone(double height, double low, double high, int segments) =>
            new Solid(Native.manifold_cylinder(Native.manifold_alloc_manifold(), height, low, high, segments, 1));

        public static Solid Sphere(double radius, int segments) =>
            new Solid(Native.manifold_sphere(Native.manifold_alloc_manifold(), radius, segments));

        /// <summary>The convex hull of points.</summary>
        public static Solid HullOf(IReadOnlyList<Vector3> points)
        {
            var xyz = new double[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                xyz[3 * i] = points[i].x;
                xyz[3 * i + 1] = points[i].y;
                xyz[3 * i + 2] = points[i].z;
            }
            return new Solid(Native.manifold_hull_pts(Native.manifold_alloc_manifold(), xyz, (nuint)points.Count));
        }

        /// <summary>A polygon in the xy plane (counter-clockwise) extruded along +z from 0 to <paramref name="height"/>.</summary>
        public static Solid Extrude(IReadOnlyList<Vector2> polygon, double height)
        {
            var xy = new double[polygon.Count * 2];
            for (int i = 0; i < polygon.Count; i++)
            {
                xy[2 * i] = polygon[i].x;
                xy[2 * i + 1] = polygon[i].y;
            }
            IntPtr simple = Native.manifold_simple_polygon(Native.manifold_alloc_simple_polygon(), xy, (nuint)polygon.Count);
            IntPtr polygons = Native.manifold_polygons(Native.manifold_alloc_polygons(), new[] { simple }, 1);
            try
            {
                return new Solid(Native.manifold_extrude(Native.manifold_alloc_manifold(), polygons, height, 0, 0, 1, 1));
            }
            finally
            {
                Native.manifold_delete_polygons(polygons);
                Native.manifold_delete_simple_polygon(simple);
            }
        }

        /// <summary>
        /// A solid from any triangle mesh (millimetres). Manifold merges vertices that touch; when the mesh is still
        /// not a closed solid, <paramref name="closed"/> is false and the result must not take part in booleans.
        /// </summary>
        public static Solid FromMesh(float[] positions, int[] triangles, out bool closed)
        {
            var tris = new uint[triangles.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = (uint)triangles[i];
            IntPtr gl = Native.manifold_meshgl(Native.manifold_alloc_meshgl(), positions, (nuint)(positions.Length / 3), 3, tris, (nuint)(tris.Length / 3));
            IntPtr merged = Native.manifold_meshgl_merge(Native.manifold_alloc_meshgl(), gl);
            try
            {
                var solid = new Solid(Native.manifold_of_meshgl(Native.manifold_alloc_manifold(), merged));
                closed = solid.Status == 0 && Native.manifold_num_tri(solid.handle) > 0;
                return solid;
            }
            finally
            {
                Native.manifold_delete_meshgl(merged);
                Native.manifold_delete_meshgl(gl);
            }
        }

        /// <summary>Applies a Unity matrix (rotation, scale and translation; the columns go to Manifold's 3 × 4 matrix).</summary>
        public Solid Transform(Matrix4x4 m) =>
            new Solid(Native.manifold_transform(Native.manifold_alloc_manifold(), handle,
                m.m00, m.m10, m.m20, m.m01, m.m11, m.m21, m.m02, m.m12, m.m22, m.m03, m.m13, m.m23));

        public Solid Scale(double x, double y, double z) =>
            new Solid(Native.manifold_scale(Native.manifold_alloc_manifold(), handle, x, y, z));

        public int TriangleCount => (int)Native.manifold_num_tri(handle);

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
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_sphere(IntPtr mem, double radius, int circularSegments);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_scale(IntPtr mem, IntPtr m, double x, double y, double z);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr manifold_transform(IntPtr mem, IntPtr m, double x1, double y1, double z1, double x2, double y2, double z2,
                                                       double x3, double y3, double z3, double x4, double y4, double z4);
        // ManifoldVec3 and ManifoldVec2 are plain doubles, so arrays of doubles marshal as arrays of them.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_hull_pts(IntPtr mem, double[] ps, nuint length);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_alloc_simple_polygon();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_alloc_polygons();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_simple_polygon(IntPtr mem, double[] ps, nuint length);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_polygons(IntPtr mem, IntPtr[] ps, nuint length);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void manifold_delete_simple_polygon(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void manifold_delete_polygons(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr manifold_extrude(IntPtr mem, IntPtr cs, double height, int slices, double twistDegrees, double scaleX, double scaleY);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr manifold_meshgl(IntPtr mem, float[] vertProps, nuint nVerts, nuint nProps, uint[] triVerts, nuint nTris);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_meshgl_merge(IntPtr mem, IntPtr m);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr manifold_of_meshgl(IntPtr mem, IntPtr mesh);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nuint manifold_num_tri(IntPtr m);

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
