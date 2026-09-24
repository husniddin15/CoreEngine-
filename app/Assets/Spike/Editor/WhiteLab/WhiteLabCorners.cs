using System.Collections.Generic;
using CoreEngine.Spike.Parts;
using UnityEngine;

namespace CoreEngine.Spike.Editor
{
    public static partial class WhiteLab
    {
        static readonly string[] BinNames =
        {
            "RESISTORS", "CAPACITORS", "LEDS", "SENSORS", "SERVOS", "MOTORS", "JUMPERS", "M3 SCREWS", "STANDOFFS", "BATTERIES", "BOARDS", "WHEELS",
        };

        /// <summary>
        /// White steel shelving on the left wall with labelled grey bins (resistors, LEDs, servos, motors…) and two
        /// cardboard boxes on top.
        /// </summary>
        static void Shelves()
        {
            const float x0 = LeftWall + 20, x1 = LeftWall + 400, z0 = 250, z1 = 1300;
            var labels = new Raster(4 * 100, 3 * 60, 3);
            labels.Fill(Ink.Solid(new Color32(92, 96, 102, 255), 0, 0.4f, 0));
            for (int i = 0; i < BinNames.Length; i++)
            {
                float cx = (i % 4) * 100 + 50, cy = (i / 4) * 60 + 30;
                labels.RoundRect(cx, cy + 6, 70, 22, 2, Ink.Solid(new Color32(240, 240, 236, 255), 0, 0.3f, 0.2f));
                labels.Text(BinNames[i], cx, cy + 2.5f, 7, Label(new Color32(30, 32, 36, 255)), Align.Centre, 0, 0.14f);
                labels.RoundRect(cx, cy - 16, 40, 8, 3, Ink.Solid(new Color32(70, 74, 80, 255), 0, 0.35f, -0.8f)); // the finger pull
            }
            var binFront = Painted("Bin labels", labels, 0.5f);
            var grey = Plain("Bin grey", new Color(0.4f, 0.42f, 0.45f), 0.4f);
            var white = Plain("Shelf white", new Color(0.9f, 0.9f, 0.89f), 0.4f);
            var kit = new MeshKit(4);
            foreach (float x in new[] { x0 + 15, x1 - 15 })
                foreach (float z in new[] { z0 + 15, z1 - 15 })
                    kit.Box(0, new Vector3(x, FloorY + 950, z), new Vector3(30, 1900, 30), 3);
            float[] levels = { FloorY + 80, FloorY + 480, FloorY + 880, FloorY + 1280, FloorY + 1680 };
            int bin = 0;
            foreach (float y in levels)
            {
                kit.Box(0, new Vector3((x0 + x1) / 2, y, (z0 + z1) / 2), new Vector3(x1 - x0, 18, z1 - z0), 2);
                if (y > FloorY + 1600) continue;
                for (int k = 0; k < 3 && bin < BinNames.Length; k++, bin++)
                {
                    float z = z0 + 60 + 150 + k * 330;
                    int col = bin % 4, row = bin / 4;
                    var uv = new Rect(col * 0.25f, row / 3f, 0.25f, 1 / 3f);
                    kit.Box(1, new Vector3(x1 - 200, y + 9 + 90, z), new Vector3(360, 180, 290), 6, 0.001f, uv, 2, MeshKit.Face.PlusX);
                }
            }
            // Two parts boxes on the top shelf.
            kit.Box(3, new Vector3(x1 - 190, levels[4] + 9 + 110, z0 + 250), new Vector3(330, 220, 380), 3);
            kit.Box(3, new Vector3(x1 - 200, levels[4] + 9 + 80, z0 + 700), new Vector3(300, 160, 300), 3);
            Place("PartsShelves", kit, new[] { white, grey, binFront, Plain("Cardboard", new Color(0.62f, 0.47f, 0.32f), 0.25f) }, default, null, 0.4f);
        }

        static void Corners()
        {
            Whiteboard();
            Poster();
            Door();
            Clock();
            Stool(new Vector3(520, FloorY, 760));
            Printer();
        }

