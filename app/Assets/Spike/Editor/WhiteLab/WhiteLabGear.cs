using System.Collections.Generic;
using CoreEngine.Spike.Parts;
using UnityEngine;

namespace CoreEngine.Spike.Editor
{
    public static partial class WhiteLab
    {
        /// <summary>The LED lamp's head, over the turntable (mm): its spot light shines from just under it.</summary>
        static readonly Vector3 LampHead = new Vector3(-40, 440, -170);

        static readonly Color32 PanelDark = new Color32(38, 40, 44, 255);
        static Ink Label(Color32 colour) => Ink.Paint(colour, 0, 0.35f);
        static readonly Color32 LabelWhite = new Color32(226, 228, 230, 255);

        // ------------------------------------------------------------------ instruments

        static void Instruments()
        {
            Oscilloscope(new Vector3(310, ShelfY + 22, -415));
            PowerSupply(new Vector3(770, ShelfY + 22, -460));
            SolderingStation(new Vector3(840, 0, -440));
            Monitor(new Vector3(-560, 0, -430));
            Lamp();
        }

        /// <summary>A bench oscilloscope: grey case, dark front with the screen on the left, knobs, buttons and four BNCs.</summary>
        static void Oscilloscope(Vector3 foot)
        {
            const float w = 360, h = 170, d = 130;
            var panelArt = new Raster(w, h, 3);
            panelArt.Fill(Ink.Solid(PanelDark, 0, 0.4f, 0));
            var label = Label(LabelWhite);
            panelArt.Text("DIGITAL OSCILLOSCOPE", 12, 157, 4.2f, label);
            panelArt.Text("100 MHz  1 GSa/s", 12, 150, 3.4f, Label(new Color32(150, 154, 160, 255)));
            string[] buttons = { "RUN/STOP", "SINGLE", "AUTO", "MENU", "MEASURE", "CURSOR", "TRIGGER", "HELP" };
            for (int i = 0; i < buttons.Length; i++)
            {
                float bx = 214 + (i % 4) * 36, by = 130 - (i / 4) * 22;
                panelArt.RoundRect(bx + 13, by + 5, 28, 12, 2.5f, Ink.Solid(new Color32(60, 63, 68, 255), 0, 0.5f, 0.6f));
                panelArt.Text(buttons[i], bx + 13, by - 6, 2.1f, label, Align.Centre);
            }
            panelArt.RoundRect(230, 50, 34, 22, 3, Ink.Solid(new Color32(198, 42, 36, 255), 0, 0.5f, 0.6f));
            panelArt.Text("CH1", 238, 20, 3, Label(new Color32(236, 200, 60, 255)), Align.Centre);
            panelArt.Text("CH2", 272, 20, 3, Label(new Color32(80, 200, 230, 255)), Align.Centre);
            panelArt.Text("CH3", 306, 20, 3, Label(new Color32(220, 90, 200, 255)), Align.Centre);
            panelArt.Text("CH4", 340, 20, 3, Label(new Color32(90, 140, 240, 255)), Align.Centre);
            var panel = Painted("Scope panel", panelArt, 0.6f);

            var screenArt = new Raster(160, 92, 7);
            screenArt.Fill(Ink.Solid(new Color32(6, 8, 12, 255), 0, 0.8f, 0));
            var grid = Ink.Paint(new Color32(52, 58, 66, 255), 0, 0.8f);
            for (int i = 0; i <= 12; i++) screenArt.Line(8 + i * 12, 8, 8 + i * 12, 80, i == 6 ? 0.35f : 0.18f, grid);
            for (int i = 0; i <= 8; i++) screenArt.Line(8, 8 + i * 9, 152, 8 + i * 9, i == 4 ? 0.35f : 0.18f, grid);
            var sine = new List<Vector2>();
            for (int i = 0; i <= 288; i++) sine.Add(new Vector2(8 + i * 0.5f, 56 + 13 * Mathf.Sin(i * 0.5f / 144 * Mathf.PI * 2 * 2.5f)));
            screenArt.Polyline(sine, 0.8f, Ink.Paint(new Color32(250, 220, 70, 255), 0, 0.8f));
            var square = new List<Vector2>();
            for (int k = 0; k < 6; k++)
            {
                float x = 8 + k * 24;
                square.Add(new Vector2(x, 22));
                square.Add(new Vector2(x, 34));
                square.Add(new Vector2(x + 12, 34));
                square.Add(new Vector2(x + 12, 22));
            }
            square.Add(new Vector2(152, 22));
            screenArt.Polyline(square, 0.8f, Ink.Paint(new Color32(80, 215, 240, 255), 0, 0.8f));
            screenArt.Rect(0, 82, 160, 92, Ink.Paint(new Color32(24, 30, 40, 255), 0, 0.8f));
            screenArt.Text("RUN", 4, 84.5f, 4, Label(new Color32(90, 220, 110, 255)));
            screenArt.Text("H 500us   T 1.20V   f = 1.000 kHz", 26, 85, 3.2f, Label(new Color32(210, 214, 220, 255)));
            screenArt.Text("CH1 2.00V", 6, 2, 3, Label(new Color32(250, 220, 70, 255)));
            screenArt.Text("CH2 5.00V", 44, 2, 3, Label(new Color32(80, 215, 240, 255)));
            var screen = Painted("Scope screen", screenArt, 0, glow: 1.4f);

            var kit = new MeshKit(6);
            kit.Box(0, new Vector3(0, h / 2 + 8, -d / 2), new Vector3(w, h, d), 8, 0.001f);
            kit.Box(1, new Vector3(0, h / 2 + 8, 1.5f), new Vector3(w - 6, h - 6, 3), 4, 0.001f, new Rect(0, 0, 1, 1), 1, MeshKit.Face.PlusZ);
            // Seen from the front (+z) the frame's +x is on the left: the panel art's x is 180 - frame x.
            kit.Quad(2, new Vector3(170, 36, 3.2f), new Vector3(10, 36, 3.2f), new Vector3(10, 128, 3.2f), new Vector3(170, 128, 3.2f), Vector3.forward);
            foreach (var (x, y, r) in new[] { (-75f, 88f, 7f), (-105f, 80f, 10f), (-145f, 80f, 10f) })
            {
                kit.Cylinder(3, new Vector3(x, y, 3), new Vector3(x, y, 3 + r * 1.2f), r, 24, 1.2f);
                kit.Box(4, new Vector3(x, y + r * 0.55f, 3 + r * 1.2f + 0.2f), new Vector3(1.2f, r * 0.7f, 0.4f));
            }
            for (int i = 0; i < 4; i++)
            {
                float x = 180 - (238 + i * 34);
                kit.Cylinder(5, new Vector3(x, 32, 3), new Vector3(x, 32, 16), 5.5f, 20, 0.8f);
                kit.Cylinder(3, new Vector3(x, 32, 3), new Vector3(x, 32, 8), 7, 20, 1);
            }
            foreach (float x in new[] { -150f, 150f })
                foreach (float z in new[] { -d + 15, -15f })
                    kit.Box(3, new Vector3(x, 4, z), new Vector3(30, 8, 16), 3);
            Place("Oscilloscope", kit, new[] { Plain("Instrument grey", new Color(0.78f, 0.79f, 0.8f), 0.45f), panel, screen, DarkPlastic,
                Plain("Index white", Color.white, 0.4f), Plain("BNC nickel", new Color(0.82f, 0.82f, 0.8f), 0.8f, 1f) }, foot + new Vector3(0, 0, d / 2), null, 0.6f);
        }

