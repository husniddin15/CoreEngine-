using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// The yellow TT gear motor, 1:48 (docs/09 §6): the moulded gearbox with its shaft bosses and mounting holes,
    /// the white double-sided output shaft (the wheel on the -x side), the silver FA-130 can in the gearbox's
    /// cradle, and the black end cap with its brass tabs and the red dot by M+. Frame: the gearbox's middle; the
    /// shaft runs along x at y = 8.5 (docs: DesignGeometry.WheelInMotor). The wheel is its own mesh, as it turns.
    /// </summary>
    static class MotorModel
    {
        const float ShaftY = 8.5f;
        static readonly Color Yellow = new Color(0.97f, 0.74f, 0.10f);

        public static PartModel Make()
        {
            var b = new Bench();
            var k = b.Kit;
            int yellow = b[PartLooks.Solid("tt yellow", Yellow, 0.38f)];
            k.Box(yellow, new Vector3(0, 0, 2.75f), new Vector3(18.8f, 22f, 42.5f), 1.4f);
            // The two halves' seam and the ribs of the moulding.
            k.Box(b[PartLooks.Solid("tt seam", Yellow * 0.72f, 0.3f)], new Vector3(0, 0, 2.75f), new Vector3(0.35f, 22.06f, 42.56f));
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 9.4f;
                k.Cylinder(yellow, new Vector3(x, ShaftY, 0), new Vector3(x + side * 1.3f, ShaftY, 0), 4.3f, 32, 0.45f);
                foreach (float y in new[] { -5.5f, 5.5f })
                {
                    Pieces.Disc(b, b[PartLooks.Hole], new Vector3(x + side * 0.02f, y, 13.5f), new Vector3(side, 0, 0), 1.65f, 0.1f, 20);
                    k.Cylinder(b[PartLooks.Solid("tt yellow shade", Yellow * 0.8f, 0.35f)], new Vector3(x - side * 0.3f, y, 13.5f), new Vector3(x + side * 0.01f, y, 13.5f), 2.3f, 20, 0, false);
                }
                k.Box(yellow, new Vector3(x + side * 0.4f, -7.5f, -8.0f), new Vector3(0.8f, 3.5f, 14f), 0.3f);
            }
            // The output shaft, white nylon: out to the wheel on -x, a short stub on +x.
            int nylon = b[PartLooks.Solid("tt shaft", new Color(0.93f, 0.92f, 0.88f), 0.4f)];
            k.Cylinder(nylon, new Vector3(-10.6f, ShaftY, 0), new Vector3(-16.0f, ShaftY, 0), 2.7f, 20, 0.2f);
            k.Cylinder(nylon, new Vector3(10.6f, ShaftY, 0), new Vector3(17.5f, ShaftY, 0), 2.7f, 20, 0.3f);

            // The FA-130 can: a stadium 15.1 × 20.1 mm, along z from the cradle to the end cap.
            var can = Stadium(15.1f, 20.1f, 10);
            k.Push(Vector3.zero, Quaternion.Euler(90, 0, 0)); // extrusion y is the motor's z
            k.Extrude(can, 24f, 43.4f, b[PartLooks.Solid("motor can", new Color(0.78f, 0.79f, 0.80f), 0.62f, 1f)],
                b[PartLooks.Solid("motor can", new Color(0.78f, 0.79f, 0.80f), 0.62f, 1f)], -1, null, 40);
            k.Extrude(Stadium(14.4f, 19.4f, 10), 43.4f, 44.8f, b[PartLooks.BlackPlastic], b[PartLooks.BlackPlastic], -1, null, 40);
            k.Pop();
            k.Cylinder(b[PartLooks.BlackPlastic], new Vector3(0, 0, 44.8f), new Vector3(0, 0, 45.4f), 2.6f, 24, 0.2f);
            k.Cylinder(b[PartLooks.Nickel], new Vector3(0, 0, 45.4f), new Vector3(0, 0, 45.6f), 1.0f, 16);
            foreach (float y in new[] { 4f, -4f })
            {
                k.Box(b[PartLooks.Brass], new Vector3(0, y, 45.1f), new Vector3(2.2f, 0.35f, 1.4f));
                k.Sphere(b[PartLooks.Tin], new Vector3(0, y, 45.4f), 0.7f, 12);
            }
            Pieces.Disc(b, b[PartLooks.Solid("red dot", new Color(0.8f, 0.05f, 0.05f), 0.4f)], new Vector3(-4.2f, 6.2f, 44.82f), Vector3.forward, 0.6f, 0.1f, 12);
            return b.Finish("TT gear motor");
        }

        /// <summary>A stadium outline in the extrusion's plane: <paramref name="width"/> across x, <paramref name="height"/> round ends.</summary>
        static List<Vector2> Stadium(float width, float height, int steps)
        {
            float r = width / 2, straight = height / 2 - r;
            var points = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.PI * i / steps;
                points.Add(new Vector2(Mathf.Cos(a) * r, straight + Mathf.Sin(a) * r));
            }
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.PI + Mathf.PI * i / steps;
                points.Add(new Vector2(Mathf.Cos(a) * r, -straight + Mathf.Sin(a) * r));
            }
            return points;
        }

        // ------------------------------------------------------------------ the wheel

        /// <summary>Submeshes of <see cref="WheelMesh"/>: tread, sidewall rubber, hub (the wheels' finish), screw, hole.</summary>
        public const int HubSlot = 2;

        public static Mesh WheelMesh => (wheel ??= MakeWheel()).Mesh;
        public static Material[] WheelMaterials => (wheel ??= MakeWheel()).Materials;
        static PartModel? wheel;

        /// <summary>The treaded tyre: chevron grooves every 9 mm round it, 0.8 mm deep.</summary>
        static Material Tread => PartLooks.Textured("tt tread", () =>
        {
            var r = new Raster(9f, 21.2f, 40);
            r.Fill(Ink.Solid(new Color32(15, 15, 16, 255), 0, 0.26f, 0));
            var groove = Ink.Solid(new Color32(7, 7, 7, 255), 0, 0.12f, -0.8f);
            foreach (float shift in new[] { 0f, 9f, -9f })
            {
                r.Line(1.0f + shift, 0.6f, 4.2f + shift, 10.6f, 1.3f, groove);
                r.Line(4.2f + shift, 10.6f, 1.0f + shift, 20.6f, 1.3f, groove);
            }
            r.Line(0, 10.6f, 9, 10.6f, 0.5f, Ink.Solid(new Color32(10, 10, 10, 255), 0, 0.15f, -0.3f));
            r.Grain(0.04f, 0.06f, 0.5f, 3);
            return r;
        }, 1.3f, repeat: true);

        static PartModel MakeWheel()
        {
            var b = new Bench();
            var k = b.Kit;
            int tread = b[Tread], rubber = b[PartLooks.Rubber], hub = b[PartLooks.Solid("tt hub", Yellow, 0.4f)], screw = b[PartLooks.Nickel], hole = b[PartLooks.Hole];
            var axis = Vector3.right;
            // Tyre: the treaded face and the rounded shoulders down to the rim.
            k.Lathe(tread, new Vector3(-10.6f, 0, 0), axis, new List<Vector2> { new(32.5f, 0), new(32.5f, 21.2f) }, 96, 30, 0, 360, 22, 1f / 21.2f);
            foreach (int side in new[] { -1, 1 })
            {
                var shoulder = new List<Vector2> { new(24.8f, 12.4f), new(27.8f, 13.0f), new(30.6f, 12.7f), new(32.0f, 11.8f), new(32.5f, 10.6f) };
                if (side < 0) for (int i = 0; i < shoulder.Count; i++) shoulder[i] = new Vector2(shoulder[i].x, -shoulder[i].y);
                if (side > 0) shoulder.Reverse();
                k.Lathe(rubber, Vector3.zero, axis, shoulder, 96, 60);
            }
            // Hub: the rim with its flanges, six spokes, the boss with the screw on the outer (-x) face.
            var rim = new List<Vector2>
            {
                new(21f, -13.2f), new(26.2f, -13.2f), new(26.2f, -12.4f), new(24.8f, -12.4f), new(24.8f, 12.4f),
                new(26.2f, 12.4f), new(26.2f, 13.2f), new(21f, 13.2f), new(21f, -13.2f),
            };
            k.Lathe(hub, Vector3.zero, axis, rim, 72, 30);
            for (int i = 0; i < 6; i++)
            {
                k.Push(Vector3.zero, Quaternion.AngleAxis(i * 60f, Vector3.right));
                k.Box(hub, new Vector3(-1f, 14f, 0), new Vector3(10f, 15f, 3.2f), 0.6f);
                k.Pop();
            }
            k.Cylinder(hub, new Vector3(-12.0f, 0, 0), new Vector3(11.0f, 0, 0), 6.5f, 40, 0.8f);
            Pieces.Disc(b, hole, new Vector3(-12.02f, 0, 0), Vector3.left, 3.3f, 0.1f, 24);
            k.Cylinder(screw, new Vector3(-12.0f, 0, 0), new Vector3(-13.3f, 0, 0), 2.3f, 24, 0.5f);
            k.Box(hole, new Vector3(-13.32f, 0, 0), new Vector3(0.06f, 2.6f, 0.5f));
            k.Box(hole, new Vector3(-13.32f, 0, 0), new Vector3(0.06f, 0.5f, 2.6f));
            Pieces.Disc(b, hole, new Vector3(11.02f, 0, 0), Vector3.right, 2.7f, 0.1f, 20);
            return b.Finish("TT wheel");
        }
    }
}