        /// <summary>A whiteboard on the left wall with the H-bridge drawn out, the robot seen from above and a to-do list.</summary>
        static void Whiteboard()
        {
            const float w = 1300, h = 900;
            var art = new Raster(w, h, 1f);
            art.Fill(Ink.Solid(new Color32(246, 247, 248, 255), 0, 0.82f, 0));
            art.Grain(0.01f, 0.04f, 20, 5);
            var blue = Ink.Paint(new Color32(30, 70, 170, 255), 0, 0.5f);
            var red = Ink.Paint(new Color32(190, 40, 40, 255), 0, 0.5f);
            var black = Ink.Paint(new Color32(30, 30, 34, 255), 0, 0.5f);
            var green = Ink.Paint(new Color32(30, 130, 70, 255), 0, 0.5f);
            // The H-bridge: four switches round the motor, the supply above, ground below.
            float cx = 330, cy = 470;
            art.Line(cx - 160, cy + 220, cx + 160, cy + 220, 5, red);
            art.Line(cx - 160, cy - 220, cx + 160, cy - 220, 5, black);
            art.Text("+6V", cx - 20, cy + 240, 34, red);
            art.Text("GND", cx - 30, cy - 280, 34, black);
            foreach (int side in new[] { -1, 1 })
            {
                float x = cx + side * 160;
                art.Line(x, cy + 220, x, cy + 120, 5, blue);
                art.Line(x, cy + 120, x + side * 40, cy + 60, 5, blue);   // a switch drawn open
                art.Line(x, cy + 40, x, cy - 40, 5, blue);
                art.Line(x, cy - 40, x + side * 40, cy - 100, 5, blue);
                art.Line(x, cy - 120, x, cy - 220, 5, blue);
                art.Line(x, cy, cx + side * 60, cy, 5, blue);
            }
            art.Ring(cx, cy, 60, 55, black);
            art.Text("M", cx - 18, cy - 16, 40, black);
            art.Text("IN1", cx - 280, cy + 70, 26, green);
            art.Text("IN2", cx - 280, cy - 90, 26, green);
            art.Text("IN3", cx + 205, cy + 70, 26, green);
            art.Text("IN4", cx + 205, cy - 90, 26, green);
            art.Text("L298N  H-BRIDGE", cx - 150, h - 80, 36, blue);
            // The robot from above: wheels, the sonar's cone.
            float rx = 900, ry = 520;
            art.Line(rx - 90, ry - 140, rx + 90, ry - 140, 5, black);
            art.Line(rx + 90, ry - 140, rx + 90, ry + 140, 5, black);
            art.Line(rx + 90, ry + 140, rx - 90, ry + 140, 5, black);
            art.Line(rx - 90, ry + 140, rx - 90, ry - 140, 5, black);
            foreach (int side in new[] { -1, 1 }) art.Line(rx + side * 110, ry - 60, rx + side * 110, ry + 20, 18, black);
            art.Line(rx, ry + 150, rx - 110, ry + 330, 4, red);
            art.Line(rx, ry + 150, rx + 110, ry + 330, 4, red);
            art.Text("15°", rx + 30, ry + 250, 28, red);
            art.Text("< 20 cm: TURN", rx - 150, ry - 250, 30, blue);
            art.Text("TODO", 700, 180, 30, black);
            art.Text("- calibrate turn time", 700, 140, 24, black);
            art.Text("- servo radar sweep", 700, 105, 24, black);
            var board = Painted("Whiteboard", art, 0.2f);
            var kit = new MeshKit(3);
            kit.Box(1, new Vector3(0, 0, 0), new Vector3(12, h, w), 1, 0.001f, new Rect(0, 0, 1, 1), 1, MeshKit.Face.PlusX);
            foreach (int side in new[] { -1, 1 })
            {
                kit.Box(0, new Vector3(4, side * (h / 2 + 10), 0), new Vector3(22, 20, w + 40), 4);
                kit.Box(0, new Vector3(4, 0, side * (w / 2 + 10)), new Vector3(22, h, 20), 4);
            }
            kit.Box(0, new Vector3(35, -h / 2 - 15, 0), new Vector3(60, 10, w * 0.6f), 3); // the tray
            foreach (var (z, slot) in new[] { (-120f, 2), (-60f, 2), (40f, 2) })
                kit.Cylinder(slot, new Vector3(40, -h / 2 - 3, z - 60), new Vector3(40, -h / 2 - 3, z + 60), 8, 16, 3);
            Place("Whiteboard", kit, new[] { Brushed, board, Plain("Marker", new Color(0.1f, 0.2f, 0.6f), 0.5f) }, new Vector3(LeftWall + 16, 850, 2250), null, 0.4f);
        }

