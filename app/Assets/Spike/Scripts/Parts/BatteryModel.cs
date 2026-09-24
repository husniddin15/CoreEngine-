using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// A 4×AA holder, 58 × 15 × 62 mm (docs/09 §5): the black open tray with its ribs, four alkaline cells lying
    /// head to tail with their printed wraps, a steel spring at each cell's minus end and a flat contact at its
    /// plus end, and the red and black leads leaving the back wall where the catalogue has their ends.
    /// </summary>
    static class BatteryModel
    {
        const float CellRadius = 7.1f, CellLength = 50.5f;
        static readonly float[] CellX = { -21.75f, -7.25f, 7.25f, 21.75f };

        /// <summary>An alkaline cell's wrap: black with a copper band at the plus end and its print along it.</summary>
        static Material Wrap => PartLooks.Textured("aa wrap", () =>
        {
            // u runs once round (44.6 mm), v along the cell from its minus end (0) to its plus end (1).
            var r = new Raster(44.6f, CellLength, 12);
            r.Fill(Ink.Solid(new Color32(20, 20, 22, 255), 0.3f, 0.62f, 0));
            r.Rect(0, 36f, 44.6f, CellLength, Ink.Solid(new Color32(196, 120, 52, 255), 0.85f, 0.7f, 0));
            r.Rect(0, 35.4f, 44.6f, 36f, Ink.Solid(new Color32(214, 214, 210, 255), 0.9f, 0.6f, 0));
            var print = Ink.Paint(new Color32(222, 222, 216, 255), 0, 0.5f);
            r.Text("ALKALINE", 16f, 18f, 3.4f, print, Align.Centre, 90, 0.17f);
            r.Text("AA  1.5V", 30f, 18f, 2.6f, print, Align.Centre, 90, 0.16f);
            r.Text("+", 22.3f, 41.5f, 4.2f, Ink.Paint(new Color32(24, 24, 26, 255), 0, 0.5f), Align.Centre, 0, 0.2f);
            return r;
        }, 0.6f);

        public static PartModel Make()
        {
            var b = new Bench();
            var k = b.Kit;
            int tray = b[PartLooks.Solid("holder plastic", new Color(0.035f, 0.035f, 0.04f), 0.4f)];
            int steel = b[PartLooks.Nickel];
            // The tray: floor, side walls, end walls and the low ribs between the cells.
            k.Box(tray, new Vector3(0, -7.1f, 0), new Vector3(58f, 0.8f, 62f), 0.3f);
            foreach (int side in new[] { -1, 1 })
            {
                k.Box(tray, new Vector3(side * 28.4f, -2.5f, 0), new Vector3(1.2f, 10f, 62f), 0.4f);
                k.Box(tray, new Vector3(0, -0.5f, side * 30.4f), new Vector3(58f, 14f, 1.2f), 0.4f);
            }
            foreach (float x in new[] { -14.5f, 0f, 14.5f }) k.Box(tray, new Vector3(x, -5.2f, 0), new Vector3(0.9f, 3.6f, 52f), 0.3f);

            var wrap = b[Wrap];
            for (int i = 0; i < 4; i++)
            {
                float x = CellX[i], y = -6.7f + CellRadius;
                float sign = i % 2 == 0 ? 1 : -1; // the plus end toward +z on the first cell, then head to tail
                var minus = new Vector3(x, y, -sign * CellLength / 2);
                var along = new Vector3(0, 0, sign);
                // The wrap along the cell, its rolled edges, the steel ends and the plus end's button.
                k.Lathe(wrap, minus, along, new List<Vector2> { new(CellRadius, 0.35f), new(CellRadius, CellLength - 0.35f) }, 40, 30, 0, -360, 1, 1f / CellLength);
                k.Lathe(steel, minus, along, new List<Vector2> { new(0, 0), new(CellRadius - 0.6f, 0), new(CellRadius, 0.35f) }, 40, 50);
                k.Lathe(steel, minus, along, new List<Vector2> { new(CellRadius, CellLength - 0.35f), new(CellRadius - 0.6f, CellLength), new(2.9f, CellLength), new(2.9f, CellLength + 1.1f), new(0, CellLength + 1.1f) }, 40, 50);
                // A spring against the minus end, a flat contact against the plus end.
                var coil = new List<Vector3>();
                for (int s = 0; s <= 96; s++)
                {
                    float t = s / 96f, a = t * Mathf.PI * 2 * 5;
                    coil.Add(new Vector3(x + Mathf.Cos(a) * 3.6f, y + Mathf.Sin(a) * 3.6f, -sign * (CellLength / 2 + 3.9f - t * 3.6f)));
                }
                k.Sweep(steel, coil, 0.32f, 6);
                k.Box(steel, new Vector3(x, y, sign * (CellLength / 2 + 1.1f + 0.2f)), new Vector3(8f, 9f, 0.4f), 0.1f);
            }
            // The leads leave the back wall (-z) where the catalogue has their ends.
            foreach (var (x, colour) in new[] { (12f, new Color(0.8f, 0.06f, 0.06f)), (-12f, new Color(0.06f, 0.06f, 0.07f)) })
            {
                int insulation = b[PartLooks.Solid(colour.r > 0.5f ? "lead red" : "lead black", colour, 0.45f)];
                k.Cylinder(insulation, new Vector3(x, 5f, -30.0f), new Vector3(x, 5f, -31.5f), 0.8f, 12, 0.2f);
            }
            return b.Finish("Battery holder 4×AA");
        }
    }

    /// <summary>
    /// A 20 mm ball caster (docs/09 §9): the chrome ball held by a black housing below its equator, the housing
    /// rising to a square flange with two screw holes that fixes it under a plate. Frame: the ball's middle.
    /// </summary>
    static class CasterModel
    {
        public static PartModel Make()
        {
            var b = new Bench();
            var k = b.Kit;
            k.Sphere(b[PartLooks.Chrome], Vector3.zero, 10f, 48);
            int housing = b[PartLooks.Solid("caster housing", new Color(0.05f, 0.05f, 0.055f), 0.42f)];
            // The holder: a ring round the ball below its middle, closing over it, and a plate with two ears on top.
            var profile = new List<Vector2>
            {
                new(9.75f, -2.4f), new(10.9f, -2.0f), new(11.2f, -1.0f), new(11.2f, 3.5f), new(10.6f, 6.5f), new(9.0f, 9.0f), new(0, 10.2f),
            };
            k.Lathe(housing, Vector3.zero, Vector3.up, profile, 48, 30);
            k.Box(housing, new Vector3(0, 10.5f, 0), new Vector3(22f, 2.6f, 11f), 1.0f);
            foreach (int side in new[] { -1, 1 })
            {
                // A brass hex spacer from each ear up to the plate, the screw's head under the ear.
                var at = new Vector3(side * 8f, 11.8f, 0);
                k.Cylinder(b[PartLooks.Brass], at, at + Vector3.up * 13.2f, 2.3f, 6, 0.15f);
                k.Cylinder(b[PartLooks.Nickel], at - Vector3.up * 2.6f, at - Vector3.up * 3.8f, 2.6f, 20, 0.4f);
            }
            return b.Finish("Ball caster");
        }
    }
}
