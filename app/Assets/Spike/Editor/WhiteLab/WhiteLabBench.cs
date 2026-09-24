using System.Collections.Generic;
using CoreEngine.Spike.Parts;
using UnityEngine;

namespace CoreEngine.Spike.Editor
{
    public static partial class WhiteLab
    {
        const float BenchHalf = 1000f, BenchBack = -600f, BenchFront = 200f;
        const float ShelfY = 380f, ShelfLeft = 10f, ShelfRight = 990f, ShelfBack = -600f, ShelfFront = -330f;

        static Material Laminate => Plain("Bench laminate", new Color(0.93f, 0.93f, 0.915f), 0.42f);
        static Material PowderGrey => Plain("Powder grey", new Color(0.72f, 0.73f, 0.75f), 0.38f, 0.1f);
        static Material Brushed => Plain("Brushed aluminium", new Color(0.8f, 0.81f, 0.82f), 0.62f, 1f);
        static Material Steel => Plain("Steel", new Color(0.7f, 0.71f, 0.72f), 0.72f, 1f);
        static Material DarkPlastic => Plain("Dark plastic", new Color(0.05f, 0.05f, 0.055f), 0.45f);
        static Material GreyPlastic => Plain("Grey plastic", new Color(0.3f, 0.31f, 0.33f), 0.4f);

        /// <summary>
        /// The bench: a white laminate top 2 m wide on a grey steel frame, a drawer unit under its left end, a back
        /// panel with a power strip, and on the right half a shelf on two uprights for the instruments.
        /// </summary>
        static void Bench()
        {
            float depth = BenchFront - BenchBack, midZ = (BenchFront + BenchBack) / 2;
            Slab("BenchTop", new Vector3(0, -15, midZ), new Vector3(2 * BenchHalf, 30, depth), Laminate, 4, 2.5f);

            var frame = new MeshKit(1);
            foreach (float x in new[] { -960f, 960f })
                foreach (float z in new[] { BenchBack + 35, BenchFront - 35 })
                {
                    frame.Box(0, new Vector3(x, (FloorY - 30) / 2, z), new Vector3(50, -30 - FloorY, 50), 3);
                    frame.Box(0, new Vector3(x, FloorY + 6, z), new Vector3(70, 12, 70), 3); // levelling foot
                }
            frame.Box(0, new Vector3(0, -65, BenchFront - 35), new Vector3(1870, 60, 30), 2);
            frame.Box(0, new Vector3(0, -65, BenchBack + 35), new Vector3(1870, 60, 30), 2);
            foreach (float x in new[] { -960f, 960f }) frame.Box(0, new Vector3(x, -65, midZ), new Vector3(30, 60, depth - 120), 2);
            frame.Box(0, new Vector3(0, FloorY + 180, BenchBack + 35), new Vector3(1870, 40, 30), 2); // the stretcher low at the back
            Place("BenchFrame", frame, new[] { PowderGrey }, default, null, 0.5f);

            // The drawer unit on castors, white, with aluminium bar handles.
            var drawers = new MeshKit(3);
            float x0 = -920, x1 = -520, top = -40, bottom = FloorY + 70;
            drawers.Box(0, new Vector3((x0 + x1) / 2, (top + bottom) / 2, midZ - 20), new Vector3(x1 - x0, top - bottom, depth - 120), 3);
            float[] heights = { 150, 200, 290 };
            float y = top - 12;
            foreach (float h in heights)
            {
                drawers.Box(0, new Vector3((x0 + x1) / 2, y - h / 2, BenchFront - 58), new Vector3(x1 - x0 - 16, h - 8, 18), 2.5f);
                drawers.Cylinder(1, new Vector3((x0 + x1) / 2 - 110, y - 35, BenchFront - 40), new Vector3((x0 + x1) / 2 + 110, y - 35, BenchFront - 40), 6, 16, 1);
                foreach (float s in new[] { -95f, 95f })
                    drawers.Cylinder(1, new Vector3((x0 + x1) / 2 + s, y - 35, BenchFront - 49), new Vector3((x0 + x1) / 2 + s, y - 35, BenchFront - 40), 4, 12);
                y -= h;
            }
            foreach (float x in new[] { x0 + 40, x1 - 40 })
                foreach (float z in new[] { BenchBack + 90, BenchFront - 110 })
                {
                    drawers.Cylinder(2, new Vector3(x - 12, bottom - 45, z), new Vector3(x + 12, bottom - 45, z), 25, 20, 3);
                    drawers.Box(1, new Vector3(x, bottom - 12, z), new Vector3(40, 24, 40), 3);
                }
            Place("DrawerUnit", drawers, new[] { Plain("Drawer white", new Color(0.92f, 0.92f, 0.91f), 0.38f), Brushed, GreyPlastic }, default, null, 0.6f);

            // The back panel and its power strip, then the instrument shelf.
            Slab("BackPanel", new Vector3(0, 230, BenchBack + 8), new Vector3(2 * BenchHalf, 460, 16), Laminate, 2, 0.8f);
            Place("PowerStrip", PowerStrip(), new[] { Brushed, Plain("Socket grey", new Color(0.18f, 0.19f, 0.2f), 0.4f), Plain("Socket hole", Color.black, 0.05f), Plain("Switch red", new Color(0.8f, 0.08f, 0.06f), 0.5f) },
                new Vector3(-150, 150, BenchBack + 36), null, 0.4f);

            var shelf = new MeshKit(1);
            foreach (float x in new[] { ShelfLeft + 15, ShelfRight - 15 })
            {
                shelf.Box(0, new Vector3(x, ShelfY / 2 + 10, BenchBack + 40), new Vector3(30, ShelfY + 20, 30), 2);
                shelf.Box(0, new Vector3(x, ShelfY - 20, (ShelfBack + ShelfFront) / 2 + 20), new Vector3(30, 20, ShelfFront - ShelfBack - 40), 2);
            }
            Place("ShelfFrame", shelf, new[] { PowderGrey }, default, null, 0.4f);
            Slab("ShelfBoard", new Vector3((ShelfLeft + ShelfRight) / 2, ShelfY + 11, (ShelfBack + ShelfFront) / 2 + 5), new Vector3(ShelfRight - ShelfLeft, 22, ShelfFront - ShelfBack - 10), Laminate, 3, 1.2f);
        }

