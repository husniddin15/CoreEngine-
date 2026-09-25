using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.UI
{
    /// <summary>
    /// A view cube, as in Tinkercad and CAD programs: a small cube that turns with the view and names the robot's
    /// sides. A click on one of its faces, edges or corners asks for the view from there (<see cref="Picked"/>
    /// gets the direction from the robot toward the camera, in the robot's frame, +x right, +y up, +z forward);
    /// a drag on it turns the view (<see cref="Dragged"/>, in pixels). Drawn with the vector painter; the owner
    /// calls <see cref="Refresh"/> every frame with how the camera sees the robot.
    /// </summary>
    public sealed class ViewCube : VisualElement
    {
        /// <summary>A face's middle reaches this far toward its sides (of 1); the rest are edge and corner zones.</summary>
        const float Band = 0.55f;

        static readonly Vector3[] Normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly string[] Keys = { "view.right", "view.left", "view.top", "view.bottom", "view.front", "view.back" };

        static readonly Color Face = new Color(0.95f, 0.96f, 0.98f, 0.96f);
        static readonly Color Edge = new Color(0.36f, 0.40f, 0.47f, 1f);
        static readonly Color Hover = new Color(0.31f, 0.76f, 1.0f, 0.9f);

        /// <summary>A face, edge or corner was clicked: the unit direction from the robot to where the camera goes.</summary>
        public event Action<Vector3>? Picked;

        /// <summary>The cube is being dragged: pixels moved since the last event.</summary>
        public event Action<Vector2>? Dragged;

        readonly Label[] labels = new Label[6];
        Quaternion seen = Quaternion.identity; // the robot's frame in the camera's (x right, y up, z into the screen)
        Vector3Int? hovered;
        Vector2 pressAt, lastAt;
        bool pressed, dragging;

        public ViewCube()
        {
            AddToClassList("view-cube");
            for (int i = 0; i < 6; i++)
            {
                labels[i] = new Label { pickingMode = PickingMode.Ignore };
                labels[i].AddToClassList("view-cube-label");
                Add(labels[i]);
            }
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (hovered == null) return;
                hovered = null;
                MarkDirtyRepaint();
            });
        }

        /// <summary>The zone under the mouse, for tests: the direction a click there would pick, or null.</summary>
        public Vector3Int? Hovered => hovered;

        /// <summary>
        /// Turns the cube as the camera sees the robot: <paramref name="robotInCamera"/> is the camera's rotation
        /// inverted times the robot's. Also renames the faces after a change of language.
        /// </summary>
        public void Refresh(Quaternion robotInCamera)
        {
            seen = robotInCamera;
            var rect = contentRect;
            float scale = Scale(rect);
            var centre = rect.center;
            for (int i = 0; i < 6; i++)
            {
                labels[i].text = SpikeStrings.Get(Keys[i]);
                var n = seen * Normals[i];
                float facing = -n.z;
                var label = labels[i];
                label.style.opacity = Mathf.Clamp01((facing - 0.3f) / 0.4f);
                var at = centre + new Vector2(n.x, -n.y) * scale;
                label.style.left = at.x;
                label.style.top = at.y;
                // The name shrinks with its face as the face turns away, like printing on the face would.
                label.style.scale = new Scale(Vector3.one * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(facing)));
            }
            MarkDirtyRepaint();
        }

        /// <summary>A screen point (the element's own pixels) to the zone of the cube under it, or null.</summary>
        public Vector3Int? ZoneAt(Vector2 local)
        {
            var rect = contentRect;
            float scale = Scale(rect);
            Vector3Int? best = null;
            float nearest = float.MaxValue;
            for (int i = 0; i < 6; i++)
            {
                var n = Normals[i];
                var cn = seen * n;
                if (cn.z > -0.05f) continue; // turned away
                Axes(n, out var b, out var c);
                var centre = rect.center + Flip(cn) * scale;
                var u = Flip(seen * b) * scale;
                var v = Flip(seen * c) * scale;
                float det = u.x * v.y - u.y * v.x;
                if (Mathf.Abs(det) < 1e-4f) continue;
                var d = local - centre;
                float fu = (d.x * v.y - d.y * v.x) / det, fv = (u.x * d.y - u.y * d.x) / det;
                if (Mathf.Abs(fu) > 1 || Mathf.Abs(fv) > 1) continue;
                if (cn.z >= nearest) continue; // a face nearer the camera wins (only at the outline)
                nearest = cn.z;
                var zone = n + Step(fu) * b + Step(fv) * c;
                best = new Vector3Int(Mathf.RoundToInt(zone.x), Mathf.RoundToInt(zone.y), Mathf.RoundToInt(zone.z));
            }
            return best;
        }

        /// <summary>Where a face's middle is drawn (the element's own pixels), or null when it is turned away.</summary>
        public Vector2? FaceCentre(Vector3Int normal)
        {
            var n = seen * (Vector3)normal;
            if (n.z > -0.05f) return null;
            return contentRect.center + Flip(n) * Scale(contentRect);
        }

        static float Scale(Rect rect) => Mathf.Min(rect.width, rect.height) * 0.29f;

        static Vector2 Flip(Vector3 v) => new Vector2(v.x, -v.y);

        static float Step(float t) => t < -Band ? -1 : t > Band ? 1 : 0;

        /// <summary>Two axes along a face, square to its normal.</summary>
        static void Axes(Vector3 n, out Vector3 b, out Vector3 c)
        {
            if (Mathf.Abs(n.x) > 0.5f)
            {
                b = Vector3.forward;
                c = Vector3.up;
            }
            else if (Mathf.Abs(n.y) > 0.5f)
            {
                b = Vector3.right;
                c = Vector3.forward;
            }
            else
            {
                b = Vector3.right;
                c = Vector3.up;
            }
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float scale = Scale(rect);
            if (scale < 2) return;
            var p = context.painter2D;
            p.lineJoin = LineJoin.Round;
            for (int i = 0; i < 6; i++)
            {
                var n = Normals[i];
                var cn = seen * n;
                if (cn.z > -0.02f) continue;
                Axes(n, out var b, out var c);
                var centre = rect.center + Flip(cn) * scale;
                var u = Flip(seen * b) * scale;
                var v = Flip(seen * c) * scale;
                // The face, lit by how squarely it faces the camera, then the hovered zone on it.
                float light = Mathf.Lerp(0.80f, 1f, -cn.z);
                p.fillColor = new Color(Face.r * light, Face.g * light, Face.b * light, Face.a);
                Quad(p, centre, u, v, -1, 1, -1, 1);
                p.Fill();
                if (hovered != null)
                {
                    var zone = (Vector3)hovered.Value;
                    if (Vector3.Dot(zone, n) > 0.5f)
                    {
                        var (u0, u1) = Range(Vector3.Dot(zone, b));
                        var (v0, v1) = Range(Vector3.Dot(zone, c));
                        p.fillColor = Hover;
                        Quad(p, centre, u, v, u0, u1, v0, v1);
                        p.Fill();
                    }
                }
                p.strokeColor = Edge;
                p.lineWidth = 1.3f;
                Quad(p, centre, u, v, -1, 1, -1, 1);
                p.Stroke();
            }
        }

        static (float, float) Range(float along) => along < -0.5f ? (-1f, -Band) : along > 0.5f ? (Band, 1f) : (-Band, Band);

        static void Quad(Painter2D p, Vector2 centre, Vector2 u, Vector2 v, float u0, float u1, float v0, float v1)
        {
            p.BeginPath();
            p.MoveTo(centre + u * u0 + v * v0);
            p.LineTo(centre + u * u1 + v * v0);
            p.LineTo(centre + u * u1 + v * v1);
            p.LineTo(centre + u * u0 + v * v1);
            p.ClosePath();
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            pressed = true;
            dragging = false;
            pressAt = lastAt = e.localPosition;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            Vector2 at = e.localPosition;
            if (pressed)
            {
                if (!dragging && (at - pressAt).magnitude > 3) dragging = true;
                if (dragging) Dragged?.Invoke(at - lastAt);
                lastAt = at;
                e.StopPropagation();
                return;
            }
            var zone = ZoneAt(at);
            if (zone == hovered) return;
            hovered = zone;
            MarkDirtyRepaint();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!pressed || e.button != 0) return;
            pressed = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            if (!dragging)
            {
                var zone = ZoneAt(e.localPosition);
                if (zone != null) Pick(zone.Value);
            }
            dragging = false;
            e.StopPropagation();
        }

        /// <summary>Asks for the view from a zone, as a click there does (also for the benchmark).</summary>
        public void Pick(Vector3Int zone) => Picked?.Invoke(((Vector3)zone).normalized);
    }
}