        /// <summary>A bench power supply: two glowing displays (12.00 V, 0.235 A), two knobs and three binding posts.</summary>
        static void PowerSupply(Vector3 foot)
        {
            const float w = 140, h = 165, d = 260;
            var art = new Raster(w, h, 3);
            art.Fill(Ink.Solid(new Color32(222, 224, 226, 255), 0, 0.45f, 0));
            var dark = Label(new Color32(40, 42, 46, 255));
            art.Text("DC POWER SUPPLY  0-30V 0-5A", w / 2, 154, 3.2f, dark, Align.Centre);
            art.Text("VOLTAGE", 38, 60, 3, dark, Align.Centre);
            art.Text("CURRENT", 102, 60, 3, dark, Align.Centre);
            art.Text("CV", 20, 104, 2.6f, dark, Align.Centre);
            art.Text("CC", 120, 104, 2.6f, dark, Align.Centre);
            art.Circle(28, 104 + 1.2f, 1.6f, Ink.Paint(new Color32(40, 210, 70, 255), 0, 0.5f));
            art.Circle(112, 104 + 1.2f, 1.6f, Ink.Paint(new Color32(90, 30, 30, 255), 0, 0.5f));
            var panel = Painted("PSU panel", art, 0.4f);

            var displays = new Raster(120, 30, 8);
            displays.Fill(Ink.Solid(new Color32(10, 10, 10, 255), 0, 0.85f, 0));
            SevenSegment(displays, "12.00", 6, 6, 18, new Color32(255, 60, 40, 255));
            SevenSegment(displays, "0.235", 64, 6, 18, new Color32(60, 255, 90, 255));
            var screen = Painted("PSU displays", displays, 0, glow: 1.6f);

            var kit = new MeshKit(8);
            kit.Box(0, new Vector3(0, h / 2 + 6, -d / 2), new Vector3(w, h, d), 6, 0.001f);
            kit.Box(1, new Vector3(0, h / 2 + 6, 1.5f), new Vector3(w - 6, h - 6, 3), 3, 0.001f, new Rect(0, 0, 1, 1), 1, MeshKit.Face.PlusZ);
            kit.Quad(2, new Vector3(60, 118, 3.2f), new Vector3(-60, 118, 3.2f), new Vector3(-60, 148, 3.2f), new Vector3(60, 148, 3.2f), Vector3.forward);
            foreach (float x in new[] { 32f, -32f })
            {
                kit.Cylinder(3, new Vector3(x, 90, 3), new Vector3(x, 90, 20), 13, 28, 2);
                kit.Box(4, new Vector3(x, 98, 20.2f), new Vector3(1.6f, 8, 0.4f));
            }
            foreach (var (x, slot) in new[] { (25f, 5), (-5f, 6), (-35f, 7) })
            {
                kit.Cylinder(slot, new Vector3(x, 34, 3), new Vector3(x, 34, 15), 7.5f, 6, 0.8f);
                kit.Cylinder(0, new Vector3(x, 34, 15), new Vector3(x, 34, 22), 2.2f, 12, 0.4f);
            }
            kit.Box(3, new Vector3(52, 34, 6), new Vector3(16, 22, 8), 2);
            Place("PowerSupply", kit, new[] { Plain("PSU case", new Color(0.84f, 0.85f, 0.86f), 0.5f), panel, screen, DarkPlastic, Plain("Index white", Color.white, 0.4f),
                Plain("Post red", new Color(0.78f, 0.08f, 0.06f), 0.55f), Plain("Post black", new Color(0.05f, 0.05f, 0.05f), 0.55f),
                Plain("Post green", new Color(0.1f, 0.5f, 0.2f), 0.55f) }, foot + new Vector3(0, 0, d / 2), null, 0.5f);
        }