        /// <summary>An aluminium power strip with eight sockets and a red switch, facing the room (+z).</summary>
        static MeshKit PowerStrip()
        {
            var kit = new MeshKit(4);
            kit.Box(0, Vector3.zero, new Vector3(1500, 56, 40), 6);
            for (int i = 0; i < 8; i++)
            {
                float x = -560 + i * 150;
                kit.Box(1, new Vector3(x, 0, 20.5f), new Vector3(44, 44, 2), 7);
                foreach (float s in new[] { -9.5f, 9.5f }) kit.Cylinder(2, new Vector3(x + s, 0, 21.4f), new Vector3(x + s, 0, 21.6f), 2.3f, 12);
            }
            kit.Box(3, new Vector3(-700, 0, 21), new Vector3(28, 20, 4), 3);
            return kit;
        }

        /// <summary>
        /// A white pegboard on the wall behind the bench, and the tools on it: a rack of screwdrivers, pliers, side
        /// cutters, a wire stripper, scissors, tweezers, hex keys, two coils of wire and a steel rule.
        /// </summary>
        static void Pegboard()
        {
            const float x0 = -700, x1 = 700, y0 = 520, y1 = 1120, face = BackWall + 17;
            var holes = new Raster(25.4f, 25.4f, 12);
            holes.Fill(Ink.Solid(new Color32(236, 236, 234, 255), 0, 0.35f, 0));
            holes.Circle(12.7f, 12.7f, 3.4f, Ink.Solid(new Color32(170, 170, 168, 255), 0, 0.25f, -0.6f));
            holes.Circle(12.7f, 12.7f, 2.9f, Ink.Solid(new Color32(36, 36, 36, 255), 0, 0.05f, -2f));
            var board = Painted("Pegboard", holes, 0.8f, repeat: true);
            var kit = new MeshKit(1);
            kit.Box(0, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, 0), new Vector3(x1 - x0, y1 - y0, 14), 3, 1f / 25.4f);
            Place("Pegboard", kit, new[] { board }, new Vector3(0, 0, face - 7), null, 0.8f);

