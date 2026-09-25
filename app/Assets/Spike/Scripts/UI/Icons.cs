using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.UI
{
    public enum Icon
    {
        Play, Build, Wire, Code, Body, Customize, Repair, Garage, Notebook, Shop, Workshop, Gear,
        Chip, Weight, Parts, Battery, Warning, Check, Plus, Back,
        ShapeBox, ShapeRounded, ShapeCylinder, ShapeCone, ShapeSphere, ShapeWedge, ShapeTube, Draw, Import, Export,
        Move, Rotate, Size, Undo, Redo, Duplicate, Mirror, Trash, Eye, Drop, Solid, Hole, Frame,
        Plate, Driver, Sonar, Motor, Caster, Servo, Led,
        Glue, Bend, Route, Question,
        ViewFollow, ViewTop, ViewSide, ViewArena,
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
            // Ellipses from four cubic curves (0.5523 puts the control points on a circle's tangents).
            void Ellipse(float cx, float cy, float rx, float ry, bool fill = false)
            {
                float kx = rx * 0.5523f, ky = ry * 0.5523f;
                p.BeginPath();
                p.MoveTo(P(cx + rx, cy));
                p.BezierCurveTo(P(cx + rx, cy + ky), P(cx + kx, cy + ry), P(cx, cy + ry));
                p.BezierCurveTo(P(cx - kx, cy + ry), P(cx - rx, cy + ky), P(cx - rx, cy));
                p.BezierCurveTo(P(cx - rx, cy - ky), P(cx - kx, cy - ry), P(cx, cy - ry));
                p.BezierCurveTo(P(cx + kx, cy - ry), P(cx + rx, cy - ky), P(cx + rx, cy));
                p.ClosePath();
                if (fill) p.Fill();
                else p.Stroke();
            }
            void FrontHalf(float cx, float cy, float rx, float ry) // the near half of a flat circle seen from above
            {
                float kx = rx * 0.5523f, ky = ry * 0.5523f;
                p.BeginPath();
                p.MoveTo(P(cx + rx, cy));
                p.BezierCurveTo(P(cx + rx, cy + ky), P(cx + kx, cy + ry), P(cx, cy + ry));
                p.BezierCurveTo(P(cx - kx, cy + ry), P(cx - rx, cy + ky), P(cx - rx, cy));
                p.Stroke();
            }
            void Head(float x, float y, float dx, float dy, float size) // an arrow's chevron at (x, y) pointing along (dx, dy)
            {
                var d = new Vector2(dx, dy).normalized;
                var n = new Vector2(-d.y, d.x);
                var back = new Vector2(x, y) - d * size;
                p.BeginPath();
                p.MoveTo(P(back.x + n.x * size * 0.75f, back.y + n.y * size * 0.75f));
                p.LineTo(P(x, y));
                p.LineTo(P(back.x - n.x * size * 0.75f, back.y - n.y * size * 0.75f));
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

                // ---- Body Studio
                case Icon.ShapeBox:
                    Poly(true, false, 12, 3, 20.5f, 7.5f, 20.5f, 16.5f, 12, 21, 3.5f, 16.5f, 3.5f, 7.5f);
                    Poly(false, false, 3.5f, 7.5f, 12, 12, 20.5f, 7.5f);
                    Line(12, 12, 12, 21);
                    break;
                case Icon.ShapeRounded:
                    p.BeginPath();
                    p.MoveTo(P(9, 4));
                    p.ArcTo(P(20, 4), P(20, 20), 5 * scale);
                    p.ArcTo(P(20, 20), P(4, 20), 5 * scale);
                    p.ArcTo(P(4, 20), P(4, 4), 5 * scale);
                    p.ArcTo(P(4, 4), P(20, 4), 5 * scale);
                    p.ClosePath();
                    p.Stroke();
                    Arc(9.5f, 9.5f, 2.5f, 180, 270);
                    break;
                case Icon.ShapeCylinder:
                    Ellipse(12, 6.5f, 7.5f, 2.8f);
                    Line(4.5f, 6.5f, 4.5f, 17.5f);
                    Line(19.5f, 6.5f, 19.5f, 17.5f);
                    FrontHalf(12, 17.5f, 7.5f, 2.8f);
                    break;
                case Icon.ShapeCone:
                    Line(12, 3, 4.5f, 17.5f);
                    Line(12, 3, 19.5f, 17.5f);
                    FrontHalf(12, 17.5f, 7.5f, 2.8f);
                    break;
                case Icon.ShapeSphere:
                    Circle(12, 12, 8.5f);
                    FrontHalf(12, 12, 8.5f, 3f);
                    break;
                case Icon.ShapeWedge:
                    Poly(true, false, 3.5f, 19.5f, 3.5f, 8.5f, 15, 19.5f);
                    Poly(false, false, 3.5f, 8.5f, 8.5f, 5, 20, 16, 15, 19.5f);
                    break;
                case Icon.ShapeTube:
                    Ellipse(12, 6.5f, 7.5f, 2.8f);
                    Ellipse(12, 6.5f, 4f, 1.4f);
                    Line(4.5f, 6.5f, 4.5f, 17.5f);
                    Line(19.5f, 6.5f, 19.5f, 17.5f);
                    FrontHalf(12, 17.5f, 7.5f, 2.8f);
                    break;
                case Icon.Draw: // a pencil
                    Poly(true, false, 4, 20, 5, 15.5f, 16, 4.5f, 19.5f, 8, 8.5f, 19);
                    Line(13.5f, 7, 17, 10.5f);
                    break;
                case Icon.Import:
                    Poly(false, false, 4, 14, 4, 20, 20, 20, 20, 14);
                    Line(12, 16, 12, 4);
                    Head(12, 4, 0, -1, 4);
                    break;
                case Icon.Export:
                    Poly(false, false, 4, 14, 4, 20, 20, 20, 20, 14);
                    Line(12, 4, 12, 16);
                    Head(12, 16, 0, 1, 4);
                    break;
                case Icon.Move:
                    Line(12, 3, 12, 21);
                    Line(3, 12, 21, 12);
                    Head(12, 3, 0, -1, 3);
                    Head(12, 21, 0, 1, 3);
                    Head(3, 12, -1, 0, 3);
                    Head(21, 12, 1, 0, 3);
                    break;
                case Icon.Rotate:
                {
                    Arc(12, 12, 7.5f, -60, 210);
                    float a = 210 * Mathf.Deg2Rad;
                    Head(12 + 7.5f * Mathf.Cos(a), 12 + 7.5f * Mathf.Sin(a), -Mathf.Sin(a), Mathf.Cos(a), 3.5f);
                    break;
                }
                case Icon.Size:
                    Line(6.5f, 17.5f, 17.5f, 6.5f);
                    Head(17.5f, 6.5f, 1, -1, 3.5f);
                    Head(6.5f, 17.5f, -1, 1, 3.5f);
                    Poly(false, false, 3.5f, 9, 3.5f, 3.5f, 9, 3.5f);
                    Poly(false, false, 15, 20.5f, 20.5f, 20.5f, 20.5f, 15);
                    break;
                case Icon.Undo:
                    p.BeginPath();
                    p.MoveTo(P(19, 18.5f));
                    p.BezierCurveTo(P(19, 11), P(15, 8), P(9, 8));
                    p.LineTo(P(5, 8));
                    p.Stroke();
                    Head(5, 8, -1, 0, 3.5f);
                    break;
                case Icon.Redo:
                    p.BeginPath();
                    p.MoveTo(P(5, 18.5f));
                    p.BezierCurveTo(P(5, 11), P(9, 8), P(15, 8));
                    p.LineTo(P(19, 8));
                    p.Stroke();
                    Head(19, 8, 1, 0, 3.5f);
                    break;
                case Icon.Duplicate:
                    Poly(true, false, 8.5f, 8.5f, 20, 8.5f, 20, 20, 8.5f, 20);
                    Poly(false, false, 4, 15.5f, 4, 4, 15.5f, 4);
                    break;
                case Icon.Mirror:
                    for (int i = 0; i < 4; i++) Line(12, 3 + i * 5, 12, 5.5f + i * 5);
                    Poly(true, false, 9, 6, 9, 18, 3, 18);
                    Poly(true, true, 15, 6, 15, 18, 21, 18);
                    break;
                case Icon.Trash:
                    Line(4, 6.5f, 20, 6.5f);
                    Poly(false, false, 9.5f, 6.5f, 9.5f, 4, 14.5f, 4, 14.5f, 6.5f);
                    Poly(false, false, 6, 6.5f, 7, 20.5f, 17, 20.5f, 18, 6.5f);
                    Line(10, 10, 10, 17);
                    Line(14, 10, 14, 17);
                    break;
                case Icon.Eye:
                    p.BeginPath();
                    p.MoveTo(P(2.5f, 12));
                    p.BezierCurveTo(P(6, 6), P(18, 6), P(21.5f, 12));
                    p.BezierCurveTo(P(18, 18), P(6, 18), P(2.5f, 12));
                    p.ClosePath();
                    p.Stroke();
                    Circle(12, 12, 3);
                    break;
                case Icon.Drop: // put on the deck
                    Line(12, 3.5f, 12, 14.5f);
                    Head(12, 14.5f, 0, 1, 3.5f);
                    Line(4, 19.5f, 20, 19.5f);
                    break;
                case Icon.Solid:
                    Poly(true, true, 5, 5, 19, 5, 19, 19, 5, 19);
                    break;
                case Icon.Hole:
                    Poly(true, false, 5, 5, 19, 5, 19, 19, 5, 19);
                    Line(5, 12, 12, 5);
                    Line(5, 19, 19, 5);
                    Line(12, 19, 19, 12);
                    break;
                case Icon.Plate: // a sheet with a grid of holes
                    Poly(true, false, 3, 8, 21, 8, 21, 16, 3, 16);
                    for (int i = 0; i < 4; i++) Circle(6 + i * 4, 12, 0.9f, true);
                    break;
                case Icon.Driver: // a board with a finned heatsink
                    Poly(true, false, 3, 13, 21, 13, 21, 20, 3, 20);
                    for (int i = 0; i < 4; i++) Line(8 + i * 2.7f, 5, 8 + i * 2.7f, 13);
                    Line(6.5f, 5, 17.5f, 5);
                    break;
                case Icon.Sonar: // two round transducers on a board
                    Poly(true, false, 2.5f, 7, 21.5f, 7, 21.5f, 17, 2.5f, 17);
                    Circle(7.5f, 12, 3.5f);
                    Circle(16.5f, 12, 3.5f);
                    break;
                case Icon.Motor: // a gearbox with its wheel
                    Circle(8.5f, 12, 6.5f);
                    Circle(8.5f, 12, 2);
                    Poly(true, false, 13, 8.5f, 21, 8.5f, 21, 15.5f, 13, 15.5f);
                    break;
                case Icon.Caster: // a ball under its holder
                    Poly(false, false, 5, 6, 19, 6);
                    Poly(false, false, 7.5f, 6, 7.5f, 10, 16.5f, 10, 16.5f, 6);
                    Circle(12, 15, 5);
                    break;
                case Icon.Servo: // the case with its tabs, the horn on top
                    Poly(true, false, 7, 10, 17, 10, 17, 20, 7, 20);
                    Poly(false, false, 4, 13, 20, 13);
                    Poly(false, false, 8, 6.5f, 20, 6.5f);
                    Circle(14, 6.5f, 1.4f, true);
                    Poly(false, false, 14, 8, 14, 10);
                    break;
                case Icon.Led: // a 5 mm LED on its legs, glowing
                    Poly(false, false, 9, 16, 9, 9, 10, 6.5f, 12, 5, 14, 6.5f, 15, 9, 15, 16);
                    Poly(false, false, 7.5f, 16, 16.5f, 16);
                    Poly(false, false, 10.5f, 16, 10.5f, 21);
                    Poly(false, false, 13.5f, 16, 13.5f, 21);
                    Poly(false, false, 18, 6, 20.5f, 4.5f);
                    Poly(false, false, 18.5f, 10, 21, 10);
                    break;
                case Icon.Frame:
                    Poly(false, false, 4, 9, 4, 4, 9, 4);
                    Poly(false, false, 15, 4, 20, 4, 20, 9);
                    Poly(false, false, 20, 15, 20, 20, 15, 20);
                    Poly(false, false, 9, 20, 4, 20, 4, 15);
                    Circle(12, 12, 2, true);
                    break;
                case Icon.Glue: // a glue bottle with its nozzle, and a drop
                    Poly(true, false, 5, 21, 5, 12, 7, 9.5f, 11, 9.5f, 13, 12, 13, 21);
                    Poly(false, false, 7.5f, 9.5f, 8.5f, 5.5f, 9.5f, 5.5f, 10.5f, 9.5f);
                    Line(9, 5.5f, 9, 3);
                    Line(5, 15, 13, 15);
                    p.BeginPath();
                    p.MoveTo(P(18, 9));
                    p.BezierCurveTo(P(16.2f, 12), P(15.5f, 13.3f), P(15.5f, 14.5f));
                    p.BezierCurveTo(P(15.5f, 16), P(16.6f, 17), P(18, 17));
                    p.BezierCurveTo(P(19.4f, 17), P(20.5f, 16), P(20.5f, 14.5f));
                    p.BezierCurveTo(P(20.5f, 13.3f), P(19.8f, 12), P(18, 9));
                    p.ClosePath();
                    p.Fill();
                    break;
                case Icon.Bend: // a wire bending smoothly through a point
                    p.BeginPath();
                    p.MoveTo(P(3, 19));
                    p.BezierCurveTo(P(9, 19), P(8, 12), P(12, 12));
                    p.BezierCurveTo(P(16, 12), P(15, 5), P(21, 5));
                    p.Stroke();
                    Circle(12, 12, 2.6f);
                    break;
                case Icon.Route: // a way found between two ends, with a spark: done by itself
                    Circle(5, 19, 1.8f, true);
                    Circle(15, 5, 1.8f, true);
                    p.BeginPath();
                    p.MoveTo(P(5, 19));
                    p.BezierCurveTo(P(5, 11), P(15, 13), P(15, 5));
                    p.Stroke();
                    Line(19.5f, 12.5f, 19.5f, 19.5f);
                    Line(16, 16, 23, 16);
                    break;
                case Icon.ViewFollow: // the robot seen from a camera behind it
                    Poly(true, false, 7, 3.5f, 17, 3.5f, 17, 10, 7, 10);
                    Line(9, 12, 9, 13.5f);
                    Line(15, 12, 15, 13.5f);
                    Poly(true, false, 8, 16, 14.5f, 16, 14.5f, 21, 8, 21);
                    Poly(true, false, 14.5f, 17.2f, 17.5f, 15.8f, 17.5f, 21.2f, 14.5f, 19.8f);
                    break;
                case Icon.ViewTop: // looking straight down on a robot
                    Line(12, 2.5f, 12, 8.5f);
                    Head(12, 8.5f, 0, 1, 2.6f);
                    Poly(true, false, 6.5f, 11.5f, 17.5f, 11.5f, 17.5f, 21, 6.5f, 21);
                    Line(4.5f, 13, 4.5f, 19.5f);
                    Line(19.5f, 13, 19.5f, 19.5f);
                    break;
                case Icon.ViewSide: // a robot from its side: body, two wheels, an eye beside it
                    Poly(true, false, 4, 8, 17, 8, 17, 14, 4, 14);
                    Circle(7.5f, 16.5f, 3);
                    Circle(14, 16.5f, 3);
                    Line(20.5f, 9, 20.5f, 13);
                    break;
                case Icon.ViewArena: // the arena's floor in perspective with the robot on it
                    Poly(true, false, 2.5f, 20, 21.5f, 20, 17.5f, 6, 6.5f, 6);
                    Line(12, 6, 12, 20);
                    Line(4.5f, 13, 19.5f, 13);
                    Circle(15.5f, 16.5f, 1.8f, true);
                    break;
                case Icon.Question:
                    Circle(12, 12, 9.5f);
                    p.BeginPath();
                    p.MoveTo(P(9, 9.5f));
                    p.BezierCurveTo(P(9, 6.3f), P(15, 6.3f), P(15, 9.5f));
                    p.BezierCurveTo(P(15, 12), P(12, 11.6f), P(12, 14.3f));
                    p.Stroke();
                    Circle(12, 17.6f, 1.1f, true);
                    break;
            }
        }
    }
}