        /// <summary>Seven-segment digits (and a point) in a colour, for the instruments' displays.</summary>
        static void SevenSegment(Raster r, string text, float x, float y, float height, Color32 colour)
        {
            var on = Ink.Paint(colour, 0, 0.8f);
            var off = Ink.Paint(new Color32((byte)(colour.r / 8), (byte)(colour.g / 8), (byte)(colour.b / 8), 255), 0, 0.8f);
            float w = height * 0.55f, t = height * 0.12f;
            // segments a..g as lines in a 1 × 2 box
            (float, float, float, float)[] segments = { (0, 2, 1, 2), (1, 2, 1, 1), (1, 1, 1, 0), (0, 0, 1, 0), (0, 0, 0, 1), (0, 1, 0, 2), (0, 1, 1, 1) };
            string[] lit = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
            foreach (char c in text)
            {
                if (c == '.')
                {
                    r.Circle(x - w * 0.18f, y + t * 0.6f, t * 0.6f, on);
                    continue;
                }
                string segs = c >= '0' && c <= '9' ? lit[c - '0'] : "";
                for (int i = 0; i < 7; i++)
                {
                    var (ax, ay, bx, by) = segments[i];
                    float skew = 0.12f * height;
                    float x0 = x + ax * w + ay / 2 * skew * 0.5f, y0 = y + ay * height / 2, x1 = x + bx * w + by / 2 * skew * 0.5f, y1 = y + by * height / 2;
                    r.Line(x0, y0, x1, y1, t, segs.IndexOf((char)('a' + i)) >= 0 ? on : off);
                }
                x += w + height * 0.3f;
            }
        }

