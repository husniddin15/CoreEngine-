using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// The common red L298N module, 43 × 43 mm (docs/09 §6): the black finned heatsink at the back with the
    /// L298N in its Multiwatt15 package screwed to it, a blue screw terminal for each motor beside the heatsink,
    /// the 3-way power terminal at the front left, the logic header at the front right (ENA and ENB each with a
    /// pin behind it for its jumper; the jumpers themselves are drawn by the robot's look while no wire takes
    /// their pin), the 5V-EN jumper, the 78M05 regulator, two 220 µF capacitors, eight flyback diodes and the
    /// power LED (light "PWR"). Board coordinates: mm from the front left corner, x to the right, y to the back;
    /// the part's frame is the middle of the board, 1.6 mm under it.
    /// </summary>
    static class L298NModel
    {
        const float S = 43f, ArtHeight = 55f, Bottom = 1.6f, Top = 3.2f, O = 21.5f;
        const float SinkX0 = 10f, SinkX1 = 33f, SinkFront = 29f, SinkPlate = 2.5f, FinDepth = 9.5f, SinkHeight = 23f;
        const float HeaderY = 7.0f;

        /// <summary>Where the header's pins leave the board, above the part's frame: a jumper cap sits from here up.</summary>
        public const float PinFoot = Top;

        static readonly Vector2[] Holes = { new Vector2(3, 3), new Vector2(40.5f, 2.5f), new Vector2(3, 40), new Vector2(40, 40) };
        static readonly float[] HeaderX = { 25.0f, 27.54f, 30.08f, 32.62f, 35.16f, 37.7f };
        static readonly string[] HeaderNames = { "ENA", "IN1", "IN2", "IN3", "IN4", "ENB" };
        static readonly Vector2 Regulator = new Vector2(16.0f, 13.2f);
        static readonly Vector2 EnJumper = new Vector2(21.9f, 10.2f);
        static readonly Vector2[] Caps = { new Vector2(5.5f, 18.5f), new Vector2(37.5f, 18.5f) };
        static readonly Vector2 Led = new Vector2(40.8f, 12.2f);
        static readonly float[] DiodeX = { 13.0f, 16.8f, 20.6f, 24.4f };
        static readonly float[] DiodeY = { 18.6f, 20.6f };

        static readonly Rect ChipMark = Rect.MinMaxRect(0.5f, 44.0f, 20.1f, 54.0f);
        static readonly Rect RegulatorMark = Rect.MinMaxRect(21.0f, 44.0f, 27.5f, 50.1f);

        static Raster? art;

        static Material TopMaterial => PartLooks.Textured("l298n top", Paint, 1.1f);

        static Vector2 Uv(Vector2 p) => art!.Uv(p.x, p.y);

        static Rect Uv(Rect r) => art!.UvRect(r.xMin, r.yMin, r.xMax, r.yMax);

        /// <summary>A jumper cap: the black shell over two pins 2.54 mm apart along z, from the base of the pins up.</summary>
        public static Mesh JumperMesh => jumper ??= MakeJumper();
        static Mesh? jumper;

        static Mesh MakeJumper()
        {
            var kit = new MeshKit(1);
            kit.Box(0, new Vector3(0, 5.5f, 1.27f), new Vector3(2.5f, 6.0f, 5.0f), 0.35f);
            kit.Box(0, new Vector3(0, 8.7f, 1.27f), new Vector3(2.0f, 0.4f, 3.6f), 0.15f);
            return kit.ToMesh("Jumper");
        }

        public static PartModel Make()
        {
            var top = TopMaterial;
            var b = new Bench();
            var k = b.Kit;
            k.Push(new Vector3(-O, 0, -O));
            var outline = new List<Vector2>();
            foreach (var (cx, cy, a0) in new[] { (S - 1, 1f, -90f), (S - 1, S - 1, 0f), (1f, S - 1, 90f), (1f, 1f, 180f) })
                for (int i = 0; i <= 4; i++)
                {
                    float a = (a0 + 90f * i / 4) * Mathf.Deg2Rad;
                    outline.Add(new Vector2(cx + Mathf.Cos(a), cy + Mathf.Sin(a)));
                }
            Pieces.Board(b, outline, Bottom, Top, top, Uv, PartLooks.Solid("l298n bottom", new Color(0.45f, 0.03f, 0.05f), 0.6f));
            k.Push(new Vector3(0, Top, 0));

            Heatsink(b);
            Chip(b, top);

            var blue = PartLooks.Solid("terminal blue", new Color(0.05f, 0.30f, 0.78f), 0.5f);
            Pieces.ScrewTerminal(b, new Vector3(14.5f, 0, 3.8f), Vector3.right, Vector3.back, 3, 5f, blue);
            Pieces.ScrewTerminal(b, new Vector3(3.8f, 0, 33f), Vector3.forward, Vector3.left, 2, 5f, blue);
            Pieces.ScrewTerminal(b, new Vector3(S - 3.8f, 0, 33f), Vector3.forward, Vector3.right, 2, 5f, blue);

            // The logic header, and the pins behind ENA and ENB that their jumpers join them to.
            Pieces.MaleHeader(b, new Vector3(HeaderX[0], 0, HeaderY), Vector3.right, 6);
            Pieces.MaleHeader(b, new Vector3(HeaderX[0], 0, HeaderY + Pieces.Pitch), Vector3.right, 1);
            Pieces.MaleHeader(b, new Vector3(HeaderX[5], 0, HeaderY + Pieces.Pitch), Vector3.right, 1);
            // 5V-EN: two pins and the jumper that is always on.
            Pieces.MaleHeader(b, new Vector3(EnJumper.x, 0, EnJumper.y), Vector3.forward, 2);
            k.Push(new Vector3(EnJumper.x, 0, EnJumper.y));
            k.Box(b[PartLooks.BlackPlastic], new Vector3(0, 5.5f, 1.27f), new Vector3(2.5f, 6.0f, 5.0f), 0.35f);
            k.Box(b[PartLooks.BlackPlastic], new Vector3(0, 8.7f, 1.27f), new Vector3(2.0f, 0.4f, 3.6f), 0.15f);
            k.Pop();

            // The regulator (DPAK): body, tab toward the front, legs toward the back.
            k.Box(b[PartLooks.MatteBlack], new Vector3(Regulator.x, 1.25f, Regulator.y), new Vector3(6.5f, 2.3f, 6.1f), 0.12f,
                topUv: Uv(RegulatorMark), topSlot: b[top]);
            k.Box(b[PartLooks.Tin], new Vector3(Regulator.x, 0.3f, Regulator.y - 3.65f), new Vector3(5.3f, 0.5f, 1.2f), 0.05f);
            foreach (int side in new[] { -1, 1 })
            {
                k.Box(b[PartLooks.Tin], new Vector3(Regulator.x + side * 2.28f, 0.9f, Regulator.y + 3.35f), new Vector3(0.7f, 0.4f, 0.6f));
                k.Box(b[PartLooks.Tin], new Vector3(Regulator.x + side * 2.28f, 0.2f, Regulator.y + 3.9f), new Vector3(0.8f, 0.3f, 1.0f));
            }

            var sleeve = Pieces.CapSleeve("black 220", new Color32(22, 22, 26, 255), new Color32(196, 170, 96, 255), "220µF 35V");
            foreach (var c in Caps) Pieces.Electrolytic(b, new Vector3(c.x, 0, c.y), 8f, 11.5f, sleeve, 90);

            var diode = PartLooks.Solid("diode", new Color(0.06f, 0.06f, 0.065f), 0.35f);
            var band = PartLooks.Solid("diode band", new Color(0.55f, 0.56f, 0.58f), 0.4f);
            foreach (float y in DiodeY)
                foreach (float x in DiodeX)
                {
                    k.Box(b[diode], new Vector3(x, 0.55f, y), new Vector3(2.7f, 1.1f, 1.6f), 0.12f);
                    k.Box(b[band], new Vector3(x - 0.95f, 0.56f, y), new Vector3(0.45f, 1.12f, 1.62f), 0.1f);
                    foreach (int side in new[] { -1, 1 })
                        k.Box(b[PartLooks.Tin], new Vector3(x + side * 1.55f, 0.15f, y), new Vector3(0.6f, 0.3f, 1.0f));
                }

            // The power LED and its resistor.
            k.Box(b[PartLooks.WhitePlastic], new Vector3(Led.x, 0.2f, Led.y), new Vector3(1.25f, 0.4f, 2.0f), 0.05f);
            Pieces.Smd(b, new Vector3(Led.x, 0, Led.y - 2.4f), 1.6f, 0.8f, 0.45f, Pieces.ResistorBody, alongZ: true);
            b.Light("PWR", new Vector3(Led.x - O, Top + 0.62f, Led.y - O), new Vector3(1.1f, 0.45f, 1.3f), new Color(0.95f, 0.85f, 0.8f), new Color(1f, 0.12f, 0.08f));
            Pieces.Smd(b, new Vector3(11.0f, 0, 10.5f), 2.0f, 1.25f, 0.8f, Pieces.CapacitorBody, alongZ: true);
            Pieces.Smd(b, new Vector3(20.2f, 0, 16.4f), 1.6f, 0.8f, 0.8f, Pieces.CapacitorBody);

            k.Pop();
            k.Pop();
            return b.Finish("L298N module");
        }

        /// <summary>The black anodised heatsink: a plate against the chip and fins running back from it.</summary>
        static void Heatsink(Bench b)
        {
            int metal = b[PartLooks.Anodised];
            float width = SinkX1 - SinkX0;
            b.Kit.Box(metal, new Vector3((SinkX0 + SinkX1) / 2, SinkHeight / 2, SinkFront + SinkPlate / 2), new Vector3(width, SinkHeight, SinkPlate), 0.3f);
            const int fins = 7;
            for (int i = 0; i < fins; i++)
            {
                float x = SinkX0 + 0.6f + i * (width - 1.2f) / (fins - 1);
                b.Kit.Box(metal, new Vector3(x, SinkHeight / 2, SinkFront + SinkPlate + FinDepth / 2), new Vector3(1.2f, SinkHeight, FinDepth), 0.25f);
            }
            b.Kit.Box(metal, new Vector3((SinkX0 + SinkX1) / 2, 1.0f, SinkFront + SinkPlate + FinDepth / 2), new Vector3(width, 2.0f, FinDepth), 0.2f);
        }

        /// <summary>
        /// The L298N in Multiwatt15: the plastic body standing against the heatsink, its metal tab above it with the
        /// screw, and fifteen legs in two staggered rows down to the board, the front row bent forward.
        /// </summary>
        static void Chip(Bench b, Material mark)
        {
            const float bodyBottom = 4.5f, bodyHeight = 10f, depth = 4.6f;
            float cx = (SinkX0 + SinkX1) / 2, back = SinkFront, front = back - depth;
            b.Kit.Box(b[PartLooks.MatteBlack], new Vector3(cx, bodyBottom + bodyHeight / 2, (front + back) / 2), new Vector3(19.6f, bodyHeight, depth), 0.25f,
                topUv: Uv(ChipMark), topSlot: b[mark], markFace: MeshKit.Face.MinusZ);
            b.Kit.Box(b[PartLooks.Tin], new Vector3(cx, bodyBottom + bodyHeight + 3.2f, back - 0.75f), new Vector3(19.6f, 6.4f, 1.5f), 0.2f);
            b.Kit.Cylinder(b[PartLooks.Nickel], new Vector3(cx, bodyBottom + bodyHeight + 3.4f, back - 1.5f), new Vector3(cx, bodyBottom + bodyHeight + 3.4f, back - 3.3f), 2.7f, 28, 0.6f);
            b.Kit.Box(b[PartLooks.Hole], new Vector3(cx, bodyBottom + bodyHeight + 3.4f, back - 3.31f), new Vector3(3.0f, 0.6f, 0.05f));
            b.Kit.Box(b[PartLooks.Hole], new Vector3(cx, bodyBottom + bodyHeight + 3.4f, back - 3.31f), new Vector3(0.6f, 3.0f, 0.05f));
            int tin = b[PartLooks.Tin];
            for (int i = 0; i < 15; i++)
            {
                float x = cx - 7 * 1.27f + i * 1.27f;
                if (i % 2 == 0)
                {
                    const float bend = 1.4f;
                    b.Kit.Box(tin, new Vector3(x, (bodyBottom + bend) / 2, front + 0.9f), new Vector3(0.5f, bodyBottom - bend, 0.35f));
                    b.Kit.Box(tin, new Vector3(x, bend, front - 0.4f), new Vector3(0.5f, 0.35f, 2.6f));
                    b.Kit.Box(tin, new Vector3(x, bend / 2, front - 1.55f), new Vector3(0.5f, bend, 0.35f));
                }
                else b.Kit.Box(tin, new Vector3(x, bodyBottom / 2, front + 3.0f), new Vector3(0.5f, bodyBottom, 0.35f));
            }
        }

        // ------------------------------------------------------------------ the painted board

        static readonly Color32 Mask = new Color32(146, 12, 20, 255);
        static readonly Color32 CopperMask = new Color32(174, 24, 30, 255);
        static readonly Color32 Silk = new Color32(238, 238, 234, 255);

        static Ink MaskInk => Ink.Solid(Mask, 0, 0.74f, 0);
        static Ink CopperInk => Ink.Solid(CopperMask, 0, 0.76f, 0.035f);
        static Ink PadInk => Ink.Solid(new Color32(206, 206, 201, 255), 1, 0.55f, 0.07f);
        static Ink SilkInk => Ink.Paint(Silk, 0, 0.32f);

        static Raster Paint()
        {
            var r = new Raster(S, ArtHeight, 18);
            art = r;
            r.Fill(MaskInk);
            r.RoundRect(S / 2, S / 2, S - 3, S - 3, 2, CopperInk);

            foreach (var (points, width) in Routes()) Route(r, points, width);
            foreach (var (x, y) in new[] { (10.9f, 26.2f), (32.1f, 26.2f), (19.0f, 24.4f), (23.8f, 24.2f), (8.0f, 26.8f), (35.2f, 26.8f), (29.4f, 11.2f), (12.4f, 8.8f) })
                Via(r, new Vector2(x, y));

            // Pads the parts sit on show round their ends.
            foreach (float y in DiodeY)
                foreach (float x in DiodeX)
                    foreach (int side in new[] { -1, 1 }) Pad(r, new Vector2(x + side * 1.55f, y), new Vector2(1.0f, 1.3f));
            Pad(r, Regulator + new Vector2(0, -3.65f), new Vector2(5.8f, 1.8f));
            foreach (int side in new[] { -1, 1 }) Pad(r, Regulator + new Vector2(side * 2.28f, 3.9f), new Vector2(1.2f, 1.6f));
            Pad(r, Led + new Vector2(0, 0.9f), new Vector2(1.3f, 0.8f));
            Pad(r, Led + new Vector2(0, -0.9f), new Vector2(1.3f, 0.8f));

            foreach (var h in Holes)
            {
                r.Circle(h.x, h.y, 2.3f, Ink.Solid(new Color32(206, 206, 201, 255), 1, 0.55f, 0.06f));
                r.Circle(h.x, h.y, 1.6f, Ink.Solid(new Color32(14, 14, 14, 255), 0, 0.05f, -1.2f));
            }

            var silk = SilkInk;
            // IN1..IN4 behind their pins; ENA and ENB behind the pins their jumpers join them to.
            for (int i = 0; i < 6; i++)
            {
                bool enable = i == 0 || i == 5;
                r.Text(HeaderNames[i], HeaderX[i], enable ? HeaderY + 4.2f : HeaderY + 2.0f, 0.85f, silk, Align.Centre);
            }
            string[] power = { "+12V", "GND", "+5V" };
            for (int i = 0; i < 3; i++) r.Text(power[i], 9.5f + 5 * i, 7.95f, 0.95f, silk, Align.Centre);
            r.Text("OUT2", 9.4f, 30.5f, 0.8f, silk, Align.Centre, 90);
            r.Text("OUT1", 9.4f, 35.5f, 0.8f, silk, Align.Centre, 90);
            r.Text("OUT3", 34.6f, 30.5f, 0.8f, silk, Align.Centre, 90);
            r.Text("OUT4", 34.6f, 35.5f, 0.8f, silk, Align.Centre, 90);
            r.Text("5V-EN", 20.25f, EnJumper.y + 1.27f, 0.75f, silk, Align.Centre, 90);
            r.Text("L298N", 2.9f, 9.6f, 1.4f, silk, Align.Centre, 90);
            r.Text("PWR", Led.x - 1.2f, Led.y + 1.6f, 0.7f, silk, Align.Right);
            foreach (var c in Caps)
            {
                r.Ring(c.x, c.y, 4.4f, 4.25f, silk);
                r.Text("+", c.x + 4.6f, c.y + 2.4f, 1.0f, silk, Align.Centre);
            }
            SilkBox(r, new Vector2(21.5f, 22.4f), new Vector2(21.6f, 5.4f));
            r.Grain(0.025f, 0.05f, 0.9f, 23);
            Marks(r);
            return r;
        }

        static void Pad(Raster r, Vector2 centre, Vector2 size)
        {
            r.RoundRect(centre.x, centre.y, size.x + 0.5f, size.y + 0.5f, 0.3f, MaskInk);
            r.RoundRect(centre.x, centre.y, size.x, size.y, 0.12f, PadInk);
        }

        static void Route(Raster r, IList<Vector2> points, float width)
        {
            r.Polyline(points, width + 0.6f, MaskInk);
            r.Polyline(points, width, CopperInk);
        }

        static void Via(Raster r, Vector2 at)
        {
            r.Circle(at.x, at.y, 0.75f, MaskInk);
            r.Circle(at.x, at.y, 0.48f, Ink.Solid(new Color32(214, 46, 52, 255), 0, 0.74f, 0.05f));
            r.Circle(at.x, at.y, 0.18f, Ink.Solid(new Color32(70, 6, 10, 255), 0, 0.5f, -0.2f));
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

        /// <summary>Wide traces for the motor outputs and the supply, thin ones from the inputs up to the chip at 45°.</summary>
        static IEnumerable<(List<Vector2> points, float width)> Routes()
        {
            yield return (new List<Vector2> { new(13.2f, 23.0f), new(10.2f, 23.0f), new(8.9f, 24.3f), new(8.9f, 30.5f), new(5.5f, 30.5f) }, 1.2f);
            yield return (new List<Vector2> { new(15.8f, 23.6f), new(12.0f, 27.4f), new(12.0f, 35.5f), new(5.5f, 35.5f) }, 1.2f);
            yield return (new List<Vector2> { new(29.8f, 23.0f), new(32.8f, 23.0f), new(34.1f, 24.3f), new(34.1f, 30.5f), new(37.5f, 30.5f) }, 1.2f);
            yield return (new List<Vector2> { new(27.2f, 23.6f), new(31.0f, 27.4f), new(31.0f, 35.5f), new(37.5f, 35.5f) }, 1.2f);
            yield return (new List<Vector2> { new(9.5f, 7.8f), new(9.5f, 11.5f), new(6.9f, 14.1f) }, 1.2f);
            yield return (new List<Vector2> { new(19.5f, 7.8f), new(21.9f, 9.2f) }, 0.8f);
            float[] legs = { 26.6f, 27.9f, 29.1f, 30.4f };
            for (int i = 0; i < 4; i++)
            {
                float from = HeaderX[i + 1], bend = 12f + 0.8f * i, shift = from - legs[i];
                yield return (new List<Vector2> { new(from, HeaderY + 1.3f), new(from, bend), new(legs[i], bend + shift), new(legs[i], 22.4f) }, 0.3f);
            }
        }

        static void Marks(Raster r)
        {
            var epoxy = Ink.Solid(new Color32(22, 22, 24, 255), 0, 0.3f, 0);
            var laser = Ink.Paint(new Color32(90, 90, 94, 255), 0, 0.18f);
            r.Rect(ChipMark.xMin, ChipMark.yMin, ChipMark.xMax, ChipMark.yMax, epoxy);
            var c = ChipMark.center;
            r.Text("L298N", c.x, c.y + 0.9f, 2.0f, laser, Align.Centre);
            r.Text("E3  GK937", c.x, c.y - 1.6f, 0.95f, laser, Align.Centre);
            r.Text("MAR 21", c.x, c.y - 3.2f, 0.85f, laser, Align.Centre);
            r.Rect(RegulatorMark.xMin, RegulatorMark.yMin, RegulatorMark.xMax, RegulatorMark.yMax, epoxy);
            r.Text("78M05", RegulatorMark.center.x, RegulatorMark.center.y - 0.1f, 1.0f, laser, Align.Centre);
            r.Text("ST 142", RegulatorMark.center.x, RegulatorMark.center.y - 1.7f, 0.7f, laser, Align.Centre);
        }
    }
}
