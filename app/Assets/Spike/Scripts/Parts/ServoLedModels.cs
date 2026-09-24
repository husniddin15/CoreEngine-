using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// The SG90 micro servo (docs/09 §6): the blue case with its mounting tabs, the gear housing on top with the
    /// output spline toward +x, the paper label, and the brown, red and orange lead leaving the -x end and doubling
    /// back to its 3-way socket (mouth toward +x, where the catalogue has the pins). The horn is its own mesh
    /// (<see cref="HornMesh"/>) on the spline at <see cref="HornPivot"/>, so it can turn.
    /// </summary>
    static class ServoModel
    {
        const float CaseLength = 22.8f, CaseWidth = 12.2f, CaseHeight = 22.7f, TabBottom = 15.9f, TabTop = 18.4f, CoverTop = 3.0f;
        const float TabHalfLength = 16.1f, TabHole = 13.8f, TabHoleRadius = 1.0f, SlitHalfWidth = 0.45f;

        /// <summary>The moulded case's blue: deep and glossy, as the translucent plastic looks over the dark parts inside.</summary>
        static readonly Color Blue = new Color(0.13f, 0.27f, 0.66f);

        /// <summary>Where the horn sits, in the part's frame (mm): on top of the output spline.</summary>
        public static readonly Vector3 HornPivot = new Vector3(5.3f, 29.2f, 0);

        static Material Label => PartLooks.Textured("sg90 label", () =>
        {
            var r = new Raster(22f, 9f, 24);
            r.Fill(Ink.Solid(new Color32(236, 236, 230, 255), 0, 0.3f, 0));
            var ink = Ink.Paint(new Color32(24, 34, 90, 255), 0, 0.35f);
            r.Text("MICRO SERVO", 11f, 5.2f, 1.9f, ink, Align.Centre, 0, 0.17f);
            r.Text("SG90  9g", 11f, 1.8f, 1.6f, ink, Align.Centre, 0, 0.16f);
            r.Rect(0.6f, 0.5f, 21.4f, 0.8f, ink);
            return r;
        });

        public static PartModel Make()
        {
            var b = new Bench();
            var k = b.Kit;
            int blue = b[PartLooks.Solid("sg90 blue", Blue, 0.72f)];
            int label = b[Label];
            // Three mouldings, as on the real case: the bottom cover, the body with the mounting tabs, the top
            // cover. Their small rounded edges leave a fine seam where they meet.
            k.Box(blue, new Vector3(0, CoverTop / 2, 0), new Vector3(CaseLength, CoverTop, CaseWidth), 0.45f);
            k.Box(blue, new Vector3(0, (CoverTop + TabBottom) / 2, 0), new Vector3(CaseLength, TabBottom - CoverTop, CaseWidth), 0.45f);
            k.Box(blue, new Vector3(0, (TabTop + CaseHeight) / 2, 0), new Vector3(CaseLength, CaseHeight - TabTop, CaseWidth), 0.45f);
            k.Extrude(TabOutline(), TabBottom, TabTop, blue, blue, blue, null, 40);
            // The paper label wrapped on the long side facing -z.
            k.Box(label, new Vector3(0, 7.4f, -CaseWidth / 2 - 0.02f), new Vector3(20.5f, 8.6f, 0.05f), 0, 0.1f,
                new Rect(0, 0, 1, 1), label, MeshKit.Face.MinusZ);
            // The gear housing: a big boss round the output shaft and a smaller one beside it.
            k.Cylinder(blue, new Vector3(HornPivot.x, CaseHeight - 0.1f, 0), new Vector3(HornPivot.x, CaseHeight + 4.0f, 0), 5.9f, 48, 0.35f);
            k.Cylinder(blue, new Vector3(-1.2f, CaseHeight - 0.1f, 0), new Vector3(-1.2f, CaseHeight + 3.2f, 0), 3.2f, 32, 0.3f);
            k.Box(blue, new Vector3(2.0f, CaseHeight + 1.5f, 0), new Vector3(6.4f, 3.2f, 6.4f), 0.3f);
            int nylon = b[PartLooks.Solid("sg90 spline", new Color(0.93f, 0.92f, 0.86f), 0.4f)];
            k.Cylinder(nylon, new Vector3(HornPivot.x, CaseHeight + 3.9f, 0), new Vector3(HornPivot.x, HornPivot.y, 0), 2.4f, 20, 0.2f);

            // The lead: three wires side by side out of the -x end, round a U-turn to the socket beside the case.
            var middle = new List<Vector3>
            {
                new(-11.2f, 4.5f, 0), new(-13.4f, 3.6f, 0), new(-14.4f, 1.9f, -2.6f), new(-14.2f, 1.3f, -7.2f), new(-13.2f, 1.3f, -10.4f), new(-12.4f, 1.3f, -11f),
            };
            (float offset, float pinZ, Color colour, string name)[] wires =
            {
                (1.05f, -8.46f, new Color(0.36f, 0.18f, 0.08f), "lead brown"), (0, -11f, new Color(0.8f, 0.06f, 0.06f), "lead red"), (-1.05f, -13.54f, new Color(0.96f, 0.46f, 0.05f), "lead orange"),
            };
            foreach (var wire in wires)
            {
                var path = new List<Vector3>();
                for (int i = 0; i < middle.Count; i++)
                {
                    var tangent = (i + 1 < middle.Count ? middle[i + 1] - middle[i] : middle[i] - middle[i - 1]).normalized;
                    var side = Vector3.Cross(tangent, Vector3.up).normalized;
                    path.Add(middle[i] + side * wire.offset);
                }
                path.Add(new Vector3(-12.0f, 1.3f, wire.pinZ));
                k.Sweep(b[PartLooks.Solid(wire.name, wire.colour, 0.45f)], path, 0.5f, 8);
            }
            // The socket: black, lying flat, three square mouths at its +x end.
            int socket = b[PartLooks.BlackPlastic];
            k.Box(socket, new Vector3(-4.9f, 1.3f, -11f), new Vector3(14.2f, 2.6f, 7.8f), 0.3f);
            k.Box(socket, new Vector3(-4.0f, 2.7f, -11f), new Vector3(6f, 0.3f, 2.2f), 0.1f);
            foreach (float z in new[] { -8.46f, -11f, -13.54f })
                k.Box(b[PartLooks.Hole], new Vector3(2.22f, 1.3f, z), new Vector3(0.06f, 1.1f, 1.1f));
            return b.Finish("SG90 micro servo");
        }

        /// <summary>
        /// The mounting tabs seen from above (x, z): 32.2 × 12.2 mm with rounded corners, and at each end the screw
        /// hole with the slit that runs from it out to the end, cut through as on the real case.
        /// </summary>
        static List<Vector2> TabOutline()
        {
            const float corner = 0.8f, halfWidth = CaseWidth / 2;
            float slitEnd = TabHole + Mathf.Sqrt(TabHoleRadius * TabHoleRadius - SlitHalfWidth * SlitHalfWidth);
            float meet = Mathf.Asin(SlitHalfWidth / TabHoleRadius) * Mathf.Rad2Deg;
            var outline = new List<Vector2>();
            foreach (float s in new[] { 1f, -1f })
            {
                // Round the corner onto this end, along the end to the slit, in along it, clockwise round the
                // hole's far side, back out along the slit and on round the next corner.
                Arc(outline, new Vector2(s * (TabHalfLength - corner), -s * (halfWidth - corner)), corner, s > 0 ? -90 : 90, s > 0 ? 0 : 180, 4, true);
                outline.Add(new Vector2(s * TabHalfLength, -s * SlitHalfWidth));
                outline.Add(new Vector2(s * slitEnd, -s * SlitHalfWidth));
                float from = s > 0 ? -meet : 180 - meet;
                Arc(outline, new Vector2(s * TabHole, 0), TabHoleRadius, from, from - (360 - 2 * meet), 20, false);
                outline.Add(new Vector2(s * slitEnd, s * SlitHalfWidth));
                outline.Add(new Vector2(s * TabHalfLength, s * SlitHalfWidth));
                Arc(outline, new Vector2(s * (TabHalfLength - corner), s * (halfWidth - corner)), corner, s > 0 ? 0 : 180, s > 0 ? 90 : 270, 4, true);
            }
            return outline;
        }

        /// <summary>Points round an arc from <paramref name="from"/> to <paramref name="to"/> degrees, with or without its two ends.</summary>
        static void Arc(List<Vector2> points, Vector2 centre, float radius, float from, float to, int steps, bool ends)
        {
            for (int i = ends ? 0 : 1; i <= (ends ? steps : steps - 1); i++)
            {
                float a = (from + (to - from) * i / steps) * Mathf.Deg2Rad;
                points.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        /// <summary>The double-arm horn: a hub on the spline, two tapering arms with their holes and the screw.</summary>
        public static Mesh HornMesh => (horn ??= MakeHorn()).Mesh;
        public static Material[] HornMaterials => (horn ??= MakeHorn()).Materials;
        static PartModel? horn;

        static PartModel MakeHorn()
        {
            var b = new Bench();
            var k = b.Kit;
            int white = b[PartLooks.Solid("sg90 horn", new Color(0.95f, 0.95f, 0.93f), 0.42f)];
            k.Cylinder(white, new Vector3(0, -2.4f, 0), new Vector3(0, 1.6f, 0), 3.5f, 32, 0.4f);
            var outline = new List<Vector2> { new(-16f, -1.7f), new(-2f, -2.8f), new(2f, -2.8f), new(16f, -1.7f), new(16f, 1.7f), new(2f, 2.8f), new(-2f, 2.8f), new(-16f, 1.7f) };
            // Round the arms' ends.
            outline.Insert(4, new Vector2(16.9f, 0));
            outline.Add(new Vector2(-16.9f, 0));
            k.Extrude(outline, 0, 1.6f, white, white, white, null, 25);
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 5; i++)
                    Pieces.Disc(b, b[PartLooks.Hole], new Vector3(side * (6.5f + i * 2.2f), 1.61f, 0), Vector3.up, 0.5f, 0.1f, 12);
            k.Cylinder(b[PartLooks.Nickel], new Vector3(0, 1.6f, 0), new Vector3(0, 2.3f, 0), 1.3f, 20, 0.3f);
            k.Box(b[PartLooks.Hole], new Vector3(0, 2.31f, 0), new Vector3(1.6f, 0.05f, 0.35f));
            return b.Finish("SG90 horn");
        }
    }

    /// <summary>
    /// An LED module: a 5 mm red LED on a small black board with its 220 Ω resistor and two pins, S and −
    /// (docs/09 §8). Its lens is the light "LED".
    /// </summary>
    static class LedModel
    {
        const float W = 20f, D = 14f, Bottom = 1.6f, Top = 3.2f;

        static Material BoardMaterial => PartLooks.Textured("led module board", () =>
        {
            var r = new Raster(W, D, 30);
            r.Fill(Ink.Solid(new Color32(18, 18, 20, 255), 0, 0.7f, 0));
            r.RoundRect(W / 2, D / 2, W - 1.6f, D - 1.6f, 1.0f, Ink.Solid(new Color32(30, 30, 33, 255), 0, 0.72f, 0.035f));
            var silk = Ink.Paint(new Color32(236, 238, 236, 255), 0, 0.32f);
            // The LED's outline with its flat side, the pin names, the part's name.
            r.Ring(7f, D / 2, 3.35f, 3.2f, silk);
            r.Text("S", 15.2f, D / 2 - 1.27f - 0.45f, 0.9f, silk, Align.Centre);
            r.Text("-", 15.2f, D / 2 + 1.27f - 0.45f, 0.9f, silk, Align.Centre);
            r.Text("LED", 3.0f, 1.2f, 1.0f, silk, Align.Centre);
            foreach (float z in new[] { -1.27f, 1.27f }) r.Circle(17.46f, D / 2 + z, 0.9f, Ink.Solid(new Color32(206, 206, 201, 255), 1, 0.55f, 0.06f));
            // Solder mask cures almost even: a strong grain on a black board reads as stone.
            r.Grain(0.015f, 0.015f, 0.3f, 41);
            return r;
        }, 1.1f);

        public static PartModel Make()
        {
            var b = new Bench();
            var k = b.Kit;
            var board = BoardMaterial;
            var outline = new List<Vector2> { new(-W / 2, -D / 2), new(W / 2, -D / 2), new(W / 2, D / 2), new(-W / 2, D / 2) };
            k.Extrude(outline, Bottom, Top, b[board], b[PartLooks.Fr4Edge], b[PartLooks.Solid("led board bottom", new Color(0.07f, 0.07f, 0.08f), 0.6f)],
                p => new Vector2((p.x + W / 2) / W, (p.y + D / 2) / D), 20);
            k.Push(new Vector3(0, Top, 0));
            foreach (float z in new[] { -1.27f, 1.27f }) k.Box(b[PartLooks.Tin], new Vector3(-3f, 1.25f, z * 0.8f), new Vector3(0.5f, 2.5f, 0.5f));
            Pieces.Smd(b, new Vector3(3f, 0, -3.5f), 2.0f, 1.25f, 0.5f, Pieces.ResistorBody);
            Pieces.MaleHeader(b, new Vector3(7.46f, 0, -1.27f), Vector3.forward, 2);
            k.Pop();
            b.Light("LED", new Vector3(-3f, Top + 2.5f + 4.2f, 0), new Vector3(5f, 8.4f, 5f), new Color(0.55f, 0.04f, 0.04f), new Color(1f, 0.08f, 0.04f), dome: true);
            return b.Finish("LED module");
        }
    }
}