        /// <summary>A soldering station with its 350 °C display, and the iron resting in its stand with the brass wool.</summary>
        static void SolderingStation(Vector3 foot)
        {
            const float w = 120, h = 100, d = 170;
            var art = new Raster(w, h, 3);
            art.Fill(Ink.Solid(new Color32(44, 46, 50, 255), 0, 0.45f, 0));
            art.Text("SOLDERING STATION", w / 2, 88, 3.4f, Label(LabelWhite), Align.Centre);
            art.Text("°C", 110, 60, 5, Label(new Color32(255, 80, 60, 255)), Align.Centre);
            art.Text("SET", 30, 8, 3, Label(LabelWhite), Align.Centre);
            var panel = Painted("Station panel", art, 0.4f);
            var digits = new Raster(60, 26, 8);
            digits.Fill(Ink.Solid(new Color32(12, 8, 8, 255), 0, 0.85f, 0));
            SevenSegment(digits, "350", 5, 4, 18, new Color32(255, 70, 40, 255));
            var display = Painted("Station display", digits, 0, glow: 1.6f);

            var kit = new MeshKit(8);
            kit.Box(0, new Vector3(0, h / 2, -d / 2), new Vector3(w, h, d), 6);
            kit.Box(1, new Vector3(0, h / 2, 1.5f), new Vector3(w - 6, h - 6, 3), 3, 0.001f, new Rect(0, 0, 1, 1), 1, MeshKit.Face.PlusZ);
            kit.Quad(2, new Vector3(18, 52, 3.2f), new Vector3(-42, 52, 3.2f), new Vector3(-42, 78, 3.2f), new Vector3(18, 78, 3.2f), Vector3.forward);
            foreach (float x in new[] { 38f, 20f }) kit.Cylinder(3, new Vector3(x, 22, 3), new Vector3(x, 22, 9), 5, 16, 1);
            kit.Cylinder(3, new Vector3(-35, 22, 3), new Vector3(-35, 22, 12), 8, 20, 1.2f); // the iron's socket
            // The stand, beside the station: a heavy base, the brass wool cup and the spring holder with the iron in it.
            var stand = new Vector3(-165, 0, -30);
            kit.Box(3, stand + new Vector3(0, 8, 0), new Vector3(110, 16, 90), 6);
            kit.Cylinder(4, stand + new Vector3(-28, 16, 12), stand + new Vector3(-28, 40, 12), 24, 28, 2);
            kit.Cylinder(7, stand + new Vector3(-28, 38, 12), stand + new Vector3(-28, 42, 12), 21, 28, 3);
            var spring = new List<Vector3>();
            var axis = new Vector3(0.55f, 0.55f, 0.63f).normalized;
            var side = Vector3.Cross(axis, Vector3.up).normalized;
            var up = Vector3.Cross(side, axis);
            var springFoot = stand + new Vector3(18, 18, -12);
            for (int i = 0; i <= 160; i++)
            {
                float t = i / 160f, a = t * Mathf.PI * 2 * 9, radius = Mathf.Lerp(9, 17, t);
                spring.Add(springFoot + axis * (t * 80) + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
            }
            kit.Sweep(5, spring, 1.1f, 6);
            var ironBack = springFoot + axis * 118;
            var ironTip = springFoot + axis * -10;
            kit.Push(ironBack, Quaternion.FromToRotation(Vector3.up, -axis));
            var handle = new List<Vector2> { new(0, 0), new(5, 0), new(8, 10), new(10, 40), new(11, 70), new(8.5f, 95), new(6, 100) };
            kit.Lathe(6, Vector3.zero, Vector3.up, handle, 24, 40);
            kit.Pop();
            kit.Cylinder(5, ironBack - axis * 100, ironTip, 2.4f, 16, 0.6f);
            var cable = new List<Vector3> { ironBack, ironBack + axis * 30 + Vector3.up * 5, new Vector3(-60, 30, -120), new Vector3(-35, 22, 10) };
            kit.Sweep(3, cable, 2.5f, 8);
            Place("SolderingStation", kit, new[]
            {
                Plain("Station case", new Color(0.18f, 0.19f, 0.2f), 0.45f), panel, display, DarkPlastic,
                Plain("Brass wool cup", new Color(0.55f, 0.56f, 0.58f), 0.6f, 1f), Steel, Plain("Iron handle", new Color(0.15f, 0.35f, 0.6f), 0.45f),
                Plain("Brass wool", new Color(0.85f, 0.66f, 0.32f), 0.4f, 1f),
            }, foot + new Vector3(0, 0, d / 2), null, 0.5f);
        }

        /// <summary>A 27-inch monitor on its stand, turned toward the bench, showing the obstacle-avoider sketch in an editor.</summary>
        static void Monitor(Vector3 foot)
        {
            const float sw = 596, sh = 336;
            var code = new Raster(sw, sh, 2);
            code.Fill(Ink.Solid(new Color32(22, 25, 30, 255), 0, 0.9f, 0));
            code.Rect(0, sh - 22, sw, sh, Ink.Paint(new Color32(34, 38, 46, 255), 0, 0.9f));
            code.Rect(0, 0, 150, sh - 22, Ink.Paint(new Color32(28, 31, 38, 255), 0, 0.9f));
            code.Text("ObstacleAvoider.ino", 170, sh - 15, 7, Label(new Color32(220, 224, 232, 255)));
            string[] files = { "ObstacleAvoider", "  ObstacleAvoider.ino", "  README.md", "Libraries", "  Servo", "Board: Arduino Uno" };
            for (int i = 0; i < files.Length; i++) code.Text(files[i], 12, sh - 50 - i * 18, 6.2f, Label(new Color32(170, 176, 188, 255)));
            (string text, Color32 colour)[] lines =
            {
                ("const int TRIG = 9, ECHO = 10;", new Color32(214, 160, 110, 255)),
                ("", default),
                ("long distanceCm() {", new Color32(120, 180, 250, 255)),
                ("  digitalWrite(TRIG, HIGH);", new Color32(220, 224, 232, 255)),
                ("  delayMicroseconds(10);", new Color32(220, 224, 232, 255)),
                ("  digitalWrite(TRIG, LOW);", new Color32(220, 224, 232, 255)),
                ("  return pulseIn(ECHO, HIGH) / 58;", new Color32(220, 224, 232, 255)),
                ("}", new Color32(120, 180, 250, 255)),
                ("", default),
                ("void loop() {", new Color32(120, 180, 250, 255)),
                ("  long cm = distanceCm();", new Color32(220, 224, 232, 255)),
                ("  if (cm < 20) turnRight();", new Color32(200, 140, 230, 255)),
                ("  else forward();", new Color32(200, 140, 230, 255)),
                ("}", new Color32(120, 180, 250, 255)),
            };
            for (int i = 0; i < lines.Length; i++)
            {
                code.Text((i + 1).ToString(), 176, sh - 48 - i * 19, 6.4f, Label(new Color32(90, 96, 108, 255)), Align.Right);
                if (lines[i].text.Length > 0) code.Text(lines[i].text, 190, sh - 48 - i * 19, 6.8f, Label(lines[i].colour));
            }
            code.Rect(150, 0, sw, 24, Ink.Paint(new Color32(0, 122, 204, 255), 0, 0.9f));
            code.Text("Done compiling  ·  Sketch uses 2954 bytes (9%)", 160, 8, 6, Label(new Color32(240, 244, 250, 255)));
            var screen = Painted("Monitor screen", code, 0, glow: 1.1f);

            var kit = new MeshKit(3);
            kit.Box(0, new Vector3(0, 6, 20), new Vector3(250, 12, 190), 30);                   // the foot
            kit.Box(1, new Vector3(0, 150, -10), new Vector3(70, 270, 22), 8);                  // the neck
            kit.Box(0, new Vector3(0, 300, 12), new Vector3(sw + 16, sh + 16, 24), 6);           // the body
            kit.Quad(2, new Vector3(sw / 2, 300 - sh / 2, 24.3f), new Vector3(-sw / 2, 300 - sh / 2, 24.3f), new Vector3(-sw / 2, 300 + sh / 2, 24.3f), new Vector3(sw / 2, 300 + sh / 2, 24.3f), Vector3.forward);
            Place("Monitor", kit, new[] { Plain("Monitor black", new Color(0.035f, 0.035f, 0.04f), 0.55f), Brushed, screen }, foot, Quaternion.Euler(0, 16, 0), 0.5f);

            // Keyboard and mouse in front of it.
            var desk = new MeshKit(2);
            desk.Box(0, new Vector3(0, 9, 0), new Vector3(440, 18, 132), 6);
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 15; col++)
                    desk.Box(1, new Vector3(-196 + col * 28, 19.5f, -48 + row * 24), new Vector3(24, 3, 20), 2);
            desk.Box(0, new Vector3(290, 16, 20), new Vector3(62, 32, 110), 28);
            Place("Keyboard", desk, new[] { Plain("Keyboard white", new Color(0.9f, 0.9f, 0.9f), 0.45f), Plain("Keycap", new Color(0.95f, 0.95f, 0.95f), 0.4f) },
                new Vector3(-590, 0, -150), Quaternion.Euler(0, 10, 0), 0.4f);
        }