        /// <summary>An Uno pinout poster in a thin black frame on the wall opposite the bench.</summary>
        static void Poster()
        {
            const float w = 600, h = 850;
            var art = new Raster(w, h, 1.4f);
            art.Fill(Ink.Solid(new Color32(244, 244, 240, 255), 0, 0.3f, 0));
            art.Text("UNO R3", w / 2, h - 90, 56, Label(new Color32(0, 110, 120, 255)), Align.Centre, 0, 0.16f);
            art.Text("PINOUT", w / 2, h - 150, 36, Label(new Color32(40, 44, 50, 255)), Align.Centre);
            art.RoundRect(w / 2, 390, 300, 420, 12, Ink.Paint(new Color32(0, 118, 128, 255), 0, 0.4f));
            art.RoundRect(w / 2 - 150, 520, 60, 70, 4, Ink.Paint(new Color32(200, 200, 196, 255), 0, 0.4f));
            art.RoundRect(w / 2 + 40, 330, 90, 30, 3, Ink.Paint(new Color32(24, 24, 26, 255), 0, 0.4f));
            Color32[] kinds = { new Color32(46, 160, 80, 255), new Color32(230, 130, 30, 255), new Color32(210, 40, 40, 255), new Color32(230, 200, 40, 255) };
            for (int i = 0; i < 14; i++)
            {
                float y = 570 - i * 26;
                art.RoundRect(w / 2 + 200, y, 80, 20, 4, Ink.Paint(i % 3 == 1 ? kinds[1] : kinds[0], 0, 0.4f));
                art.Text("D" + (13 - i), w / 2 + 200, y - 6, 12, Label(new Color32(255, 255, 255, 255)), Align.Centre);
                art.Line(w / 2 + 150, y, w / 2 + 160, y, 2, Label(new Color32(80, 80, 80, 255)));
            }
            string[] power = { "IOREF", "RESET", "3.3V", "5V", "GND", "GND", "VIN" };
            for (int i = 0; i < power.Length; i++)
            {
                float y = 570 - i * 26;
                art.RoundRect(w / 2 - 205, y, 80, 20, 4, Ink.Paint(kinds[2], 0, 0.4f));
                art.Text(power[i], w / 2 - 205, y - 6, 11, Label(new Color32(255, 255, 255, 255)), Align.Centre);
            }
            for (int i = 0; i < 6; i++)
            {
                float y = 360 - i * 26;
                art.RoundRect(w / 2 - 205, y, 80, 20, 4, Ink.Paint(kinds[3], 0, 0.4f));
                art.Text("A" + i, w / 2 - 205, y - 6, 12, Label(new Color32(40, 40, 40, 255)), Align.Centre);
            }
            art.Text("~ PWM    ANALOG IN    POWER", w / 2, 90, 18, Label(new Color32(60, 64, 70, 255)), Align.Centre);
            var poster = Painted("Pinout poster", art, 0.2f);
            var kit = new MeshKit(2);
            kit.Box(1, Vector3.zero, new Vector3(w, h, 4), 0, 0.001f, new Rect(0, 0, 1, 1), 1, MeshKit.Face.MinusZ);
            foreach (int side in new[] { -1, 1 })
            {
                kit.Box(0, new Vector3(0, side * (h / 2 + 6), 4), new Vector3(w + 24, 12, 16), 1);
                kit.Box(0, new Vector3(side * (w / 2 + 6), 0, 4), new Vector3(12, h, 16), 1);
            }
            Place("PinoutPoster", kit, new[] { Plain("Frame black", new Color(0.05f, 0.05f, 0.05f), 0.5f), poster }, new Vector3(-900, 900, FrontWall - 12), null, 0.4f);
        }

