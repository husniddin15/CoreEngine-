using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.UI
{
    public enum Icon
    {
        Play, Build, Wire, Code, Body, Customize, Repair, Garage, Notebook, Shop, Workshop, Gear,
        Chip, Weight, Parts, Battery, Warning, Check, Plus, Back,
    }

    /// <summary>
    /// A line icon drawn with the vector painter, so it stays sharp at any size and needs no image files. It takes
    /// the element's text colour, so hover and active styles recolour it. Drawn in a 24 × 24 grid, 1.8 px lines.
    /// </summary>
    public sealed class IconView : VisualElement
    {
        Icon icon;

        public IconView(Icon icon)
        {
            this.icon = icon;
            pickingMode = PickingMode.Ignore;
            AddToClassList("icon");
            generateVisualContent += Draw;
        }

        public Icon Icon
        {
            get => icon;
            set
            {
                icon = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 1) return;
            float scale = size / 24f;
            var origin = new Vector2(rect.x + (rect.width - size) / 2, rect.y + (rect.height - size) / 2);
            Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;
            var p = context.painter2D;
            var colour = resolvedStyle.color;
            p.strokeColor = colour;
            p.fillColor = colour;
            p.lineWidth = Mathf.Max(1.2f, 1.8f * scale);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            void Line(float x1, float y1, float x2, float y2)
            {
                p.BeginPath();
                p.MoveTo(P(x1, y1));
                p.LineTo(P(x2, y2));
                p.Stroke();
            }
            void Poly(bool closed, bool fill, params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                if (closed) p.ClosePath();
                if (fill) p.Fill();
                else p.Stroke();
            }
            void Circle(float x, float y, float r, bool fill = false)
            {
                p.BeginPath();
                p.Arc(P(x, y), r * scale, Angle.Degrees(0), Angle.Degrees(360));
                p.ClosePath();
                if (fill) p.Fill();
                else p.Stroke();
            }
            void Arc(float x, float y, float r, float from, float to)
            {
                p.BeginPath();
                p.Arc(P(x, y), r * scale, Angle.Degrees(from), Angle.Degrees(to));
                p.Stroke();
            }

            switch (icon)
            {
                case Icon.Play:
                    Poly(true, true, 7, 4.5f, 19.5f, 12, 7, 19.5f);
                    break;
                case Icon.Build: // wrench
                    Arc(16.5f, 7.5f, 4.5f, 150, 480);
                    Line(13.3f, 10.7f, 4.5f, 19.5f);
                    break;
                case Icon.Wire: // plug with a cable
                    Poly(true, false, 8, 7, 16, 7, 16, 12, 12, 15, 8, 12);
                    Line(10, 3, 10, 7);
                    Line(14, 3, 14, 7);
                    p.BeginPath();
                    p.MoveTo(P(12, 15));
                    p.BezierCurveTo(P(12, 20), P(6, 18), P(5, 21.5f));
                    p.Stroke();
                    break;
                case Icon.Code:
                    Poly(false, false, 8, 6, 2.5f, 12, 8, 18);
                    Poly(false, false, 16, 6, 21.5f, 12, 16, 18);
                    Line(13.5f, 4.5f, 10.5f, 19.5f);
                    break;
                case Icon.Body: // a box seen from above at an angle
                    Poly(true, false, 12, 3, 20.5f, 7.5f, 20.5f, 16.5f, 12, 21, 3.5f, 16.5f, 3.5f, 7.5f);
                    Poly(false, false, 3.5f, 7.5f, 12, 12, 20.5f, 7.5f);
                    Line(12, 12, 12, 21);
                    break;
                case Icon.Customize: // palette
                    p.BeginPath();
                    p.MoveTo(P(12, 3));
                    p.BezierCurveTo(P(5, 3), P(2.5f, 9), P(3.5f, 13.5f));
                    p.BezierCurveTo(P(4.5f, 19), P(10, 21.5f), P(13, 20.5f));
                    p.BezierCurveTo(P(15.5f, 19.5f), P(13, 16.5f), P(15.5f, 15.5f));
                    p.BezierCurveTo(P(18, 14.5f), P(21.5f, 16), P(21, 11));
                    p.BezierCurveTo(P(20.5f, 6), P(16.5f, 3), P(12, 3));
                    p.Stroke();
                    Circle(8, 10, 1.3f, true);
                    Circle(12, 7, 1.3f, true);
                    Circle(16.5f, 9, 1.3f, true);
                    break;
                case Icon.Repair: // shield with a tick
                    Poly(true, false, 12, 2.5f, 19.5f, 5.5f, 19, 12, 12, 21.5f, 5, 12, 4.5f, 5.5f);
                    Poly(false, false, 8.5f, 12, 11, 14.5f, 15.5f, 9.5f);
                    break;
                case Icon.Garage: // house with a door
                    Poly(false, false, 3, 11, 12, 3.5f, 21, 11);
                    Poly(false, false, 5.5f, 9, 5.5f, 20.5f, 18.5f, 20.5f, 18.5f, 9);
                    Poly(false, false, 10, 20.5f, 10, 14.5f, 14, 14.5f, 14, 20.5f);
                    break;
                case Icon.Notebook:
                    Poly(true, false, 6, 3, 18.5f, 3, 18.5f, 21, 6, 21);
                    Line(9.5f, 3, 9.5f, 21);
                    Line(12, 8, 16, 8);
                    Line(12, 11.5f, 16, 11.5f);
                    break;
                case Icon.Shop: // bag
                    Poly(true, false, 4.5f, 8, 19.5f, 8, 18.5f, 21, 5.5f, 21);
                    Arc(12, 8, 3.5f, 180, 360);
                    break;
                case Icon.Workshop: // globe
                    Circle(12, 12, 9);
                    p.BeginPath();
                    p.MoveTo(P(12, 3));
                    p.BezierCurveTo(P(7, 7), P(7, 17), P(12, 21));
                    p.MoveTo(P(12, 3));
                    p.BezierCurveTo(P(17, 7), P(17, 17), P(12, 21));
                    p.Stroke();
                    Line(3, 12, 21, 12);
                    break;
                case Icon.Gear:
                    Circle(12, 12, 3);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4;
                        Line(12 + 6f * Mathf.Cos(a), 12 + 6f * Mathf.Sin(a), 12 + 8.8f * Mathf.Cos(a), 12 + 8.8f * Mathf.Sin(a));
                    }
                    Circle(12, 12, 6.2f);
                    break;
                case Icon.Chip:
                    Poly(true, false, 7, 7, 17, 7, 17, 17, 7, 17);
                    for (int i = 0; i < 3; i++)
                    {
                        float c = 9.5f + i * 2.5f;
                        Line(c, 3.5f, c, 7);
                        Line(c, 17, c, 20.5f);
                        Line(3.5f, c, 7, c);
                        Line(17, c, 20.5f, c);
                    }
                    break;
                case Icon.Weight:
                    Poly(true, false, 5, 20.5f, 19, 20.5f, 17, 9, 7, 9);
                    Circle(12, 6, 2.5f);
                    break;
                case Icon.Parts:
                    Poly(true, false, 4, 4, 10.5f, 4, 10.5f, 10.5f, 4, 10.5f);
                    Poly(true, false, 13.5f, 4, 20, 4, 20, 10.5f, 13.5f, 10.5f);
                    Poly(true, false, 4, 13.5f, 10.5f, 13.5f, 10.5f, 20, 4, 20);
                    Poly(true, false, 13.5f, 13.5f, 20, 13.5f, 20, 20, 13.5f, 20);
                    break;
                case Icon.Battery:
                    Poly(true, false, 3, 7.5f, 19, 7.5f, 19, 16.5f, 3, 16.5f);
                    Line(21.5f, 10.5f, 21.5f, 13.5f);
                    Poly(true, true, 5.5f, 10, 13, 10, 13, 14, 5.5f, 14);
                    break;
                case Icon.Warning:
                    Poly(true, false, 12, 3.5f, 21.5f, 20, 2.5f, 20);
                    Line(12, 9.5f, 12, 14);
                    Circle(12, 17, 0.6f, true);
                    break;
                case Icon.Check:
                    Poly(false, false, 4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 6.5f);
                    break;
                case Icon.Plus:
                    Line(12, 4.5f, 12, 19.5f);
                    Line(4.5f, 12, 19.5f, 12);
                    break;
                case Icon.Back:
                    Poly(false, false, 15, 5, 8, 12, 15, 19);
                    break;
            }
        }
    }
}