        /// <summary>A white LED lamp clamped to the back panel: pole, two arms and a slim glowing head over the turntable.</summary>
        static void Lamp()
        {
            var clampAt = new Vector3(-150, 460, BenchBack + 8);
            var elbow = new Vector3(-140, 760, BenchBack - 10);
            var wrist = new Vector3(-100, 640, -330);
            var kit = new MeshKit(3);
            kit.Box(0, clampAt + new Vector3(0, -10, 0), new Vector3(46, 60, 40), 6);
            kit.Cylinder(0, clampAt, elbow, 9, 20, 2);
            kit.Cylinder(0, elbow, wrist, 8, 20, 2);
            kit.Cylinder(0, wrist, LampHead + new Vector3(0, 12, 0), 7, 20, 2);
            foreach (var joint in new[] { elbow, wrist }) kit.Sphere(1, joint, 13, 20);
            kit.Box(0, LampHead + new Vector3(0, 6, 0), new Vector3(320, 14, 52), 6);
            kit.Quad(2, LampHead + new Vector3(-150, -1.2f, -20), LampHead + new Vector3(150, -1.2f, -20), LampHead + new Vector3(150, -1.2f, 20), LampHead + new Vector3(-150, -1.2f, 20), Vector3.down);
            Place("Lamp", kit, new[] { Plain("Lamp white", new Color(0.94f, 0.94f, 0.93f), 0.5f), GreyPlastic, Glow("Lamp LED", new Color(1f, 0.97f, 0.92f), 2.0f) }, default, null, 0.3f);
        }

        // ------------------------------------------------------------------ on the bench

        static void DeskThings()
        {
            Mat();
            Breadboard(new Vector3(360, 3, 70), 6);
            Multimeter(new Vector3(610, 3, -80), -24);
            Calipers(new Vector3(-360, 3, 110), 28);
            Notebook(new Vector3(-700, 0, 40), -12);
            Mug(new Vector3(-820, 0, -260));
        }

        /// <summary>
        /// The measuring mat under the turntable: a grey-blue anti-static mat printed with a millimetre grid, rulers
        /// along two edges, a protractor in one corner and the ESD mark.
        /// </summary>
        static void Mat()
        {
            const float w = 620, d = 460;
            var art = new Raster(w, d, 2);
            art.Fill(Ink.Solid(new Color32(104, 124, 140, 255), 0, 0.22f, 0));
            art.Grain(0.03f, 0.05f, 1.5f, 13);
            var fine = Ink.Paint(new Color32(142, 160, 174, 255), 0, 0.25f);
            var bold = Ink.Paint(new Color32(200, 212, 222, 255), 0, 0.25f);
            for (float x = 30; x <= w - 20; x += 10) art.Line(x, 30, x, d - 20, (x - 30) % 50 == 0 ? 0.7f : 0.35f, (x - 30) % 50 == 0 ? bold : fine);
            for (float y = 30; y <= d - 20; y += 10) art.Line(30, y, w - 20, y, (y - 30) % 50 == 0 ? 0.7f : 0.35f, (y - 30) % 50 == 0 ? bold : fine);
            var white = Ink.Paint(new Color32(228, 234, 240, 255), 0, 0.3f);
            // Rulers: millimetre ticks along the front and left edges, a number every centimetre.
            for (int mm = 0; mm <= (int)(w - 50); mm++)
            {
                float x = 30 + mm, length = mm % 10 == 0 ? 9 : mm % 5 == 0 ? 6 : 3.5f;
                art.Line(x, 6, x, 6 + length, 0.4f, white);
                if (mm % 10 == 0 && mm > 0) art.Text((mm / 10).ToString(), x, 17, 4.2f, white, Align.Centre);
            }
            for (int mm = 0; mm <= (int)(d - 50); mm++)
            {
                float y = 30 + mm, length = mm % 10 == 0 ? 9 : mm % 5 == 0 ? 6 : 3.5f;
                art.Line(6, y, 6 + length, y, 0.4f, white);
                if (mm % 10 == 0 && mm > 0) art.Text((mm / 10).ToString(), 21, y - 1.6f, 3.6f, white, Align.Left);
            }
            art.Text("cm", w - 22, 17, 4, white, Align.Centre);
            // A protractor in the back right corner.
            var c = new Vector2(w - 20, d - 20);
            art.Ring(c.x, c.y, 120, 119, white);
            art.Ring(c.x, c.y, 80, 79.2f, white);
            for (int a = 0; a <= 90; a += 5)
            {
                float rad = (180 + a) * Mathf.Deg2Rad, inner = a % 15 == 0 ? 100 : 110;
                art.Line(c.x + Mathf.Cos(rad) * inner, c.y + Mathf.Sin(rad) * inner, c.x + Mathf.Cos(rad) * 120, c.y + Mathf.Sin(rad) * 120, 0.5f, white);
                if (a % 15 == 0) art.Text(a.ToString() + "°", c.x + Mathf.Cos(rad) * 90, c.y + Mathf.Sin(rad) * 90 - 2, 4.5f, white, Align.Centre);
            }
            art.Text("ESD SAFE WORK AREA", 40, d - 14, 7, white);
            art.Circle(w - 250, d - 11, 5.5f, Ink.Paint(new Color32(236, 190, 40, 255), 0, 0.3f));
            var material = Painted("Measuring mat", art, 0.5f);
            var kit = new MeshKit(1);
            kit.Box(0, Vector3.zero, new Vector3(w, 3, d), 1.5f, 0.001f, new Rect(0, 0, 1, 1), 0);
            Place("MeasuringMat", kit, new[] { material }, new Vector3(0, 1.5f, -40), Quaternion.Euler(0, 180, 0), 3f);
        }