        static void Door()
        {
            var kit = new MeshKit(3);
            kit.Box(0, new Vector3(0, 1050, 0), new Vector3(900, 2100, 40), 2);
            foreach (int side in new[] { -1, 1 }) kit.Box(1, new Vector3(side * 480, 1070, 10), new Vector3(60, 2140, 60), 2);
            kit.Box(1, new Vector3(0, 2150, 10), new Vector3(1020, 60, 60), 2);
            kit.Cylinder(2, new Vector3(-360, 1050, -20), new Vector3(-360, 1050, -65), 9, 16, 2);
            kit.Cylinder(2, new Vector3(-360, 1050, -65), new Vector3(-240, 1050, -65), 9, 16, 3);
            Place("Door", kit, new[] { Plain("Door white", new Color(0.93f, 0.93f, 0.92f), 0.35f), Plain("Door frame", new Color(0.7f, 0.71f, 0.72f), 0.4f), Brushed },
                new Vector3(1300, FloorY, FrontWall - 20), null, 0.4f);
        }

        static void Clock()
        {
            var dial = new Raster(300, 300, 2);
            dial.Fill(Ink.Solid(new Color32(248, 248, 246, 255), 0, 0.5f, 0));
            var ink = Label(new Color32(30, 30, 32, 255));
            for (int m = 0; m < 60; m++)
            {
                float a = m * 6 * Mathf.Deg2Rad, inner = m % 5 == 0 ? 118 : 128;
                dial.Line(150 + Mathf.Sin(a) * inner, 150 + Mathf.Cos(a) * inner, 150 + Mathf.Sin(a) * 136, 150 + Mathf.Cos(a) * 136, m % 5 == 0 ? 4 : 1.5f, ink);
            }
            // Hands at ten past ten.
            dial.Line(150, 150, 150 + Mathf.Sin(-60 * Mathf.Deg2Rad) * 70, 150 + Mathf.Cos(-60 * Mathf.Deg2Rad) * 70, 8, ink);
            dial.Line(150, 150, 150 + Mathf.Sin(60 * Mathf.Deg2Rad) * 110, 150 + Mathf.Cos(60 * Mathf.Deg2Rad) * 110, 5, ink);
            dial.Circle(150, 150, 7, ink);
            var face = Painted("Clock face", dial, 0.1f);
            var kit = new MeshKit(2);
            kit.Cylinder(0, new Vector3(0, 0, 0), new Vector3(0, 0, 36), 162, 64, 6);
            FrontDisc(kit, 1, new Vector3(0, 0, 36.5f), 150, 64);
            Place("Clock", kit, new[] { Plain("Clock rim", new Color(0.9f, 0.9f, 0.9f), 0.5f), face }, new Vector3(-1150, 1480, BackWall + 2), null, 0.3f);
        }

        /// <summary>
        /// A flat disc facing the room (+z) with its texture upright as seen from there: seen from +z the frame's +x is
        /// on the left, so the texture's u runs toward -x.
        /// </summary>
        static void FrontDisc(MeshKit kit, int slot, Vector3 centre, float radius, int segments)
        {
            int middle = kit.Vertex(centre, Vector3.forward, new Vector2(0.5f, 0.5f));
            int rim = kit.VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2 * i / segments, x = Mathf.Cos(a), y = Mathf.Sin(a);
                kit.Vertex(centre + new Vector3(x, y, 0) * radius, Vector3.forward, new Vector2(0.5f - x * 0.5f, 0.5f + y * 0.5f));
            }
            for (int i = 0; i < segments; i++) kit.Triangle(slot, middle, rim + i, rim + i + 1);
        }

