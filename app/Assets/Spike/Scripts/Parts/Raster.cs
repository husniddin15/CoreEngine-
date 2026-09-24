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

        /// <summary>
        /// A shape as the painter sees it: its signed distance (mm, negative inside), and the stretch of a row of
        /// pixels it can reach, so a long slanted trace does not measure every pixel of its box. Shapes are structs
        /// so each kind gets its own compiled loop, without a delegate call per pixel.
        /// </summary>
        interface IShape
        {
            float Distance(float x, float y);

            /// <summary>Narrows [x0, x1] (mm) on the row at <paramref name="y"/> to where the shape can reach; false if nowhere.</summary>
            bool Narrow(float y, ref float x0, ref float x1);
        }

        void Put(int index, in Ink ink, float coverage)
        {
            if (coverage <= 0) return;
            if (ink.Paints)
            {
                float a = coverage * ink.Colour.a / 255f;
                if (a >= 1)
                {
                    // Fully covered by an opaque ink, as most pixels of a big shape are: no blending.
                    colour[index] = ink.Colour;
                    metal[index] = ink.Metal;
                    smooth[index] = ink.Smooth;
                    if (ink.SetsHeight) height[index] = ink.Height;
                    return;
                }
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

        /// <summary>
        /// Paints every pixel whose centre is within the box (mm), row by row within the stretch the shape can
        /// reach, by the coverage its distance gives: shapes are anti-aliased over one pixel.
        /// </summary>
        void Shape<T>(float x0, float y0, float x1, float y1, in Ink ink, T shape) where T : struct, IShape
        {
            int py0 = Mathf.Max(0, Mathf.FloorToInt(y0 * PxPerMm) - 1), py1 = Mathf.Min(Height - 1, Mathf.CeilToInt(y1 * PxPerMm) + 1);
            float inv = 1f / PxPerMm;
            for (int py = py0; py <= py1; py++)
            {
                float y = (py + 0.5f) * inv, rowX0 = x0, rowX1 = x1;
                if (!shape.Narrow(y, ref rowX0, ref rowX1)) continue;
                int px0 = Mathf.Max(0, Mathf.FloorToInt(rowX0 * PxPerMm) - 1), px1 = Mathf.Min(Width - 1, Mathf.CeilToInt(rowX1 * PxPerMm) + 1);
                int row = py * Width;
                for (int px = px0; px <= px1; px++)
                {
                    float coverage = 0.5f - shape.Distance((px + 0.5f) * inv, y) * PxPerMm;
                    if (coverage > 0) Put(row + px, ink, coverage < 1 ? coverage : 1);
                }
            }
        }

        /// <summary>Covers the whole picture, every pixel at once.</summary>
        public void Fill(Ink ink)
        {
            for (int i = 0; i < colour.Length; i++) Put(i, ink, 1);
        }

        public void Rect(float x0, float y0, float x1, float y1, Ink ink)
        {
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = Mathf.Abs(x1 - x0) / 2, hy = Mathf.Abs(y1 - y0) / 2;
            Shape(cx - hx, cy - hy, cx + hx, cy + hy, ink, new BoxShape(cx, cy, hx, hy, 0, 0));
        }

        /// <summary>A rectangle with rounded corners, turned by <paramref name="degrees"/> about its middle.</summary>
        public void RoundRect(float cx, float cy, float width, float heightMm, float radius, Ink ink, float degrees = 0)
        {
            float hx = width / 2, hy = heightMm / 2;
            float cos = Mathf.Abs(Mathf.Cos(degrees * Mathf.Deg2Rad)), sin = Mathf.Abs(Mathf.Sin(degrees * Mathf.Deg2Rad));
            float ex = cos * hx + sin * hy, ey = sin * hx + cos * hy;
            Shape(cx - ex, cy - ey, cx + ex, cy + ey, ink, new BoxShape(cx, cy, hx, hy, radius, degrees));
        }

        public void Circle(float cx, float cy, float radius, Ink ink) =>
            Shape(cx - radius, cy - radius, cx + radius, cy + radius, ink, new CircleShape(cx, cy, radius, 0, 1 / PxPerMm));

        public void Ring(float cx, float cy, float outer, float inner, Ink ink) =>
            Shape(cx - outer, cy - outer, cx + outer, cy + outer, ink, new CircleShape(cx, cy, outer, inner, 1 / PxPerMm));

        /// <summary>A line with round ends, <paramref name="width"/> mm wide.</summary>
        public void Line(float x0, float y0, float x1, float y1, float width, Ink ink)
        {
            float h = width / 2;
            Shape(Mathf.Min(x0, x1) - h, Mathf.Min(y0, y1) - h, Mathf.Max(x0, x1) + h, Mathf.Max(y0, y1) + h, ink,
                new SegmentShape(x0, y0, x1, y1, h, 1 / PxPerMm));
        }

        public void Polyline(IList<Vector2> points, float width, Ink ink)
        {
            for (int i = 0; i + 1 < points.Count; i++) Line(points[i].x, points[i].y, points[i + 1].x, points[i + 1].y, width, ink);
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
            Shape(x0, y0, x1, y1, ink, new PolygonShape(points));
        }

        static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = dx * dx + dy * dy < 1e-12f ? 0 : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
            return Mathf.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>A box with rounded corners, turned about its middle.</summary>
        readonly struct BoxShape : IShape
        {
            readonly float cx, cy, hx, hy, radius, cos, sin;

            public BoxShape(float cx, float cy, float hx, float hy, float radius, float degrees)
            {
                this.cx = cx;
                this.cy = cy;
                this.hx = hx;
                this.hy = hy;
                this.radius = radius;
                cos = Mathf.Cos(-degrees * Mathf.Deg2Rad);
                sin = Mathf.Sin(-degrees * Mathf.Deg2Rad);
            }

            public float Distance(float x, float y)
            {
                float dx = x - cx, dy = y - cy;
                float qx = Mathf.Abs(dx * cos - dy * sin) - hx + radius, qy = Mathf.Abs(dx * sin + dy * cos) - hy + radius;
                float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0, inside = qx > qy ? qx : qy;
                return Mathf.Sqrt(ox * ox + oy * oy) + (inside < 0 ? inside : 0) - radius;
            }

            public bool Narrow(float y, ref float x0, ref float x1) => true;
        }

        /// <summary>A disc, or with an inner radius above zero a ring. Each row reaches across its chord only.</summary>
        readonly struct CircleShape : IShape
        {
            readonly float cx, cy, outer, inner, reach;

            /// <param name="margin">One pixel (mm), kept beyond the edge for the anti-aliasing.</param>
            public CircleShape(float cx, float cy, float outer, float inner, float margin)
            {
                this.cx = cx;
                this.cy = cy;
                this.outer = outer;
                this.inner = inner;
                reach = outer + margin;
            }

            public float Distance(float x, float y)
            {
                float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                return inner > 0 && inner - r > r - outer ? inner - r : r - outer;
            }

            public bool Narrow(float y, ref float x0, ref float x1)
            {
                float dy = y - cy, square = reach * reach - dy * dy;
                if (square <= 0) return false;
                float half = Mathf.Sqrt(square);
                if (cx - half > x0) x0 = cx - half;
                if (cx + half < x1) x1 = cx + half;
                return x0 <= x1;
            }
        }

        /// <summary>
        /// A line with round ends. A row meets it only near where the line crosses the row: within the half width
        /// (and a pixel) divided by the sine of its slant, which keeps a long diagonal trace from filling its box.
        /// </summary>
        readonly struct SegmentShape : IShape
        {
            readonly float ax, ay, dx, dy, lengthSquared, half, slope, spread;

            /// <param name="margin">One pixel (mm), kept beyond the edge for the anti-aliasing.</param>
            public SegmentShape(float x0, float y0, float x1, float y1, float half, float margin)
            {
                ax = x0;
                ay = y0;
                dx = x1 - x0;
                dy = y1 - y0;
                lengthSquared = dx * dx + dy * dy;
                this.half = half;
                // Nearly level lines keep their box, which is tight for them anyway.
                float rise = lengthSquared > 1e-12f ? Mathf.Abs(dy) / Mathf.Sqrt(lengthSquared) : 0;
                slope = rise > 0.05f ? dx / dy : 0;
                spread = rise > 0.05f ? (half + margin) / rise : -1;
            }

            public float Distance(float x, float y)
            {
                float t = lengthSquared < 1e-12f ? 0 : ((x - ax) * dx + (y - ay) * dy) / lengthSquared;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                float ex = x - (ax + t * dx), ey = y - (ay + t * dy);
                return Mathf.Sqrt(ex * ex + ey * ey) - half;
            }

            public bool Narrow(float y, ref float x0, ref float x1)
            {
                if (spread < 0) return true;
                float centre = ax + (y - ay) * slope;
                if (centre - spread > x0) x0 = centre - spread;
                if (centre + spread < x1) x1 = centre + spread;
                return x0 <= x1;
            }
        }

        /// <summary>A filled polygon: the distance to its nearest edge, negative inside (even-odd).</summary>
        readonly struct PolygonShape : IShape
        {
            readonly IList<Vector2> points;

            public PolygonShape(IList<Vector2> points) => this.points = points;

            public float Distance(float x, float y)
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
            }

            public bool Narrow(float y, ref float x0, ref float x1) => true;
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
            // Where each column falls between the grid's points, worked out once for every row.
            var columns = new int[Width];
            var blends = new float[Width];
            for (int px = 0; px < Width; px++)
            {
                float fx = (float)px / cells;
                columns[px] = (int)fx;
                blends[px] = Smooth01(fx - columns[px]);
            }
            for (int py = 0; py < Height; py++)
            {
                float fy = (float)py / cells;
                int gy = (int)fy, below = gy * gw, above = below + gw, row = py * Width;
                float ty = Smooth01(fy - gy);
                for (int px = 0; px < Width; px++)
                {
                    int gx = columns[px];
                    float tx = blends[px];
                    float low = grid[below + gx] + (grid[below + gx + 1] - grid[below + gx]) * tx;
                    float high = grid[above + gx] + (grid[above + gx + 1] - grid[above + gx]) * tx;
                    float n = low + (high - low) * ty;
                    int index = row + px;
                    float f = 1 + n * colourAmount;
                    var col = colour[index];
                    colour[index] = new Color32(Byte(col.r * f), Byte(col.g * f), Byte(col.b * f), 255);
                    float s = smooth[index] + n * smoothAmount;
                    smooth[index] = s < 0 ? 0 : s > 1 ? 1 : s;
                }
            }
        }

        static float Smooth01(float t) => t * t * (3 - 2 * t);

        static byte Byte(float value) => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)value;

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
            // Soften by a 3 × 3 box, as a pass across and a pass down; the edge pixels repeat.
            var across = new float[Width * Height];
            for (int py = 0; py < Height; py++)
            {
                int row = py * Width;
                for (int px = 0; px < Width; px++)
                {
                    int left = px > 0 ? px - 1 : px, right = px < Width - 1 ? px + 1 : px;
                    across[row + px] = (height[row + left] + height[row + px] + height[row + right]) * (1f / 3);
                }
            }
            var soft = new float[Width * Height];
            for (int py = 0; py < Height; py++)
            {
                int row = py * Width, up = (py < Height - 1 ? py + 1 : py) * Width, down = (py > 0 ? py - 1 : py) * Width;
                for (int px = 0; px < Width; px++) soft[row + px] = (across[down + px] + across[row + px] + across[up + px]) * (1f / 3);
            }
            var pixels = new Color32[Width * Height];
            float k = strength * PxPerMm / 2;
            for (int py = 0; py < Height; py++)
            {
                int row = py * Width, up = (py < Height - 1 ? py + 1 : py) * Width, down = (py > 0 ? py - 1 : py) * Width;
                for (int px = 0; px < Width; px++)
                {
                    int right = px < Width - 1 ? px + 1 : px, left = px > 0 ? px - 1 : px;
                    float nx = -(soft[row + right] - soft[row + left]) * k, ny = -(soft[up + px] - soft[down + px]) * k;
                    float scale = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1);
                    pixels[row + px] = new Color32((byte)((nx * scale * 0.5f + 0.5f) * 255), (byte)((ny * scale * 0.5f + 0.5f) * 255), (byte)((scale * 0.5f + 0.5f) * 255), 255);
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