        /// <summary>
        /// A full-size breadboard (the wiring board): power rails with their red and blue lines, 30 × 10 holes with
        /// numbers and letters, and on it a few jumpers, two LEDs and two resistors.
        /// </summary>
        static void Breadboard(Vector3 at, float degrees)
        {
            const float w = 165, d = 55, p = 2.54f;
            var art = new Raster(w, d, 8);
            art.Fill(Ink.Solid(new Color32(238, 238, 234, 255), 0, 0.38f, 0));
            var hole = Ink.Solid(new Color32(46, 46, 46, 255), 0, 0.1f, -1.2f);
            var print = Label(new Color32(70, 70, 70, 255));
            float x0 = w / 2 - 29 * p / 2;
            for (int col = 0; col < 30; col++)
            {
                float x = x0 + col * p;
                for (int row = 0; row < 10; row++)
                {
                    float y = row < 5 ? 10.6f + row * p : 10.6f + row * p + 3 * p - 2 * p;
                    art.RoundRect(x, y, 1.1f, 1.1f, 0.2f, hole);
                }
                if (col % 5 == 4 || col == 0) art.Text((col + 1).ToString(), x, 6.2f, 1.4f, print, Align.Centre);
            }
            string letters = "ABCDEFGHIJ";
            for (int row = 0; row < 10; row++)
            {
                float y = row < 5 ? 10.6f + row * p : 10.6f + row * p + p;
                art.Text(letters[row].ToString(), x0 - 3.5f, y - 0.7f, 1.4f, print, Align.Centre);
            }
            art.Rect(0, d / 2 - 1.3f, w, d / 2 + 1.3f, Ink.Solid(new Color32(222, 222, 218, 255), 0, 0.3f, -0.8f));
            foreach (float ry in new[] { 2.2f, d - 4.7f })
            {
                for (int k = 0; k < 25; k++)
                    foreach (int j in new[] { 0, 1 })
                        art.RoundRect(x0 + 2 + k * 6 * p / 2.4f * 1.0f, ry + j * p, 1.1f, 1.1f, 0.2f, hole);
                art.Line(4, ry - 1.6f, w - 4, ry - 1.6f, 0.45f, Ink.Paint(new Color32(210, 50, 50, 255), 0, 0.4f));
                art.Line(4, ry + p + 1.6f, w - 4, ry + p + 1.6f, 0.45f, Ink.Paint(new Color32(50, 80, 200, 255), 0, 0.4f));
            }
            var top = Painted("Breadboard", art, 0.6f);
            var kit = new MeshKit(7);
            // Jumpers: little arches between holes, in their colours.
            (float x, float z, float x2, float z2, int slot)[] jumpers =
            {
                (-40, -5, -40, -22, 2), (-20, 3, 10, 3, 3), (15, -3, 15, -22, 4), (30, 8, 30, 22, 5), (-55, 10, -30, 10, 3),
            };
            foreach (var (x, z, x2, z2, slot) in jumpers)
            {
                var path = new List<Vector3> { new(x, 8, z), new(x, 12, z), new((x + x2) / 2, 15, (z + z2) / 2), new(x2, 12, z2), new(x2, 8, z2) };
                kit.Sweep(slot, path, 0.55f, 6);
            }
            // Two LEDs with their legs in the board, and two resistors.
            foreach (var (x, slot) in new[] { (45f, 6), (55f, 2) })
            {
                var led = new List<Vector2> { new(0, 0), new(2.9f, 0), new(2.9f, 1), new(2.5f, 1), new(2.5f, 5.6f) };
                for (int i = 1; i <= 6; i++)
                {
                    float t = Mathf.PI / 2 * i / 6;
                    led.Add(new Vector2(Mathf.Cos(t) * 2.5f, 5.6f + Mathf.Sin(t) * 2.5f));
                }
                kit.Lathe(slot, new Vector3(x, 14, -4), Vector3.up, led, 20, 40);
                foreach (float s in new[] { -1.27f, 1.27f }) kit.Cylinder(1, new Vector3(x + s, 9, -4), new Vector3(x + s, 14, -4), 0.25f, 6);
            }
            foreach (float x in new[] { 45f, 55f })
            {
                kit.Cylinder(0, new Vector3(x - 4, 12, 8), new Vector3(x + 4, 12, 8), 1.2f, 12, 0.5f);
                kit.Sweep(1, new List<Vector3> { new(x - 6.35f, 9, 8), new(x - 6.35f, 12, 8), new(x - 4, 12, 8) }, 0.25f, 5);
                kit.Sweep(1, new List<Vector3> { new(x + 4, 12, 8), new(x + 6.35f, 12, 8), new(x + 6.35f, 9, 8) }, 0.25f, 5);
            }
            Place("Breadboard", kit, new[]
            {
                Plain("Resistor beige", new Color(0.8f, 0.7f, 0.52f), 0.45f), Steel, Plain("Jumper red", new Color(0.82f, 0.08f, 0.06f), 0.5f),
                Plain("Jumper yellow", new Color(0.95f, 0.78f, 0.1f), 0.5f), Plain("Jumper green", new Color(0.12f, 0.6f, 0.2f), 0.5f),
                Plain("Jumper blue", new Color(0.1f, 0.3f, 0.8f), 0.5f), Plain("LED green", new Color(0.2f, 0.8f, 0.3f), 0.9f),
            }, at, Quaternion.Euler(0, 180 + degrees, 0), 0.5f);
            // The board itself, its printed top read from the front.
            var body = new MeshKit(1);
            body.Box(0, new Vector3(0, 4.5f, 0), new Vector3(w, 9, d), 1, 0.001f, new Rect(0, 0, 1, 1), 0);
            Place("BreadboardBody", body, new[] { top }, at, Quaternion.Euler(0, 180 + degrees, 0), 0.8f);
        }