            var tools = new MeshKit(10);
            // slots: 0 steel, 1 red grip, 2 blue grip, 3 yellow grip, 4 black, 5 white, 6 wire red, 7 wire black, 8 green, 9 orange
            float z = face + 2;
            // The screwdriver rack: a white bar with six drivers hanging tip down.
            tools.Box(5, new Vector3(-420, 960, z + 22), new Vector3(330, 18, 44), 4);
            int[] handleColours = { 1, 3, 2, 4, 8, 9 };
            for (int i = 0; i < 6; i++)
            {
                float x = -560 + i * 55, length = 90 + i * 12;
                var handle = new List<Vector2> { new(0, 0), new(9, 0), new(11.5f, 8), new(12, 60), new(11, 88), new(7, 96), new(0, 97) };
                tools.Lathe(handleColours[i], new Vector3(x, 972, z + 22), Vector3.up, handle, 20, 40);
                tools.Cylinder(0, new Vector3(x, 972, z + 22), new Vector3(x, 960 - length, z + 22), 2.6f, 12, 0.5f);
                tools.Box(0, new Vector3(x, 960 - length - 4, z + 22), new Vector3(4.5f, 8, 0.9f), 0.3f);
            }
            // Pliers, side cutters and a wire stripper, each hanging by its pivot from a hook.
            var hanging = new (float x, int grip, float spread, float jaw)[] { (-170, 1, 16, 45), (-80, 2, 14, 38), (10, 1, 18, 22), (100, 3, 20, 30) };
            foreach (var (x, grip, spread, jaw) in hanging)
            {
                Hook(tools, x, 915, z);
                tools.Cylinder(0, new Vector3(x, 900, z + 4), new Vector3(x, 900, z + 14), 6, 16, 1);
                tools.Box(0, new Vector3(x, 900 + jaw / 2, z + 9), new Vector3(9, jaw, 8), 2);
                foreach (int side in new[] { -1, 1 })
                {
                    var path = new List<Vector3> { new(x + side * 3, 895, z + 9), new(x + side * (spread * 0.5f), 850, z + 9), new(x + side * spread, 790, z + 9), new(x + side * (spread + 4), 740, z + 9) };
                    tools.Sweep(0, path.GetRange(0, 2), 3.5f, 8);
                    tools.Sweep(grip, path.GetRange(1, 3), 6.5f, 10);
                }
            }
            // Scissors: two loops and the blades.
            Hook(tools, 205, 915, z);
            tools.Box(0, new Vector3(205, 870, z + 9), new Vector3(10, 80, 3), 1);
            foreach (int side in new[] { -1, 1 })
            {
                var loop = new List<Vector3>();
                for (int i = 0; i <= 24; i++)
                {
                    float a = Mathf.PI * 2 * i / 24;
                    loop.Add(new Vector3(205 + side * 16 + Mathf.Cos(a) * 13, 812 + Mathf.Sin(a) * 18, z + 9));
                }
                tools.Sweep(9, loop, 3.5f, 8, false);
            }
            // Tweezers and hex keys.
            Hook(tools, 290, 955, z);
            foreach (int side in new[] { -1, 1 })
                tools.Sweep(0, new List<Vector3> { new(290, 948, z + 8), new(290 + side * 5, 900, z + 8), new(290 + side * 2, 830, z + 8) }, 1.4f, 6);
            tools.Box(4, new Vector3(390, 960, z + 18), new Vector3(90, 22, 36), 4);
            for (int i = 0; i < 8; i++)
            {
                float x = 355 + i * 10, length = 60 + i * 9;
                tools.Cylinder(0, new Vector3(x, 972, z + 18), new Vector3(x, 960 - length, z + 18), 1.2f + i * 0.2f, 6);
                tools.Cylinder(0, new Vector3(x, 972, z + 18), new Vector3(x, 972, z + 18 + 20 + i * 2), 1.2f + i * 0.2f, 6);
            }
            // Two coils of hook-up wire and a steel rule.
            foreach (var (x, colour) in new[] { (-560f, 6), (-430f, 7) })
            {
                Hook(tools, x, 760, z);
                for (int turn = 0; turn < 6; turn++)
                {
                    var coil = new List<Vector3>();
                    for (int i = 0; i <= 32; i++)
                    {
                        float a = Mathf.PI * 2 * i / 32;
                        coil.Add(new Vector3(x + Mathf.Sin(a) * (48 + turn * 1.2f), 700 + Mathf.Cos(a) * (58 + turn * 1.2f), z + 8 + turn * 1.6f));
                    }
                    tools.Sweep(colour, coil, 1.2f, 6, false);
                }
            }
            Hook(tools, 560, 1030, z);
            tools.Box(0, new Vector3(560, 870, z + 6), new Vector3(26, 320, 1.2f), 0.3f);
            Place("PegboardTools", tools, new[]
            {
                Steel, Plain("Grip red", new Color(0.78f, 0.1f, 0.08f), 0.45f), Plain("Grip blue", new Color(0.1f, 0.3f, 0.75f), 0.45f),
                Plain("Grip yellow", new Color(0.95f, 0.72f, 0.08f), 0.45f), DarkPlastic, Plain("Rack white", new Color(0.93f, 0.93f, 0.92f), 0.4f),
                Plain("Wire red", new Color(0.8f, 0.07f, 0.06f), 0.5f), Plain("Wire black", new Color(0.05f, 0.05f, 0.05f), 0.5f),
                Plain("Grip green", new Color(0.15f, 0.55f, 0.25f), 0.45f), Plain("Grip orange", new Color(0.95f, 0.45f, 0.08f), 0.45f),
            }, default, null, 0.3f);
        }

        static void Hook(MeshKit kit, float x, float y, float z)
        {
            kit.Cylinder(0, new Vector3(x, y, z - 2), new Vector3(x, y, z + 22), 1.6f, 8);
            kit.Cylinder(0, new Vector3(x, y, z + 22), new Vector3(x, y + 8, z + 26), 1.6f, 8);
        }
    }
}
