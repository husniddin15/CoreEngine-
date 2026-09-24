using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// The Arduino Uno R3 (docs/09 §2.1): its 68.6 × 53.3 mm teal board painted with copper under the mask,
    /// pads, vias and white silkscreen (pin names, "DIGITAL (PWM~)", "POWER", "ANALOG IN", "UNO"), and on it
    /// what a real R3 carries where it carries it: the USB-B and barrel jacks, the ATmega328P in its socket, the
    /// ATmega16U2, the 16 MHz crystal, the regulator, two electrolytics, the reset button, both ICSP headers, the
    /// four LEDs (lights: ON, L, TX, RX) and the small SMD parts. The female headers are where the catalogue has
    /// its pins. Board coordinates: mm from the corner by the power header, x toward the right, y toward the
    /// digital header; the part's frame is the middle of the board, 0.9 mm under it (the pins' ends).
    /// </summary>
    static class UnoModel
    {
        const float W = 68.58f, H = 53.34f, ArtHeight = 65.4f;
        const float Bottom = 0.9f, Top = 2.5f;
        const float Ox = 34.3f, Oz = 26.7f; // board corner to the part's frame

        static readonly Vector2 Chip = new Vector2(47.2f, 17.6f);
        static readonly Vector2 Usb = new Vector2(1.8f, 38.0f);
        static readonly Vector2 U2 = new Vector2(20.3f, 36.0f);
        static readonly Vector2 Soic = new Vector2(47.0f, 35.5f);
        static readonly Vector2 Regulator = new Vector2(17.5f, 8.2f);
        static readonly Vector2 Xtal = new Vector2(19.8f, 28.5f);
        static readonly Vector2 Reset = new Vector2(6.8f, 49.0f);
        static readonly Vector2 Icsp = new Vector2(63.6f, 27.0f);
        static readonly Vector2 Icsp1 = new Vector2(16.0f, 46.6f);
        static readonly Vector2[] Caps = { new Vector2(13.5f, 17.5f), new Vector2(20.5f, 17.5f) };
        static readonly Vector2[] Holes = { new Vector2(13.97f, 2.54f), new Vector2(15.24f, 50.8f), new Vector2(66.04f, 7.62f), new Vector2(66.04f, 35.56f) };

        // Where the markings are in the texture, above the board's own area.
        static readonly Rect ChipMark = Rect.MinMaxRect(0.5f, 54.6f, 36.02f, 61.22f);
        static readonly Rect U2Mark = Rect.MinMaxRect(37.0f, 54.6f, 42.0f, 59.6f);
        static readonly Rect SoicMark = Rect.MinMaxRect(43.0f, 54.6f, 47.9f, 58.5f);
        static readonly Rect RegulatorMark = Rect.MinMaxRect(49.0f, 54.6f, 55.5f, 58.1f);
        static readonly Rect XtalMark = Rect.MinMaxRect(56.5f, 54.6f, 67.9f, 59.25f);
        static readonly Rect CapMark = Rect.MinMaxRect(43.0f, 58.9f, 49.3f, 65.2f);

        /// <summary>Small SMD parts: place, length, width, height, along the board's y, and kind (R resistor, C capacitor, F fuse, N network).</summary>
        static readonly (float x, float y, float l, float w, float h, bool alongY, char kind)[] Smds =
        {
            (16.4f, 35.0f, 1.6f, 0.8f, 0.8f, true, 'C'), (20.3f, 32.3f, 1.6f, 0.8f, 0.8f, false, 'C'), (24.3f, 32.6f, 1.6f, 0.8f, 0.8f, true, 'C'),
            (12.6f, 35.6f, 1.6f, 0.8f, 0.45f, false, 'R'), (12.6f, 40.2f, 1.6f, 0.8f, 0.45f, false, 'R'), (21.0f, 42.6f, 1.6f, 0.8f, 0.45f, false, 'R'),
            (24.2f, 42.3f, 3.2f, 1.6f, 0.5f, false, 'N'), (8.8f, 28.0f, 3.2f, 1.6f, 1.1f, false, 'F'),
            (26.8f, 17.6f, 1.6f, 0.8f, 0.8f, true, 'C'), (66.7f, 17.6f, 1.6f, 0.8f, 0.8f, true, 'C'),
            (29.6f, 25.2f, 1.6f, 0.8f, 0.45f, false, 'R'), (32.4f, 25.2f, 1.6f, 0.8f, 0.8f, false, 'C'),
            (23.6f, 10.8f, 1.6f, 0.8f, 0.8f, true, 'C'), (25.8f, 10.8f, 1.6f, 0.8f, 0.8f, true, 'C'), (21.7f, 5.3f, 2.0f, 1.25f, 0.6f, false, 'R'),
            (43.2f, 38.6f, 1.6f, 0.8f, 0.45f, false, 'R'), (51.0f, 38.6f, 1.6f, 0.8f, 0.45f, false, 'R'), (51.0f, 32.6f, 1.6f, 0.8f, 0.8f, false, 'C'),
            (56.2f, 38.5f, 1.6f, 0.8f, 0.45f, false, 'R'), (59.6f, 32.0f, 1.6f, 0.8f, 0.8f, true, 'C'), (9.0f, 24.2f, 2.0f, 1.25f, 0.6f, false, 'C'),
        };

        /// <summary>The four LEDs: name, place, and the colour they light.</summary>
        static readonly (string name, Vector2 at, Color glow)[] Leds =
        {
            ("L", new Vector2(25.8f, 39.0f), new Color(1f, 0.55f, 0.1f)),
            ("TX", new Vector2(25.8f, 36.6f), new Color(1f, 0.8f, 0.15f)),
            ("RX", new Vector2(25.8f, 34.2f), new Color(1f, 0.8f, 0.15f)),
            ("ON", new Vector2(58.5f, 44.4f), new Color(0.2f, 1f, 0.3f)),
        };

        static Raster? art;

        static Material TopMaterial => PartLooks.Textured("uno top", Paint, 1.1f);

        static Vector2 Uv(Vector2 p) => art!.Uv(p.x, p.y);

        static Rect Uv(Rect r) => art!.UvRect(r.xMin, r.yMin, r.xMax, r.yMax);

        public static PartModel Make()
        {
            var top = TopMaterial; // paints the art first: the marks below need its size
            var b = new Bench();
            var k = b.Kit;
            k.Push(new Vector3(-Ox, 0, -Oz));
            var outline = new List<Vector2> { new(0, 0), new(66.04f, 0), new(68.58f, 2.54f), new(68.58f, 50.8f), new(66.04f, 53.34f), new(0, 53.34f) };
            Pieces.Board(b, outline, Bottom, Top, top, Uv, PartLooks.Solid("uno bottom", new Color(0.0f, 0.33f, 0.36f), 0.6f));
            k.Push(new Vector3(0, Top, 0));

            // Headers, where the catalogue's pins are (the power header has its NC and IOREF places too).
            Pieces.FemaleHeader(b, 23.0f, 50.8f, 10);
            Pieces.FemaleHeader(b, 44.9f, 50.8f, 8);
            Pieces.FemaleHeader(b, 25.42f, 2.54f, 8);
            Pieces.FemaleHeader(b, 50.8f, 2.54f, 6);

            UsbJack(b);
            BarrelJack(b);
            Pieces.Dip(b, new Vector3(Chip.x, 0, Chip.y), 14, 7.62f, top, Uv(ChipMark), socket: true);
            Qfn(b, new Vector3(U2.x, 0, U2.y), 5f, top, Uv(U2Mark));
            Pieces.GullWing(b, new Vector3(Soic.x, 0, Soic.y), new Vector3(4.9f, 1.45f, 3.9f), 4, 1.27f, top, Uv(SoicMark));
            Sot223(b, new Vector3(Regulator.x, 0, Regulator.y), top, Uv(RegulatorMark));
            Pieces.Crystal(b, new Vector3(Xtal.x, 0, Xtal.y), top, Uv(XtalMark));
            foreach (var c in Caps) SmdElectrolytic(b, new Vector3(c.x, 0, c.y), top, Uv(CapMark));
            Pieces.TactileSwitch(b, new Vector3(Reset.x, 0, Reset.y));
            Pieces.MaleHeader(b, new Vector3(Icsp.x - 1.27f, 0, Icsp.y - Pieces.Pitch), Vector3.forward, 3, 2, new Vector3(Pieces.Pitch, 0, 0));
            Pieces.MaleHeader(b, new Vector3(Icsp1.x - Pieces.Pitch, 0, Icsp1.y - 1.27f), Vector3.right, 3, 2, new Vector3(0, 0, Pieces.Pitch));
            // The ceramic resonator of the 328P.
            b.Kit.Box(b[PartLooks.Solid("resonator", new Color(0.62f, 0.58f, 0.46f), 0.35f)], new Vector3(38.5f, 0.45f, 24.6f), new Vector3(3.2f, 0.9f, 1.3f), 0.1f);
            foreach (var s in Smds)
            {
                var body = s.kind switch
                {
                    'R' => Pieces.ResistorBody,
                    'N' => Pieces.ResistorBody,
                    'F' => PartLooks.Solid("polyfuse", new Color(0.78f, 0.62f, 0.12f), 0.4f),
                    _ => Pieces.CapacitorBody,
                };
                Pieces.Smd(b, new Vector3(s.x, 0, s.y), s.l, s.w, s.h, body, s.alongY);
            }
            foreach (var led in Leds)
            {
                // A white 0805 body with the clear lens on top, which lights.
                b.Kit.Box(b[PartLooks.WhitePlastic], new Vector3(led.at.x, 0.2f, led.at.y), new Vector3(2.0f, 0.4f, 1.25f), 0.05f);
                foreach (int side in new[] { -1, 1 })
                    b.Kit.Box(b[PartLooks.Tin], new Vector3(led.at.x + side * 0.85f, 0.25f, led.at.y), new Vector3(0.35f, 0.5f, 1.28f), 0.05f);
                b.Light(led.name, new Vector3(led.at.x - Ox, Top + 0.62f, led.at.y - Oz), new Vector3(1.3f, 0.45f, 1.15f),
                    new Color(0.93f, 0.9f, 0.78f), led.glow);
            }
            k.Pop();
            k.Pop();
            return b.Finish("Arduino Uno R3");
        }

        // ------------------------------------------------------------------ the jacks and chips

        /// <summary>The USB-B receptacle: a nickel shell overhanging the board's edge, its square mouth toward -x with the tongue inside.</summary>
        static void UsbJack(Bench b)
        {
            const float length = 16.3f, width = 12.0f, height = 10.9f, overhang = 6.3f;
            float x0 = -overhang, cx = x0 + length / 2;
            int shell = b[PartLooks.Nickel];
            b.Kit.Box(shell, new Vector3(cx, height / 2, Usb.y), new Vector3(length, height, width), 0.45f);
            // The mouth: the dark opening with its two chamfered upper corners, and the white tongue in it.
            b.Kit.Box(b[PartLooks.Hole], new Vector3(x0 - 0.02f, height / 2 - 0.3f, Usb.y), new Vector3(0.1f, 7.8f, 8.5f));
            b.Kit.Box(b[PartLooks.WhitePlastic], new Vector3(x0 + 1.6f, height / 2 - 0.3f, Usb.y), new Vector3(3.0f, 2.6f, 5.6f), 0.2f);
            foreach (int side in new[] { -1, 1 })
            {
                // Seams and the bent tabs that hold the shell to the board.
                b.Kit.Box(shell, new Vector3(cx + side * 4.5f, 0.6f, Usb.y + side * (width / 2 + 0.25f)), new Vector3(2.4f, 1.2f, 0.5f));
                b.Kit.Box(b[PartLooks.Tin], new Vector3(cx + side * 4.5f, 0.15f, Usb.y + side * (width / 2 + 1.0f)), new Vector3(3.2f, 0.3f, 1.4f), 0.1f);
            }
            b.Kit.Box(b[PartLooks.Hole], new Vector3(cx + 2.0f, height + 0.005f, Usb.y), new Vector3(9.0f, 0.02f, 0.25f));
        }

        /// <summary>The 2.1 mm barrel jack: black body, round mouth toward -x with its centre pin.</summary>
        static void BarrelJack(Bench b)
        {
            const float length = 14.2f, width = 9.0f, height = 11.0f;
            float x0 = -1.8f, cx = x0 + length / 2, y = 7.8f;
            int plastic = b[PartLooks.BlackPlastic];
            b.Kit.Box(plastic, new Vector3(cx + 1.2f, height / 2, y), new Vector3(length - 2.4f, height, width), 0.4f);
            b.Kit.Box(plastic, new Vector3(x0 + 1.2f, 4.5f, y), new Vector3(2.4f, 9.0f, width), 0.4f);
            b.Kit.Cylinder(plastic, new Vector3(x0 + 0.2f, 6.3f, y), new Vector3(x0 + 2.5f, 6.3f, y), 4.2f, 32, 0.5f);
            b.Kit.Cylinder(b[PartLooks.Hole], new Vector3(x0 + 0.15f, 6.3f, y), new Vector3(x0 + 0.16f, 6.3f, y), 3.1f, 32, 0, false);
            b.Kit.Cylinder(b[PartLooks.Hole], new Vector3(x0 + 0.14f, 6.3f, y), new Vector3(x0 + 0.15f, 6.3f, y), 3.1f, 32);
            b.Kit.Cylinder(b[PartLooks.Tin], new Vector3(x0 + 0.3f, 6.3f, y), new Vector3(x0 + 4.0f, 6.3f, y), 1.0f, 16, 0.2f);
        }

        static void Qfn(Bench b, Vector3 centre, float side, Material mark, Rect markUv)
        {
            b.Kit.Box(b[PartLooks.MatteBlack], centre + Vector3.up * 0.45f, new Vector3(side, 0.9f, side), 0.08f, topUv: markUv, topSlot: b[mark]);
            for (int i = 0; i < 8; i++)
            {
                float t = -side / 2 + 0.5f + i * (side - 1f) / 7;
                foreach (int s in new[] { -1, 1 })
                {
                    b.Kit.Box(b[PartLooks.Tin], centre + new Vector3(t, 0.12f, s * (side / 2 + 0.05f)), new Vector3(0.28f, 0.24f, 0.12f));
                    b.Kit.Box(b[PartLooks.Tin], centre + new Vector3(s * (side / 2 + 0.05f), 0.12f, t), new Vector3(0.12f, 0.24f, 0.28f));
                }
            }
        }

        /// <summary>The NCP1117 regulator in SOT-223: three legs toward -y, the wide tab toward +y.</summary>
        static void Sot223(Bench b, Vector3 centre, Material mark, Rect markUv)
        {
            b.Kit.Box(b[PartLooks.MatteBlack], centre + new Vector3(0, 0.95f, 0), new Vector3(6.5f, 1.6f, 3.5f), 0.12f, topUv: markUv, topSlot: b[mark]);
            int tin = b[PartLooks.Tin];
            b.Kit.Box(tin, centre + new Vector3(0, 0.95f, 2.05f), new Vector3(3.0f, 0.3f, 0.6f));
            b.Kit.Box(tin, centre + new Vector3(0, 0.55f, 2.45f), new Vector3(3.0f, 0.9f, 0.3f));
            b.Kit.Box(tin, centre + new Vector3(0, 0.12f, 3.0f), new Vector3(3.0f, 0.25f, 1.3f));
            for (int i = -1; i <= 1; i++)
            {
                b.Kit.Box(tin, centre + new Vector3(i * 2.3f, 0.95f, -2.0f), new Vector3(0.7f, 0.3f, 0.5f));
                b.Kit.Box(tin, centre + new Vector3(i * 2.3f, 0.55f, -2.35f), new Vector3(0.7f, 0.9f, 0.3f));
                b.Kit.Box(tin, centre + new Vector3(i * 2.3f, 0.12f, -2.85f), new Vector3(0.7f, 0.25f, 1.1f));
            }
        }

        /// <summary>A 6.3 mm SMD electrolytic: black square base with cut corners, the aluminium can, its printed top.</summary>
        static void SmdElectrolytic(Bench b, Vector3 foot, Material mark, Rect markUv)
        {
            b.Kit.Box(b[PartLooks.BlackPlastic], foot + Vector3.up * 0.55f, new Vector3(6.6f, 1.1f, 6.6f), 0.6f);
            b.Kit.Cylinder(b[PartLooks.Aluminium], foot + Vector3.up * 1.0f, foot + Vector3.up * 5.2f, 3.15f, 40, 0.35f, caps: false);
            // The top: a flat disc mapped to its print in the texture.
            b.Kit.Push(foot + Vector3.up * 5.4f);
            int slot = b[mark];
            int centre = b.Kit.Vertex(Vector3.zero, Vector3.up, new Vector2(markUv.center.x, markUv.center.y));
            int rim = b.Kit.VertexCount;
            for (int i = 0; i <= 40; i++)
            {
                float a = Mathf.PI * 2 * i / 40;
                float x = Mathf.Cos(a), z = Mathf.Sin(a);
                b.Kit.Vertex(new Vector3(x * 2.95f, 0, z * 2.95f), Vector3.up,
                    new Vector2(markUv.center.x + x * markUv.width / 2 * 0.94f, markUv.center.y + z * markUv.height / 2 * 0.94f));
            }
            for (int i = 0; i < 40; i++) b.Kit.Triangle(slot, centre, rim + i, rim + i + 1);
            b.Kit.Pop();
            b.Kit.Lathe(b[PartLooks.Aluminium], foot + Vector3.up * 5.2f, Vector3.up,
                new List<Vector2> { new Vector2(3.15f, 0), new Vector2(3.1f, 0.12f), new Vector2(2.95f, 0.2f) }, 40, 60);
        }

        // ------------------------------------------------------------------ the painted board

        static readonly Color32 Mask = new Color32(0, 104, 114, 255);
        static readonly Color32 CopperMask = new Color32(14, 134, 142, 255);
        static readonly Color32 Silk = new Color32(236, 240, 238, 255);
        static readonly Color32 TinPad = new Color32(206, 206, 201, 255);
        static readonly Color32 BareBoard = new Color32(176, 168, 124, 255);

        static Ink MaskInk => Ink.Solid(Mask, 0, 0.72f, 0);
        static Ink CopperInk => Ink.Solid(CopperMask, 0, 0.74f, 0.035f);
        static Ink PadInk => Ink.Solid(TinPad, 1, 0.55f, 0.07f);
        static Ink SilkInk => Ink.Paint(Silk, 0, 0.32f);

        static Raster Paint()
        {
            var r = new Raster(W, ArtHeight, 17);
            art = r;
            r.Fill(MaskInk);

            // Ground pour under the mask over most of the board, with the gaps round everything it passes.
            r.RoundRect(W / 2 + 1.5f, H / 2, W - 7, H - 3.5f, 2.5f, CopperInk);
            r.RoundRect(6, 38, 13, 15, 1, MaskInk); // no copper under the USB jack's shell

            foreach (var route in Routes()) Route(r, route.points, route.width);
            foreach (var via in Vias) Via(r, via);

            // Pads of the small parts and chips: tin shows at their ends.
            foreach (var s in Smds)
            {
                float along = s.l / 2 + 0.25f, pad = s.l * 0.32f;
                foreach (int side in new[] { -1, 1 })
                {
                    var c = s.alongY ? new Vector2(s.x, s.y + side * (along - pad / 2)) : new Vector2(s.x + side * (along - pad / 2), s.y);
                    Pad(r, c, s.alongY ? new Vector2(s.w + 0.3f, pad) : new Vector2(pad, s.w + 0.3f));
                }
            }
            foreach (var led in Leds)
                foreach (int side in new[] { -1, 1 }) Pad(r, led.at + new Vector2(side * 1.0f, 0), new Vector2(0.9f, 1.5f));
            for (int i = 0; i < 4; i++)
                foreach (int side in new[] { -1, 1 }) Pad(r, new Vector2(Soic.x - 1.905f + i * 1.27f, Soic.y + side * 3.1f), new Vector2(0.6f, 1.6f));
            for (int i = -1; i <= 1; i++) Pad(r, Regulator + new Vector2(i * 2.3f, -3.0f), new Vector2(1.0f, 1.6f));
            Pad(r, Regulator + new Vector2(0, 3.1f), new Vector2(3.4f, 2.0f));
            foreach (var c in Caps)
                foreach (int side in new[] { -1, 1 }) Pad(r, c + new Vector2(side * 3.1f, 0), new Vector2(2.4f, 1.6f));
            foreach (int side in new[] { -1, 1 }) Pad(r, Xtal + new Vector2(side * 4.9f, 0), new Vector2(1.6f, 1.6f));
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 }) Pad(r, Reset + new Vector2(sx * 3.9f, sy * 2.25f), new Vector2(1.6f, 1.2f));

            // Mounting holes: bare board round a plain hole.
            foreach (var h in Holes)
            {
                r.Circle(h.x, h.y, 2.75f, Ink.Solid(BareBoard, 0, 0.35f, 0.0f));
                r.Circle(h.x, h.y, 1.6f, Ink.Solid(new Color32(14, 14, 14, 255), 0, 0.05f, -1.2f));
            }

            Silkscreen(r);
            r.Grain(0.022f, 0.05f, 0.9f, 11);
            Marks(r);
            return r;
        }

        static void Pad(Raster r, Vector2 centre, Vector2 size)
        {
            r.RoundRect(centre.x, centre.y, size.x + 0.5f, size.y + 0.5f, 0.3f, MaskInk);
            r.RoundRect(centre.x, centre.y, size.x, size.y, 0.12f, PadInk);
        }

        /// <summary>A trace through the pour: the gap either side of it first, then the copper.</summary>
        static void Route(Raster r, IList<Vector2> points, float width)
        {
            r.Polyline(points, width + 0.55f, MaskInk);
            r.Polyline(points, width, CopperInk);
        }

        static void Via(Raster r, Vector2 at)
        {
            r.Circle(at.x, at.y, 0.72f, MaskInk);
            r.Circle(at.x, at.y, 0.45f, Ink.Solid(new Color32(24, 150, 156, 255), 0, 0.72f, 0.05f));
            r.Circle(at.x, at.y, 0.17f, Ink.Solid(new Color32(0, 60, 66, 255), 0, 0.5f, -0.2f));
        }

        static readonly List<Vector2> Vias = new List<Vector2>();

        /// <summary>
        /// Traces laid as a board designer would: from the 328P's legs up to the digital header and down to the
        /// analog and power headers with 45° bends, the USB pair to the 16U2, serial lines between the two chips,
        /// and wide supply traces from the jack and the regulator.
        /// </summary>
        static IEnumerable<(List<Vector2> points, float width)> Routes()
        {
            Vias.Clear();
            float rowTop = Chip.y + 3.81f, rowBottom = Chip.y - 3.81f;
            float firstLeg = Chip.x - 6.5f * Pieces.Pitch;
            // Top row legs 1..14 from the right, to D0..D13; each goes up, bends and runs to its header pin.
            float[] digital = { 62.68f, 60.14f, 57.6f, 55.06f, 52.52f, 49.98f, 47.44f, 44.9f, 40.8f, 38.26f, 35.72f, 33.18f, 30.64f, 28.1f };
            for (int i = 0; i < digital.Length; i++)
            {
                float leg = firstLeg + (13 - i) * Pieces.Pitch;
                float target = digital[i];
                float bend = 24.2f + i * 0.55f;
                float shift = target - leg;
                var p = new List<Vector2> { new(leg, rowTop + 1.4f), new(leg, bend) };
                p.Add(new Vector2(target, bend + Mathf.Abs(shift)));
                p.Add(new Vector2(target, 49.2f));
                yield return (p, 0.3f);
                if (i % 3 == 1) Vias.Add(new Vector2(leg, bend - 0.9f));
            }
            // Bottom row legs to the analog pins A0..A5 and the supply.
            for (int i = 0; i < 6; i++)
            {
                float leg = firstLeg + (8 + i) * Pieces.Pitch;
                float target = 50.8f + i * Pieces.Pitch;
                float bend = 11.2f - i * 0.5f;
                var p = new List<Vector2> { new(leg, rowBottom - 1.4f), new(leg, bend) };
                p.Add(new Vector2(target, bend - Mathf.Abs(target - leg)));
                p.Add(new Vector2(target, 4.1f));
                yield return (p, 0.3f);
            }
            // USB data pair into the 16U2.
            for (int i = 0; i < 2; i++)
            {
                float y = 37.3f + i * 1.3f;
                yield return (new List<Vector2> { new(10.4f, y), new(15.0f, y), new(17.6f, y - 0.3f + i * 0.2f) }, 0.35f);
            }
            // Serial lines leave the 16U2 and drop through to the other side.
            for (int i = 0; i < 2; i++)
            {
                float x = 21.8f + i * 1.0f;
                yield return (new List<Vector2> { new(x, U2.y - 2.8f), new(x, 30.9f - i * 0.8f) }, 0.28f);
                Vias.Add(new Vector2(x, 30.9f - i * 0.8f));
            }
            // LEDs to their resistor network.
            float[] lanes = { 24.6f, 24.0f, 23.4f };
            for (int i = 0; i < 3; i++)
            {
                float y = Leds[i].at.y;
                yield return (new List<Vector2> { new(Leds[i].at.x - 1.0f, y), new(lanes[i], y), new(lanes[i], 41.3f) }, 0.25f);
            }
            // Supply: the jack to the regulator, the regulator's tab to the capacitor and along to 5V, wide.
            yield return (new List<Vector2> { new(11.6f, 7.8f), new(13.4f, 5.2f), new(Regulator.x - 2.3f, 5.2f) }, 0.9f);
            yield return (new List<Vector2> { new(Regulator.x, 11.4f), new(Regulator.x, 13.0f), new(Caps[1].x + 3.1f, 13.0f), new(Caps[1].x + 3.1f, 16.8f) }, 0.9f);
            yield return (new List<Vector2> { new(Regulator.x + 1.9f, 11.2f), new(35.58f, 11.2f), new(35.58f, 4.1f) }, 0.7f);
            yield return (new List<Vector2> { new(Caps[0].x - 3.1f, 16.4f), new(Caps[0].x - 3.1f, 12.2f), new(8.0f, 12.2f) }, 0.6f);
            // The 16U2 to its ICSP header, fanned out at 45°.
            float[] icspPins = { Icsp1.x - Pieces.Pitch, Icsp1.x, Icsp1.x + Pieces.Pitch };
            for (int i = 0; i < 3; i++)
            {
                float from = U2.x - 1.0f + i, shift = from - icspPins[i], pinY = Icsp1.y - 1.27f;
                yield return (new List<Vector2> { new(from, U2.y + 2.8f), new(from, pinY - shift), new(icspPins[i], pinY) }, 0.25f);
            }
            yield return (new List<Vector2> { new(Reset.x + 3.9f, Reset.y - 2.25f), new(11.8f, 44.8f), new(11.8f, 41.0f) }, 0.3f);
            // The op-amp's legs drop through to the other side.
            for (int i = 0; i < 4; i++)
            {
                float x = Soic.x - 1.905f + i * 1.27f;
                yield return (new List<Vector2> { new(x, Soic.y + 3.9f), new(x, Soic.y + 5.0f + (i % 2) * 0.9f) }, 0.25f);
                Vias.Add(new Vector2(x, Soic.y + 5.0f + (i % 2) * 0.9f));
                yield return (new List<Vector2> { new(x, Soic.y - 3.9f), new(x, Soic.y - 5.0f - (i % 2) * 0.9f) }, 0.25f);
                Vias.Add(new Vector2(x, Soic.y - 5.0f - (i % 2) * 0.9f));
            }
            Vias.Add(new Vector2(36.5f, 33.2f));
            Vias.Add(new Vector2(55.5f, 22.4f));
            Vias.Add(new Vector2(58.2f, 24.9f));
            Vias.Add(new Vector2(10.4f, 20.6f));
            Vias.Add(new Vector2(25.0f, 22.0f));
            Vias.Add(new Vector2(64.8f, 12.4f));
        }

        static void Silkscreen(Raster r)
        {
            var silk = SilkInk;
            // Pin names under the digital header, reading up the board.
            string[] names = { "AREF", "GND", "13", "12", "~11", "~10", "~9", "8", "7", "~6", "~5", "4", "~3", "2", "TX▸1", "RX◂0" };
            float[] xs = { 23.0f, 25.5f, 28.1f, 30.64f, 33.18f, 35.72f, 38.26f, 40.8f, 44.9f, 47.44f, 49.98f, 52.52f, 55.06f, 57.6f, 60.14f, 62.68f };
            for (int i = 0; i < names.Length; i++) r.Text(names[i], xs[i] + 0.5f, 48.4f, 1.0f, silk, Align.Right, 90);
            r.Text("DIGITAL (PWM~)", 45.0f, 40.3f, 1.25f, silk, Align.Centre);
            r.Line(21.8f, 49.4f, 42.1f, 49.4f, 0.15f, silk);
            r.Line(43.6f, 49.4f, 64.0f, 49.4f, 0.15f, silk);
            // Over the power and analog headers, reading up the board.
            string[] power = { "", "IOREF", "RESET", "3.3V", "5V", "GND", "GND", "VIN" };
            for (int i = 0; i < power.Length; i++) r.Text(power[i], 22.88f + i * Pieces.Pitch + 0.5f, 4.5f, 1.0f, silk, Align.Left, 90);
            for (int i = 0; i < 6; i++) r.Text("A" + i, 50.8f + i * Pieces.Pitch + 0.5f, 4.5f, 1.0f, silk, Align.Left, 90);
            r.Text("POWER", 32.5f, 10.4f, 1.25f, silk, Align.Centre);
            r.Text("ANALOG IN", 57.2f, 10.4f, 1.25f, silk, Align.Centre);
            r.Line(24.1f, 3.9f, 44.5f, 3.9f, 0.15f, silk);
            r.Line(49.5f, 3.9f, 64.8f, 3.9f, 0.15f, silk);

            // The name, large.
            r.Text("UNO", 42.2f, 26.4f, 5.2f, silk, Align.Centre, 0, 0.2f, 1.3f);
            r.Text("R3", 53.8f, 26.4f, 2.0f, silk, Align.Left, 0, 0.18f);

            // Outlines and names of the parts.
            SilkBox(r, Chip, new Vector2(36.8f, 10.8f));
            r.Circle(Chip.x + 17.0f, Chip.y - 3.2f, 0.3f, silk); // pin 1
            SilkBox(r, U2, new Vector2(6.2f, 6.2f));
            SilkBox(r, Icsp, new Vector2(5.6f, 8.1f));
            r.Text("ICSP", Icsp.x - 3.4f, Icsp.y, 1.0f, silk, Align.Centre, 90);
            SilkBox(r, Icsp1, new Vector2(8.1f, 5.6f));
            r.Text("ICSP", Icsp1.x, Icsp1.y - 3.8f, 0.9f, silk, Align.Centre);
            r.Text("RESET", Reset.x, Reset.y - 4.6f, 0.9f, silk, Align.Centre);
            foreach (var led in Leds)
            {
                bool right = led.name != "ON";
                r.Text(led.name, led.at.x + (right ? 1.9f : -1.9f), led.at.y - 0.45f, 0.9f, silk, right ? Align.Left : Align.Right);
                SilkBox(r, led.at, new Vector2(2.8f, 1.9f));
            }
            foreach (var c in Caps) r.Circle(c.x, c.y, 3.6f, silk);
            foreach (var c in Caps) r.Circle(c.x, c.y, 3.45f, MaskInk);
            r.Text("+", Caps[0].x - 4.2f, Caps[0].y + 2.6f, 1.0f, silk, Align.Centre);
            r.Text("+", Caps[1].x - 4.2f, Caps[1].y + 2.6f, 1.0f, silk, Align.Centre);
            r.Text("16.000", Xtal.x, Xtal.y - 3.8f, 0.8f, silk, Align.Centre);
            r.Text("1", Chip.x + 18.8f, Chip.y - 3.9f, 0.8f, silk, Align.Centre);
        }

        static void SilkBox(Raster r, Vector2 centre, Vector2 size)
        {
            float x0 = centre.x - size.x / 2, x1 = centre.x + size.x / 2, y0 = centre.y - size.y / 2, y1 = centre.y + size.y / 2;
            var silk = SilkInk;
            r.Line(x0, y0, x1, y0, 0.15f, silk);
            r.Line(x1, y0, x1, y1, 0.15f, silk);
            r.Line(x1, y1, x0, y1, 0.15f, silk);
            r.Line(x0, y1, x0, y0, 0.15f, silk);
        }

        /// <summary>The chips' laser markings, the crystal's stamped top and the capacitors' print, above the board.</summary>
        static void Marks(Raster r)
        {
            var epoxy = Ink.Solid(new Color32(24, 24, 26, 255), 0, 0.32f, 0);
            var laser = Ink.Paint(new Color32(84, 84, 88, 255), 0, 0.18f);
            r.Rect(ChipMark.xMin, ChipMark.yMin, ChipMark.xMax, ChipMark.yMax, epoxy);
            float cy = ChipMark.center.y;
            r.Text("ATMEGA328P-PU", ChipMark.center.x - 1.0f, cy + 0.6f, 1.25f, laser, Align.Centre);
            r.Text("1839  AVR", ChipMark.center.x - 1.0f, cy - 1.6f, 0.95f, laser, Align.Centre);
            r.Circle(ChipMark.xMax - 1.2f, cy, 1.1f, Ink.Solid(new Color32(14, 14, 15, 255), 0, 0.2f, -0.3f)); // the notch's end
            r.Circle(ChipMark.xMax - 3.0f, ChipMark.yMin + 1.4f, 0.55f, Ink.Solid(new Color32(18, 18, 20, 255), 0, 0.6f, -0.1f)); // pin 1 dimple

            r.Rect(U2Mark.xMin, U2Mark.yMin, U2Mark.xMax, U2Mark.yMax, epoxy);
            r.Text("MEGA16U2", U2Mark.center.x, U2Mark.center.y + 0.3f, 0.55f, laser, Align.Centre);
            r.Text("MU 1838", U2Mark.center.x, U2Mark.center.y - 0.8f, 0.5f, laser, Align.Centre);
            r.Circle(U2Mark.xMin + 0.6f, U2Mark.yMax - 0.6f, 0.25f, Ink.Solid(new Color32(16, 16, 17, 255), 0, 0.6f, -0.05f));

            r.Rect(SoicMark.xMin, SoicMark.yMin, SoicMark.xMax, SoicMark.yMax, epoxy);
            r.Text("LMV358", SoicMark.center.x, SoicMark.center.y - 0.2f, 0.7f, laser, Align.Centre);

            r.Rect(RegulatorMark.xMin, RegulatorMark.yMin, RegulatorMark.xMax, RegulatorMark.yMax, epoxy);
            r.Text("1117-5.0", RegulatorMark.center.x, RegulatorMark.center.y - 0.3f, 0.8f, laser, Align.Centre);

            r.Rect(XtalMark.xMin, XtalMark.yMin, XtalMark.xMax, XtalMark.yMax, Ink.Solid(new Color32(214, 214, 210, 255), 1, 0.84f, 0));
            r.Text("16.000", XtalMark.center.x, XtalMark.center.y - 0.5f, 1.2f, Ink.Solid(new Color32(150, 150, 146, 255), 1, 0.45f, -0.03f), Align.Centre);

            var aluminium = Ink.Solid(new Color32(214, 214, 216, 255), 1, 0.58f, 0);
            r.Rect(CapMark.xMin, CapMark.yMin, CapMark.xMax, CapMark.yMax, aluminium);
            var c = CapMark.center;
            var half = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.PI * (0.25f + 0.5f * i / 16);
                half.Add(new Vector2(c.x + Mathf.Cos(a) * 3.1f, c.y + Mathf.Sin(a) * 3.1f));
            }
            r.Polygon(half, Ink.Solid(new Color32(18, 18, 20, 255), 0, 0.35f, 0));
            var ink = Ink.Solid(new Color32(24, 24, 26, 255), 0, 0.35f, 0);
            r.Text("47", c.x, c.y - 0.6f, 1.2f, ink, Align.Centre);
            r.Text("25V", c.x, c.y - 2.2f, 0.9f, ink, Align.Centre);
        }
    }
}