        /// <summary>A digital multimeter in its orange holster, its LCD reading 5.02 V, its probes and leads across the mat.</summary>
        static void Multimeter(Vector3 at, float degrees)
        {
            const float w = 88, d = 180;
            var face = new Raster(w, d, 5);
            face.Fill(Ink.Solid(new Color32(46, 48, 52, 255), 0, 0.4f, 0));
            face.RoundRect(w / 2, 150, 70, 32, 3, Ink.Solid(new Color32(150, 162, 150, 255), 0, 0.6f, -0.3f));
            SevenSegmentDark(face, "5.02", 16, 138, 22);
            face.Text("V DC", 70, 139, 4, Label(new Color32(30, 34, 30, 255)), Align.Centre);
            var labels = Label(LabelWhite);
            string[] modes = { "OFF", "V~", "V=", "Ω", "°C", "A", "mA", "µA" };
            for (int i = 0; i < modes.Length; i++)
            {
                float a = (140 - i * 40) * Mathf.Deg2Rad;
                face.Text(modes[i], w / 2 + Mathf.Cos(a) * 32, 82 + Mathf.Sin(a) * 32 - 2, 4, labels, Align.Centre);
            }
            face.Text("COM", 44, 12, 3.4f, labels, Align.Centre);
            face.Text("VΩ", 70, 12, 3.4f, Label(new Color32(230, 70, 60, 255)), Align.Centre);
            face.Text("10A", 18, 12, 3.4f, labels, Align.Centre);
            var faceMaterial = Painted("Multimeter face", face, 0.5f);
            var kit = new MeshKit(6);
            kit.Box(0, new Vector3(0, 20, 0), new Vector3(w + 8, 40, d + 8), 10);                                   // the holster
            kit.Box(1, new Vector3(0, 38, 0), new Vector3(w - 4, 6, d - 6), 4, 0.001f, new Rect(0, 0, 1, 1), 1);    // the face
            kit.Cylinder(2, new Vector3(0, 41, 82 - d / 2), new Vector3(0, 49, 82 - d / 2), 20, 32, 2);   // the dial
            kit.Box(3, new Vector3(0, 49.3f, 82 - d / 2 + 10), new Vector3(3, 0.6f, 16));
            foreach (var (x, slot) in new[] { (w / 2 - 44f, 2), (w / 2 - 70f, 4), (w / 2 - 18f, 2) })
                kit.Cylinder(slot, new Vector3(x, 41, 22 - d / 2), new Vector3(x, 44, 22 - d / 2), 4.5f, 16, 0.6f);
            var turn = Quaternion.Euler(0, 180 + degrees, 0); // its display away from the player, its jacks toward them
            Place("Multimeter", kit, new[] { Plain("Holster orange", new Color(0.95f, 0.46f, 0.08f), 0.35f), faceMaterial, DarkPlastic, Plain("Index white", Color.white, 0.4f),
                Plain("Jack red", new Color(0.75f, 0.08f, 0.06f), 0.5f), Steel }, at, turn, 0.6f);

            // The leads out of the jacks, looping over the mat to the probes.
            var leads = new MeshKit(4);
            foreach (var (x, slot, bend) in new[] { (w / 2 - 44f, 0, -1f), (w / 2 - 70f, 1, 1f) })
            {
                var start = at + turn * new Vector3(x, 44, 22 - d / 2);
                var path = new List<Vector3> { start, start + turn * new Vector3(0, 12, -24), at + turn * new Vector3(bend * 30, 8, -150), at + turn * new Vector3(bend * 70 + 40, 4, -215), at + turn * new Vector3(bend * 50 + 110, 5, -185) };
                leads.Sweep(slot, path, 1.7f, 8);
                var probeStart = path[path.Count - 1];
                var probeDir = (probeStart - path[path.Count - 2]).normalized;
                leads.Push(probeStart, Quaternion.FromToRotation(Vector3.up, probeDir));
                leads.Lathe(slot, Vector3.zero, Vector3.up, new List<Vector2> { new(0, 0), new(3, 0), new(5, 8), new(5, 70), new(6.5f, 74), new(3, 80), new(0, 80) }, 20, 40);
                leads.Pop();
                leads.Cylinder(2, probeStart + probeDir * 80, probeStart + probeDir * 100, 0.9f, 8);
            }
            Place("MultimeterLeads", leads, new[] { Plain("Lead black", new Color(0.04f, 0.04f, 0.045f), 0.5f), Plain("Lead red", new Color(0.78f, 0.07f, 0.06f), 0.5f), Steel, Steel }, default, null, 0.3f);
        }

