using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>What a shape paints: a colour (sRGB), how metallic and smooth it is, and how high it stands (mm).</summary>
    public readonly struct Ink
    {
        public readonly Color32 Colour;
        public readonly float Metal, Smooth, Height;
        public readonly bool Paints, SetsHeight, AddsHeight;

        Ink(Color32 colour, float metal, float smooth, float height, bool paints, bool setsHeight, bool addsHeight)
        {
            Colour = colour;
            Metal = metal;
            Smooth = smooth;
            Height = height;
            Paints = paints;
            SetsHeight = setsHeight;
            AddsHeight = addsHeight;
        }

        /// <summary>Colour, metal and smoothness; the height stays as it is.</summary>
        public static Ink Paint(Color32 colour, float metal, float smooth) => new Ink(colour, metal, smooth, 0, true, false, false);

        /// <summary>Colour, metal and smoothness, and a height set to <paramref name="height"/>.</summary>
        public static Ink Solid(Color32 colour, float metal, float smooth, float height) => new Ink(colour, metal, smooth, height, true, true, false);

        /// <summary>Only raises (or with a negative value lowers) the surface.</summary>
        public static Ink Raise(float height) => new Ink(default, 0, 0, height, false, false, true);

        public static Color32 Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? (Color32)c : new Color32(255, 0, 255, 255);
    }

    public enum Align { Left, Centre, Right }

    /// <summary>
    /// A CPU painter for part textures (a board's copper, mask, pads and silkscreen; a chip's marking), in
    /// millimetres with y up. Shapes are anti-aliased from their distance to the pixel. It keeps three layers,
    /// turned into textures for URP Lit: colour (sRGB), metal and smoothness (the metallic map), and height,
    /// from which the normal map is made. Text uses a stroke font like a PCB's silkscreen.
    /// </summary>
    public sealed class Raster
    {
        public readonly int Width, Height;
        public readonly float PxPerMm;
        public readonly float WidthMm, HeightMm;
        readonly Color32[] colour;
        readonly float[] metal, smooth, height;

        public Raster(float widthMm, float heightMm, float pxPerMm)
        {
            WidthMm = widthMm;
            HeightMm = heightMm;
            PxPerMm = pxPerMm;
            Width = Mathf.Max(4, Mathf.CeilToInt(widthMm * pxPerMm / 4) * 4);
            Height = Mathf.Max(4, Mathf.CeilToInt(heightMm * pxPerMm / 4) * 4);
            colour = new Color32[Width * Height];
            metal = new float[Width * Height];
            smooth = new float[Width * Height];
            height = new float[Width * Height];
        }

        /// <summary>Where a point (mm) is on the texture, from 0 to 1.</summary>
        public Vector2 Uv(float x, float y) => new Vector2(x * PxPerMm / Width, y * PxPerMm / Height);

        public UnityEngine.Rect UvRect(float x0, float y0, float x1, float y1)
        {
            var a = Uv(x0, y0);
            var b = Uv(x1, y1);
            return UnityEngine.Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        // ------------------------------------------------------------------ painting

        void Put(int index, Ink ink, float coverage)
        {
            if (coverage <= 0) return;
            if (ink.Paints)
            {
                float a = coverage * ink.Colour.a / 255f;
                var c = colour[index];
                colour[index] = new Color32(
                    (byte)(c.r + (ink.Colour.r - c.r) * a),
                    (byte)(c.g + (ink.Colour.g - c.g) * a),
                    (byte)(c.b + (ink.Colour.b - c.b) * a),
                    255);
                metal[index] += (ink.Metal - metal[index]) * a;
                smooth[index] += (ink.Smooth - smooth[index]) * a;
            }
            if (ink.SetsHeight) height[index] += (ink.Height - height[index]) * coverage;
            else if (ink.AddsHeight) height[index] += ink.Height * coverage;
        }

        /// <summary>Paints every pixel whose centre is within the box (mm) by the coverage the distance function gives.</summary>
        void Shape(float x0, float y0, float x1, float y1, Ink ink, System.Func<float, float, float> distance)
        {
            int px0 = Mathf.Max(0, Mathf.FloorToInt(x0 * PxPerMm) - 1), px1 = Mathf.Min(Width - 1, Mathf.CeilToInt(x1 * PxPerMm) + 1);
            int py0 = Mathf.Max(0, Mathf.FloorToInt(y0 * PxPerMm) - 1), py1 = Mathf.Min(Height - 1, Mathf.CeilToInt(y1 * PxPerMm) + 1);
            float inv = 1f / PxPerMm;
            for (int py = py0; py <= py1; py++)
            {
                float y = (py + 0.5f) * inv;
                int row = py * Width;
                for (int px = px0; px <= px1; px++)
                {
                    float d = distance((px + 0.5f) * inv, y);
                    float coverage = Mathf.Clamp01(0.5f - d * PxPerMm);
                    if (coverage > 0) Put(row + px, ink, coverage);
                }
            }
        }

        public void Fill(Ink ink) => Rect(-1, -1, WidthMm + 1, HeightMm + 1, ink);

        public void Rect(float x0, float y0, float x1, float y1, Ink ink)
        {
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = Mathf.Abs(x1 - x0) / 2, hy = Mathf.Abs(y1 - y0) / 2;
            Shape(cx - hx, cy - hy, cx + hx, cy + hy, ink, (x, y) => BoxDistance(x - cx, y - cy, hx, hy, 0));
        }

        /// <summary>A rectangle with rounded corners, turned by <paramref name="degrees"/> about its middle.</summary>
        public void RoundRect(float cx, float cy, float width, float heightMm, float radius, Ink ink, float degrees = 0)
        {
            float hx = width / 2, hy = heightMm / 2, reach = Mathf.Sqrt(hx * hx + hy * hy);
            float cos = Mathf.Cos(-degrees * Mathf.Deg2Rad), sin = Mathf.Sin(-degrees * Mathf.Deg2Rad);
            Shape(cx - reach, cy - reach, cx + reach, cy + reach, ink, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                return BoxDistance(dx * cos - dy * sin, dx * sin + dy * cos, hx, hy, radius);
            });
        }

        static float BoxDistance(float x, float y, float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(x) - hx + radius, qy = Mathf.Abs(y) - hy + radius;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
        }

        public void Circle(float cx, float cy, float radius, Ink ink) =>
            Shape(cx - radius, cy - radius, cx + radius, cy + radius, ink, (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius);

        public void Ring(float cx, float cy, float outer, float inner, Ink ink) =>
            Shape(cx - outer, cy - outer, cx + outer, cy + outer, ink, (x, y) =>
            {
                float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                return Mathf.Max(r - outer, inner - r);
            });

        /// <summary>A line with round ends, <paramref name="width"/> mm wide.</summary>
        public void Line(float x0, float y0, float x1, float y1, float width, Ink ink)
        {
            float h = width / 2;
            Shape(Mathf.Min(x0, x1) - h, Mathf.Min(y0, y1) - h, Mathf.Max(x0, x1) + h, Mathf.Max(y0, y1) + h, ink,
                (x, y) => SegmentDistance(x, y, x0, y0, x1, y1) - h);
        }

        public void Polyline(IList<Vector2> points, float width, Ink ink)
        {
            for (int i = 0; i + 1 < points.Count; i++) Line(points[i].x, points[i].y, points[i + 1].x, points[i + 1].y, width, ink);
        }

        static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = dx * dx + dy * dy < 1e-12f ? 0 : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
            return Mathf.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>A filled polygon (any winding, no holes).</summary>
        public void Polygon(IList<Vector2> points, Ink ink)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in points)
            {
                x0 = Mathf.Min(x0, p.x);
                y0 = Mathf.Min(y0, p.y);
                x1 = Mathf.Max(x1, p.x);
                y1 = Mathf.Max(y1, p.y);
            }
            Shape(x0, y0, x1, y1, ink, (x, y) =>
            {
                float nearest = float.MaxValue;
                bool inside = false;
                for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                {
                    var a = points[i];
                    var b = points[j];
                    nearest = Mathf.Min(nearest, SegmentDistance(x, y, a.x, a.y, b.x, b.y));
                    if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                }
                return inside ? -nearest : nearest;
            });
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// Text in the stroke font: capital height <paramref name="size"/> mm, lines <paramref name="stroke"/> of the
        /// height thick, placed by its baseline at (x, y) and turned about that point by <paramref name="degrees"/>.
        /// </summary>
        public void Text(string text, float x, float y, float size, Ink ink, Align align = Align.Left, float degrees = 0, float stroke = 0.15f, float spacing = 1f)
        {
            float unit = size / 6f;
            float width = StrokeFont.Measure(text, spacing) * unit;
            float start = align == Align.Left ? 0 : align == Align.Centre ? -width / 2 : -width;
            float cos = Mathf.Cos(degrees * Mathf.Deg2Rad), sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
            float pen = start;
            float lineWidth = size * stroke;
            foreach (char raw in text)
            {
                var glyph = StrokeFont.Get(raw);
                foreach (var points in glyph.Strokes)
                {
                    // A stroke of one point is a dot; the others are lines from point to point.
                    for (int i = 0; i == 0 || i + 3 < points.Length; i += 2)
                    {
                        float ax = pen + points[i] * unit, ay = points[i + 1] * unit;
                        float bx = points.Length == 2 ? ax : pen + points[i + 2] * unit;
                        float by = points.Length == 2 ? ay : points[i + 3] * unit;
                        Line(x + ax * cos - ay * sin, y + ax * sin + ay * cos, x + bx * cos - by * sin, y + bx * sin + by * cos, lineWidth, ink);
                    }
                }
                pen += (glyph.Width + 1.6f * spacing) * unit;
            }
        }

        public static float TextWidth(string text, float size, float spacing = 1f) => StrokeFont.Measure(text, spacing) * size / 6f;

        // ------------------------------------------------------------------ texture

        /// <summary>Fine unevenness of colour and gloss (a mask's cure, a chip's moulding), deterministic from the seed.</summary>
        public void Grain(float colourAmount, float smoothAmount, float cellMm, int seed)
        {
            var random = new System.Random(seed);
            int cells = Mathf.Max(1, Mathf.RoundToInt(cellMm * PxPerMm));
            int gw = Width / cells + 2, gh = Height / cells + 2;
            var grid = new float[gw * gh];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)random.NextDouble() * 2 - 1;
            for (int py = 0; py < Height; py++)
            {
                float fy = (float)py / cells;
                int gy = (int)fy;
                float ty = Smooth01(fy - gy);
                for (int px = 0; px < Width; px++)
                {
                    float fx = (float)px / cells;
                    int gx = (int)fx;
                    float tx = Smooth01(fx - gx);
                    float a = grid[gy * gw + gx], b = grid[gy * gw + gx + 1], c = grid[(gy + 1) * gw + gx], d = grid[(gy + 1) * gw + gx + 1];
                    float n = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    int index = py * Width + px;
                    float f = 1 + n * colourAmount;
                    var col = colour[index];
                    colour[index] = new Color32((byte)Mathf.Clamp(col.r * f, 0, 255), (byte)Mathf.Clamp(col.g * f, 0, 255), (byte)Mathf.Clamp(col.b * f, 0, 255), 255);
                    smooth[index] = Mathf.Clamp01(smooth[index] + n * smoothAmount);
                }
            }
        }

        static float Smooth01(float t) => t * t * (3 - 2 * t);

        public Texture2D ColourTexture(string name)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            texture.SetPixels32(colour);
            texture.Apply(true, false);
            return texture;
        }

        /// <summary>URP Lit's metallic map: metal in red, smoothness in alpha.</summary>
        public Texture2D MetalTexture(string name)
        {
            var pixels = new Color32[Width * Height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)(Mathf.Clamp01(metal[i]) * 255), 0, 0, (byte)(Mathf.Clamp01(smooth[i]) * 255));
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        /// <summary>
        /// The normal map from the height layer, softened by one pixel: a slope of 1 mm per mm leans the normal 45°
        /// at a <paramref name="strength"/> of 1. Tangent space with y up the texture, as URP unpacks it.
        /// </summary>
        public Texture2D NormalTexture(string name, float strength = 1)
        {
            var soft = new float[Width * Height];
            for (int py = 0; py < Height; py++)
            {
                for (int px = 0; px < Width; px++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int y = py + dy;
                        if (y < 0 || y >= Height) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = px + dx;
                            if (x < 0 || x >= Width) continue;
                            sum += height[y * Width + x];
                            n++;
                        }
                    }
                    soft[py * Width + px] = sum / n;
                }
            }
            var pixels = new Color32[Width * Height];
            float k = strength * PxPerMm / 2;
            for (int py = 0; py < Height; py++)
            {
                int up = Mathf.Min(py + 1, Height - 1), down = Mathf.Max(py - 1, 0);
                for (int px = 0; px < Width; px++)
                {
                    int right = Mathf.Min(px + 1, Width - 1), left = Mathf.Max(px - 1, 0);
                    float sx = (soft[py * Width + right] - soft[py * Width + left]) * k;
                    float sy = (soft[up * Width + px] - soft[down * Width + px]) * k;
                    var normal = new Vector3(-sx, -sy, 1).normalized;
                    pixels[py * Width + px] = new Color32((byte)((normal.x * 0.5f + 0.5f) * 255), (byte)((normal.y * 0.5f + 0.5f) * 255), (byte)((normal.z * 0.5f + 0.5f) * 255), 255);
                }
            }
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }
    }

    /// <summary>
    /// A single-stroke font like the vector fonts PCB tools print silkscreen with: capitals, digits and the few
    /// signs boards carry, drawn on a grid four units wide and six high from the baseline. Small letters print as
    /// capitals, as they mostly do on boards.
    /// </summary>
    public static class StrokeFont
    {
        public readonly struct Glyph
        {
            public readonly float Width;
            public readonly float[][] Strokes;

            public Glyph(float width, params float[][] strokes)
            {
                Width = width;
                Strokes = strokes;
            }
        }

        static readonly Dictionary<char, Glyph> glyphs = new Dictionary<char, Glyph>
        {
            ['A'] = G(4, S(0, 0, 2, 6, 4, 0), S(0.7f, 2.1f, 3.3f, 2.1f)),
            ['B'] = G(4, S(0, 0, 0, 6, 3, 6, 4, 5, 4, 4, 3, 3, 0, 3), S(3, 3, 4, 2, 4, 1, 3, 0, 0, 0)),
            ['C'] = G(4, S(4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0, 3, 0, 4, 1)),
            ['D'] = G(4, S(0, 0, 0, 6, 2.5f, 6, 4, 4.5f, 4, 1.5f, 2.5f, 0, 0, 0)),
            ['E'] = G(4, S(4, 6, 0, 6, 0, 0, 4, 0), S(0, 3, 3, 3)),
            ['F'] = G(4, S(4, 6, 0, 6, 0, 0), S(0, 3, 3, 3)),
            ['G'] = G(4, S(4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0, 3, 0, 4, 1, 4, 3, 2.3f, 3)),
            ['H'] = G(4, S(0, 0, 0, 6), S(4, 0, 4, 6), S(0, 3, 4, 3)),
            ['I'] = G(2, S(0, 6, 2, 6), S(1, 6, 1, 0), S(0, 0, 2, 0)),
            ['J'] = G(4, S(4, 6, 4, 1, 3, 0, 1, 0, 0, 1)),
            ['K'] = G(4, S(0, 0, 0, 6), S(4, 6, 0, 2), S(1.4f, 3.4f, 4, 0)),
            ['L'] = G(4, S(0, 6, 0, 0, 4, 0)),
            ['M'] = G(4.4f, S(0, 0, 0, 6, 2.2f, 2.6f, 4.4f, 6, 4.4f, 0)),
            ['N'] = G(4, S(0, 0, 0, 6, 4, 0, 4, 6)),
            ['O'] = G(4, S(1, 0, 0, 1, 0, 5, 1, 6, 3, 6, 4, 5, 4, 1, 3, 0, 1, 0)),
            ['P'] = G(4, S(0, 0, 0, 6, 3, 6, 4, 5, 4, 4, 3, 3, 0, 3)),
            ['Q'] = G(4, S(1, 0, 0, 1, 0, 5, 1, 6, 3, 6, 4, 5, 4, 1, 3, 0, 1, 0), S(2.4f, 1.6f, 4, 0)),
            ['R'] = G(4, S(0, 0, 0, 6, 3, 6, 4, 5, 4, 4, 3, 3, 0, 3), S(2, 3, 4, 0)),
            ['S'] = G(4, S(4, 5, 3, 6, 1, 6, 0, 5, 0, 4, 1, 3, 3, 3, 4, 2, 4, 1, 3, 0, 1, 0, 0, 1)),
            ['T'] = G(4, S(0, 6, 4, 6), S(2, 6, 2, 0)),
            ['U'] = G(4, S(0, 6, 0, 1, 1, 0, 3, 0, 4, 1, 4, 6)),
            ['V'] = G(4, S(0, 6, 2, 0, 4, 6)),
            ['W'] = G(4.6f, S(0, 6, 1.1f, 0, 2.3f, 4, 3.5f, 0, 4.6f, 6)),
            ['X'] = G(4, S(0, 0, 4, 6), S(0, 6, 4, 0)),
            ['Y'] = G(4, S(0, 6, 2, 3, 4, 6), S(2, 3, 2, 0)),
            ['Z'] = G(4, S(0, 6, 4, 6, 0, 0, 4, 0)),
            ['0'] = G(4, S(1, 0, 0, 1, 0, 5, 1, 6, 3, 6, 4, 5, 4, 1, 3, 0, 1, 0)),
            ['1'] = G(3, S(0.5f, 5, 1.8f, 6, 1.8f, 0), S(0.5f, 0, 3, 0)),
            ['2'] = G(4, S(0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 0, 0, 4, 0)),
            ['3'] = G(4, S(0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 1.5f, 3), S(3, 3, 4, 2, 4, 1, 3, 0, 1, 0, 0, 1)),
            ['4'] = G(4, S(3, 0, 3, 6, 0, 2, 4, 2)),
            ['5'] = G(4, S(4, 6, 0, 6, 0, 3.5f, 3, 3.5f, 4, 2.5f, 4, 1, 3, 0, 1, 0, 0, 1)),
            ['6'] = G(4, S(3.5f, 6, 2, 6, 0, 4, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2.5f, 3, 3.5f, 1, 3.5f, 0, 2.5f)),
            ['7'] = G(4, S(0, 6, 4, 6, 1.4f, 0)),
            ['8'] = G(4, S(1, 3, 0, 4, 0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 1, 3, 0, 2, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 3, 3)),
            ['9'] = G(4, S(0.5f, 0, 2, 0, 4, 2, 4, 5, 3, 6, 1, 6, 0, 5, 0, 3.5f, 1, 2.5f, 3, 2.5f, 4, 3.5f)),
            [' '] = G(2.2f),
            ['-'] = G(3, S(0.3f, 3, 2.7f, 3)),
            ['+'] = G(4, S(0, 3, 4, 3), S(2, 1, 2, 5)),
            ['.'] = G(0.6f, S(0.3f, 0.1f)),
            [','] = G(0.8f, S(0.5f, 0.3f, 0.1f, -1)),
            [':'] = G(0.6f, S(0.3f, 0.3f), S(0.3f, 3.8f)),
            ['~'] = G(4, S(0, 3.2f, 0.7f, 4, 1.4f, 4, 2.6f, 2.8f, 3.3f, 2.8f, 4, 3.6f)),
            ['/'] = G(3, S(0, 0, 3, 6)),
            ['('] = G(1.6f, S(1.6f, 6.5f, 0.3f, 4.5f, 0.3f, 1.5f, 1.6f, -0.5f)),
            [')'] = G(1.6f, S(0, 6.5f, 1.3f, 4.5f, 1.3f, 1.5f, 0, -0.5f)),
            ['<'] = G(3.5f, S(3.5f, 5, 0, 3, 3.5f, 1)),
            ['>'] = G(3.5f, S(0, 5, 3.5f, 3, 0, 1)),
            ['='] = G(4, S(0, 2, 4, 2), S(0, 4, 4, 4)),
            ['_'] = G(4, S(0, -0.6f, 4, -0.6f)),
            ['#'] = G(4, S(1, 0, 1.6f, 6), S(2.6f, 0, 3.2f, 6), S(0, 2, 4, 2), S(0.2f, 4, 4.2f, 4)),
            ['!'] = G(0.6f, S(0.3f, 6, 0.3f, 1.8f), S(0.3f, 0.1f)),
            ['|'] = G(0.6f, S(0.3f, -1, 0.3f, 7)),
            ['*'] = G(4, S(2, 1, 2, 5), S(0.3f, 2, 3.7f, 4), S(0.3f, 4, 3.7f, 2)),
            ['°'] = G(1.8f, S(0.9f, 6, 0.2f, 5.3f, 0.9f, 4.6f, 1.6f, 5.3f, 0.9f, 6)),
            ['µ'] = G(4, S(0, -1.6f, 0, 4.2f), S(0, 1.3f, 1, 0.2f, 2.6f, 0.2f, 4, 1.3f), S(4, 4.2f, 4, 0)),
            ['Ω'] = G(4.4f, S(0, 0, 1.3f, 0, 1.3f, 0.9f, 0, 2.6f, 0, 4.6f, 1.2f, 6, 3.2f, 6, 4.4f, 4.6f, 4.4f, 2.6f, 3.1f, 0.9f, 3.1f, 0, 4.4f, 0)),
            ['×'] = G(3.4f, S(0, 1.3f, 3.4f, 4.7f), S(0, 4.7f, 3.4f, 1.3f)),
            ['−'] = G(3, S(0.3f, 3, 2.7f, 3)),
            ['▸'] = G(3, S(0, 1, 0, 5, 3, 3, 0, 1)),
            ['◂'] = G(3, S(3, 1, 3, 5, 0, 3, 3, 1)),
        };

        static Glyph G(float width, params float[][] strokes) => new Glyph(width, strokes);

        static float[] S(params float[] points) => points;

        public static Glyph Get(char c)
        {
            if (glyphs.TryGetValue(c, out var glyph)) return glyph;
            if (glyphs.TryGetValue(char.ToUpperInvariant(c), out glyph)) return glyph;
            return glyphs[' '];
        }

        /// <summary>The width of a text in grid units (a capital is six units tall).</summary>
        public static float Measure(string text, float spacing = 1f)
        {
            float width = 0;
            foreach (char c in text) width += Get(c).Width + 1.6f * spacing;
            return Mathf.Max(0, width - 1.6f * spacing);
        }
    }
}