        /// <summary>A lab stool: five-star base, gas lift and a round grey seat.</summary>
        static void Stool(Vector3 at)
        {
            var kit = new MeshKit(3);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72 * Mathf.Deg2Rad;
                var tip = new Vector3(Mathf.Sin(a) * 300, 70, Mathf.Cos(a) * 300);
                kit.Sweep(0, new List<Vector3> { new(0, 95, 0), tip }, 14, 8);
                kit.Sphere(2, tip + Vector3.down * 38, 26, 16);
            }
            kit.Cylinder(0, new Vector3(0, 90, 0), new Vector3(0, 420, 0), 24, 24, 2);
            kit.Cylinder(1, new Vector3(0, 420, 0), new Vector3(0, 560, 0), 16, 24, 2);
            kit.Lathe(2, new Vector3(0, 560, 0), Vector3.up, new List<Vector2> { new(0, 0), new(170, 0), new(185, 20), new(185, 55), new(172, 72), new(0, 76) }, 48, 40);
            Place("Stool", kit, new[] { PowderGrey, Brushed, Plain("Seat grey", new Color(0.25f, 0.26f, 0.28f), 0.3f) }, at, null, 0.3f);
        }

        /// <summary>A 3D printer on a white side cabinet: black frame, bed with a print on it, gantry, hot end, a spool of orange filament.</summary>
        static void Printer()
        {
            var cabinet = new Vector3(1900, FloorY, 2450);
            Slab("PrinterCabinet", cabinet + new Vector3(0, 370, 0), new Vector3(760, 740, 560), Plain("Cabinet white", new Color(0.92f, 0.92f, 0.91f), 0.35f), 4, 0.4f);
            var kit = new MeshKit(6);
            var at = cabinet + new Vector3(-40, 740, 0);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    kit.Box(0, at + new Vector3(sx * 200, 250, sz * 200), new Vector3(24, 500, 24), 2);
            foreach (float y in new[] { 12f, 488f })
                foreach (int s in new[] { -1, 1 })
                {
                    kit.Box(0, at + new Vector3(0, y, s * 200), new Vector3(424, 24, 24), 2);
                    kit.Box(0, at + new Vector3(s * 200, y, 0), new Vector3(24, 24, 424), 2);
                }
            kit.Box(1, at + new Vector3(0, 150, 0), new Vector3(300, 8, 300), 2);                 // the bed
            kit.Box(2, at + new Vector3(0, 175, 0), new Vector3(90, 42, 60), 6);                  // a print on it
            kit.Box(0, at + new Vector3(0, 380, 0), new Vector3(400, 20, 30), 2);                 // the gantry
            kit.Box(3, at + new Vector3(30, 350, 0), new Vector3(50, 60, 45), 4);                 // the hot end
            kit.Cylinder(3, at + new Vector3(30, 320, 0), at + new Vector3(30, 306, 0), 3, 12, 1);
            kit.Cylinder(4, at + new Vector3(-205, 440, 0), at + new Vector3(-275, 440, 0), 100, 40, 6);   // the spool
            kit.Cylinder(0, at + new Vector3(-190, 440, 0), at + new Vector3(-290, 440, 0), 12, 16, 2);
            kit.Box(5, at + new Vector3(-213, 60, 120), new Vector3(2, 50, 90), 0);               // the screen, on the side
            Place("Printer", kit, new[]
            {
                Plain("Frame black", new Color(0.05f, 0.05f, 0.05f), 0.5f), Plain("Bed glass", new Color(0.08f, 0.08f, 0.09f), 0.9f),
                Plain("Print white", new Color(0.92f, 0.92f, 0.9f), 0.35f), Brushed, Plain("Filament orange", new Color(0.95f, 0.45f, 0.08f), 0.55f),
                Glow("Printer screen", new Color(0.35f, 0.6f, 0.95f), 1.2f),
            }, default, null, 0.3f);
        }
    }
}