        static void SevenSegmentDark(Raster r, string text, float x, float y, float height)
        {
            var ink = Ink.Paint(new Color32(24, 28, 24, 255), 0, 0.5f);
            float w = height * 0.55f, t = height * 0.12f;
            (float, float, float, float)[] segments = { (0, 2, 1, 2), (1, 2, 1, 1), (1, 1, 1, 0), (0, 0, 1, 0), (0, 0, 0, 1), (0, 1, 0, 2), (0, 1, 1, 1) };
            string[] lit = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
            foreach (char c in text)
            {
                if (c == '.')
                {
                    r.Circle(x - w * 0.18f, y + t * 0.6f, t * 0.6f, ink);
                    continue;
                }
                string segs = c >= '0' && c <= '9' ? lit[c - '0'] : "";
                for (int i = 0; i < 7; i++)
                {
                    if (segs.IndexOf((char)('a' + i)) < 0) continue;
                    var (ax, ay, bx, by) = segments[i];
                    r.Line(x + ax * w + ay * height * 0.03f, y + ay * height / 2, x + bx * w + by * height * 0.03f, y + by * height / 2, t, ink);
                }
                x += w + height * 0.3f;
            }
        }

        /// <summary>Digital calipers: the steel beam and jaws, the grey head with its LCD.</summary>
        static void Calipers(Vector3 at, float degrees)
        {
            var lcd = new Raster(36, 14, 12);
            lcd.Fill(Ink.Solid(new Color32(150, 160, 148, 255), 0, 0.6f, 0));
            SevenSegmentDark(lcd, "25.40", 3, 3, 8);
            lcd.Text("mm", 32, 3, 2.4f, Label(new Color32(30, 30, 30, 255)), Align.Centre);
            var screen = Painted("Calipers LCD", lcd, 0);
            var kit = new MeshKit(3);
            kit.Box(0, new Vector3(0, 1.5f, 0), new Vector3(230, 3, 16), 0.5f);
            kit.Box(0, new Vector3(-108, 1.5f, -24), new Vector3(8, 3, 48), 0.5f);
            kit.Box(1, new Vector3(-72, 7, 2), new Vector3(52, 14, 34), 3);
            kit.Box(0, new Vector3(-92, 1.5f, -22), new Vector3(8, 3, 44), 0.5f);
            kit.Box(2, new Vector3(-72, 14.05f, 4), new Vector3(36, 0.1f, 14), 0, 0.001f, new Rect(0, 0, 1, 1), 2);
            Place("Calipers", kit, new[] { Steel, Plain("Calipers grey", new Color(0.22f, 0.23f, 0.25f), 0.45f), screen }, at, Quaternion.Euler(0, 180 + degrees, 0), 0.4f);
        }

        static void Notebook(Vector3 at, float degrees)
        {
            var cover = new Raster(148, 210, 3);
            cover.Fill(Ink.Solid(new Color32(62, 70, 84, 255), 0, 0.3f, 0));
            cover.Grain(0.05f, 0.05f, 0.6f, 21);
            cover.Text("LAB NOTES", 20, 170, 9, Label(new Color32(220, 222, 226, 255)), Align.Left, 0, 0.16f);
            cover.Text("robot 03", 20, 150, 6, Label(new Color32(180, 186, 196, 255)));
            var coverMaterial = Painted("Notebook cover", cover, 0.3f);
            var kit = new MeshKit(4);
            kit.Box(0, new Vector3(0, 4, 0), new Vector3(148, 8, 210), 1.5f, 0.001f, new Rect(0, 0, 1, 1), 1);
            kit.Box(2, new Vector3(0, 4, 0), new Vector3(146, 6.2f, 208.5f), 0.5f);
            kit.Box(3, new Vector3(56, 8.2f, 0), new Vector3(5, 0.6f, 211), 0.3f);
            kit.Push(new Vector3(-10, 13, -30), Quaternion.Euler(0, -35, 90));
            kit.Lathe(3, new Vector3(0, -70, 0), Vector3.up, new List<Vector2> { new(0, 0), new(1.5f, 0), new(4.5f, 12), new(4.5f, 135), new(3.5f, 140), new(0, 141) }, 16, 40);
            kit.Pop();
            Place("Notebook", kit, new[] { Plain("Notebook back", new Color(0.24f, 0.27f, 0.32f), 0.3f), coverMaterial, Plain("Paper", new Color(0.94f, 0.93f, 0.9f), 0.2f), DarkPlastic },
                at, Quaternion.Euler(0, 180 + degrees, 0), 0.5f);
        }

        static void Mug(Vector3 at)
        {
            var kit = new MeshKit(2);
            var outer = new List<Vector2> { new(0, 0), new(36, 0), new(40, 4), new(41, 90), new(39, 95), new(37, 95), new(37, 12), new(0, 12) };
            kit.Lathe(0, Vector3.zero, Vector3.up, outer, 40, 40);
            var handle = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float a = -Mathf.PI / 2 + Mathf.PI * i / 16;
                handle.Add(new Vector3(40 + Mathf.Cos(a) * 20, 50 + Mathf.Sin(a) * 26, 0));
            }
            kit.Sweep(0, handle, 5, 10);
            kit.Cylinder(1, new Vector3(0, 70, 0), new Vector3(0, 71, 0), 36.5f, 32);
            Place("Mug", kit, new[] { Plain("Ceramic", new Color(0.94f, 0.94f, 0.93f), 0.8f), Plain("Coffee", new Color(0.12f, 0.07f, 0.04f), 0.9f) }, at, Quaternion.Euler(0, 200, 0), 0.4f);
        }
    }
}
