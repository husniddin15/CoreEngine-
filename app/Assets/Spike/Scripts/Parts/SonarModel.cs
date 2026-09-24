using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// The HC-SR04 (docs/09 §7): a blue 45 × 20 mm board standing up, its two aluminium transducers (T on the
    /// left, R on the right, seen from the front) with their woven fronts, the crystal between them, the four
    /// right-angle pins at the bottom pointing back, the three chips on its back, and the black stand that holds
    /// it by its bottom edge. Front art: mm from the lower left seen from the front (x = 22.5 - frame x).
    /// </summary>
    static class SonarModel
    {
        const float W = 45f, H = 20f, T = 0.8f; // half the board's thickness
        const float BackY = 22f, ArtHeight = 47f;
        static readonly float[] PinX = { 3.81f, 1.27f, -1.27f, -3.81f };
        static readonly string[] PinNames = { "VCC", "TRIG", "ECHO", "GND" };

        static readonly Rect McuMark = Rect.MinMaxRect(0.5f, 42.8f, 5.4f, 46.7f);
        static readonly Rect OpampMark = Rect.MinMaxRect(6.0f, 42.8f, 14.7f, 46.7f);
        static readonly Rect DriverMark = Rect.MinMaxRect(15.3f, 42.8f, 20.2f, 46.7f);
        static readonly Rect XtalMark = Rect.MinMaxRect(21.0f, 42.8f, 32.4f, 46.7f);

        static Raster? art;

        static Material BoardMaterial => PartLooks.Textured("hc-sr04 board", Paint, 1.1f);

        static Rect Uv(Rect r) => art!.UvRect(r.xMin, r.yMin, r.xMax, r.yMax);

        /// <summary>The woven front of a transducer: dark threads over darkness, repeating every millimetre.</summary>
        static Material MeshFront => PartLooks.Textured("sonar mesh", () =>
        {
            var r = new Raster(1f, 1f, 96);
            r.Fill(Ink.Solid(new Color32(6, 6, 7, 255), 0, 0.1f, -0.15f));
            var thread = Ink.Solid(new Color32(46, 46, 48, 255), 0.2f, 0.35f, 0.1f);
            r.Line(0, 0.25f, 1, 0.25f, 0.28f, thread);
            r.Line(0, 0.75f, 1, 0.75f, 0.28f, thread);
            r.Line(0.25f, 0, 0.25f, 1, 0.28f, thread);
            r.Line(0.75f, 0, 0.75f, 1, 0.28f, thread);
            return r;
        }, 1.4f, repeat: true);

        public static PartModel Make()
        {
            var board = BoardMaterial;
            var b = new Bench();
            var k = b.Kit;

            // The board, extruded along y and stood up so its top faces the front (+z).
            k.Push(Vector3.zero, Quaternion.Euler(90, 0, 0));
            var outline = new List<Vector2> { new(-W / 2, -H / 2), new(W / 2, -H / 2), new(W / 2, H / 2), new(-W / 2, H / 2) };
            // Extrusion (x, z) lands on (x, -z) once stood up: the front art's (22.5 - x, y + 10) is (22.5 - px, 10 - pz).
            k.Extrude(outline, -T, T, b[board], b[PartLooks.Fr4Edge], b[board],
                p => art!.Uv(W / 2 - p.x, H / 2 - p.y), 20, p => art!.Uv(W / 2 + p.x, BackY + H / 2 - p.y));
            k.Pop();

            foreach (float x in new[] { 13f, -13f }) Transducer(b, new Vector3(x, 0, T));

            // The crystal lies on the front between the transducers, near the top.
            // Stood on the front and turned so its print reads upright from the front.
            k.Push(new Vector3(0, 6.3f, T), Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, 180, 0));
            Pieces.Crystal(b, Vector3.zero, board, Uv(XtalMark));
            k.Pop();

            // Right-angle pins: soldered on the front, through the board, bent back through a black spacer.
            int gold = b[PartLooks.Gold];
            k.Box(b[PartLooks.BlackPlastic], new Vector3(0, -9, -T - 1.25f), new Vector3(10.2f, 2.5f, 2.5f), 0.15f);
            foreach (float x in PinX)
            {
                k.Box(gold, new Vector3(x, -9, -T - 4.1f), new Vector3(0.64f, 0.64f, 8.2f));
                k.Box(gold, new Vector3(x, -9, T + 0.3f), new Vector3(0.64f, 0.64f, 0.6f));
                k.Cylinder(b[PartLooks.Tin], new Vector3(x, -9, T), new Vector3(x, -9, T + 0.35f), 0.75f, 12, 0.2f);
            }

            // The chips on the back.
            k.Push(new Vector3(0, 0, -T), Quaternion.Euler(-90, 0, 0));
            // In this frame y points back and z up the board: a chip at (x, 0, z) sits at (x, z) on the back.
            Pieces.GullWing(b, new Vector3(-12.5f, 0, -3.0f), new Vector3(4.9f, 1.45f, 3.9f), 4, 1.27f, board, Uv(McuMark));
            Pieces.GullWing(b, new Vector3(0, 0, -3.5f), new Vector3(8.7f, 1.45f, 3.9f), 7, 1.27f, board, Uv(OpampMark));
            Pieces.GullWing(b, new Vector3(12.5f, 0, -3.0f), new Vector3(4.9f, 1.45f, 3.9f), 4, 1.27f, board, Uv(DriverMark));
            foreach (var (x, z) in new[] { (-6.5f, -6.2f), (6.5f, -6.2f), (-18.0f, 1.5f), (18.0f, 1.5f), (-6.8f, 2.8f), (6.8f, 2.8f), (0f, 3.8f) })
                Pieces.Smd(b, new Vector3(x, 0, z), 1.6f, 0.8f, 0.5f, x > 0 ? Pieces.ResistorBody : Pieces.CapacitorBody);
            k.Pop();

            // The stand: a base on the deck and two clips holding the board's bottom edge.
            int stand = b[PartLooks.Solid("stand plastic", new Color(0.05f, 0.05f, 0.055f), 0.35f)];
            k.Box(stand, new Vector3(0, -14.75f, -4f), new Vector3(30f, 2.5f, 11f), 0.6f);
            foreach (float x in new[] { -12f, 12f })
            {
                k.Box(stand, new Vector3(x, -11.25f, -1.7f), new Vector3(5f, 4.5f, 1.8f), 0.3f);
                k.Box(stand, new Vector3(x, -11.25f, 1.7f), new Vector3(5f, 4.5f, 1.8f), 0.3f);
                k.Box(stand, new Vector3(x, -12.9f, 0), new Vector3(5f, 1.2f, 5.2f), 0.2f);
            }
            return b.Finish("HC-SR04");
        }

        /// <summary>A transducer: the aluminium can from the board forward 12 mm, its rolled lip and the woven front.</summary>
        static void Transducer(Bench b, Vector3 foot)
        {
            var profile = new List<Vector2>
            {
                new(7.5f, 0), new(7.9f, 0.4f), new(7.9f, 11.2f), new(8.0f, 11.6f), new(7.8f, 12.0f), new(7.1f, 12.0f), new(6.9f, 11.7f),
            };
            b.Kit.Lathe(b[PartLooks.Aluminium], foot, Vector3.forward, profile, 48, 40);
            Pieces.Disc(b, b[MeshFront], foot + Vector3.forward * 11.7f, Vector3.forward, 6.95f, 1f, 48);
        }

        // ------------------------------------------------------------------ the painted board

        static readonly Color32 Mask = new Color32(12, 70, 160, 255);
        static readonly Color32 CopperMask = new Color32(34, 98, 190, 255);

        static Ink MaskInk => Ink.Solid(Mask, 0, 0.72f, 0);
        static Ink CopperInk => Ink.Solid(CopperMask, 0, 0.74f, 0.035f);
        static Ink SilkInk => Ink.Paint(new Color32(236, 238, 240, 255), 0, 0.32f);

        static Raster Paint()
        {
            var r = new Raster(W, ArtHeight, 20);
            art = r;
            r.Fill(MaskInk);
            var silk = SilkInk;
            // Front: pour, traces to the transducers and the header, "T" and "R", the name and the pin names.
            r.RoundRect(W / 2, H / 2, W - 2.4f, H - 2.4f, 1.5f, CopperInk);
            foreach (var route in new[]
            {
                new List<Vector2> { new(17.5f, 9.0f), new(19.5f, 7.0f), new(19.5f, 3.2f) },
                new List<Vector2> { new(27.5f, 9.0f), new(25.5f, 7.0f), new(25.5f, 3.2f) },
                new List<Vector2> { new(16.0f, 15.5f), new(18.5f, 15.5f) },
                new List<Vector2> { new(29.0f, 15.5f), new(26.5f, 15.5f) },
            })
            {
                r.Polyline(route, 0.9f, MaskInk);
                r.Polyline(route, 0.35f, CopperInk);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = W / 2 - PinX[i];
                r.Circle(x, 1.0f, 1.0f, Ink.Solid(new Color32(206, 206, 201, 255), 1, 0.55f, 0.06f));
                r.Text(PinNames[i], x, 2.6f, 0.75f, silk, Align.Centre);
            }
            r.Text("HC-SR04", W / 2, 10.2f, 1.45f, silk, Align.Centre, 0, 0.16f);
            r.Text("T", 1.6f, 16.8f, 1.6f, silk, Align.Centre);
            r.Text("R", W - 1.6f, 16.8f, 1.6f, silk, Align.Centre);
            foreach (var (x, y) in new[] { (22.5f, 4.5f), (13.0f, 1.8f), (32.0f, 1.8f), (22.5f, 18.6f) })
            {
                r.Circle(x, y, 0.7f, MaskInk);
                r.Circle(x, y, 0.45f, Ink.Solid(new Color32(46, 112, 204, 255), 0, 0.72f, 0.05f));
            }

            // Back: its own pour and traces, and the pads of the chips.
            r.RoundRect(W / 2, BackY + H / 2, W - 2.4f, H - 2.4f, 1.5f, CopperInk);
            for (int i = 0; i < 9; i++)
            {
                float x = 8 + i * 3.6f;
                var route = new List<Vector2> { new(x, BackY + 3.5f), new(x, BackY + 6.5f + (i % 3)), new(x + 1.5f, BackY + 8 + (i % 3)) };
                r.Polyline(route, 0.8f, MaskInk);
                r.Polyline(route, 0.3f, CopperInk);
            }
            r.Text("HC-SR04", W / 2, BackY + 16.4f, 1.1f, silk, Align.Centre);
            r.Grain(0.025f, 0.05f, 0.8f, 31);

            // The chips' and the crystal's markings.
            var epoxy = Ink.Solid(new Color32(22, 22, 24, 255), 0, 0.3f, 0);
            var laser = Ink.Paint(new Color32(88, 88, 92, 255), 0, 0.18f);
            foreach (var (mark, text) in new[] { (McuMark, "EM78P153"), (OpampMark, "LM324"), (DriverMark, "MAX232") })
            {
                r.Rect(mark.xMin, mark.yMin, mark.xMax, mark.yMax, epoxy);
                r.Text(text, mark.center.x, mark.center.y - 0.3f, 0.65f, laser, Align.Centre);
            }
            r.Rect(XtalMark.xMin, XtalMark.yMin, XtalMark.xMax, XtalMark.yMax, Ink.Solid(new Color32(214, 214, 210, 255), 1, 0.84f, 0));
            r.Text("12.000", XtalMark.center.x, XtalMark.center.y - 0.5f, 1.2f, Ink.Solid(new Color32(150, 150, 146, 255), 1, 0.45f, -0.03f), Align.Centre);
            return r;
        }
    }
}
