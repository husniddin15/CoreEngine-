using System;
using UnityEngine;

namespace CoreEngine.Spike
{
    /// <summary>The arena's viewpoints, in the order of their buttons and of the keys 1 to 6.</summary>
    public enum ArenaView { Follow, Orbit, Top, Side, Eye, Arena }

    /// <summary>
    /// The arena's camera, with six viewpoints (the owner, 2026-09-25: the arena only showed the robot from
    /// behind): behind the robot, following it (Follow); all round it with the mouse (Orbit); straight down on it
    /// (Top); beside it (Side); from its distance sensor, as the robot sees the way ahead (Eye); and the whole
    /// arena from above (Arena). They are chosen with the buttons over the view, the keys 1 to 6, or C for the
    /// next. The wheel zooms any of them; a drag over the view with the left or right button turns it round the
    /// robot (it becomes Orbit), with the middle button or Shift it moves the point turned about. Each viewpoint
    /// glides in; the robot's own eye moves with it at once.
    /// </summary>
    public sealed class ArenaCamera : MonoBehaviour
    {
        public const int Views = 6;

        /// <summary>The robot's chassis, which the viewpoints follow.</summary>
        public Transform? Target;

        /// <summary>The distance sensor's front faces, looking the way it measures (the robot's eye); null without one.</summary>
        public Transform? Eye;

        /// <summary>True where a panel or button, not the 3D view, is under the mouse (screen pixels).</summary>
        public Func<Vector2, bool>? OverUi;

        Camera view = null!;
        ArenaView mode = ArenaView.Follow;
        float zoom = 1;                            // every viewpoint's distance, times this (the wheel)
        float yaw, pitch = 25, distance = 0.6f;    // Orbit
        Vector3 pan;                               // Orbit: the point turned about, from the robot
        bool pressed, dragging, snap = true;
        int button;
        Vector2 pressAt, lastMouse;

        /// <summary>The viewpoint shown or gliding in; setting it glides there (see <see cref="Show"/>).</summary>
        public ArenaView View
        {
            get => mode;
            set => Show(value);
        }

        void Awake()
        {
            view = GetComponent<Camera>();
        }

        /// <summary>Goes to a viewpoint, gliding, or at once with <paramref name="now"/> (for pictures).</summary>
        public void Show(ArenaView next, bool now = false)
        {
            if (next == ArenaView.Orbit && mode != ArenaView.Orbit) OrbitFromHere();
            mode = next;
            zoom = 1;
            snap |= now;
        }

        /// <summary>Orbit starts where the camera is, so taking hold of the view does not make it jump.</summary>
        void OrbitFromHere()
        {
            if (Target == null) return;
            pan = Vector3.zero;
            var offset = transform.position - Target.position;
            distance = Mathf.Clamp(offset.magnitude, 0.12f, 6f);
            var look = -offset.normalized;
            yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(-look.y, -1f, 1f)) * Mathf.Rad2Deg, 3f, 89f);
        }

        void LateUpdate()
        {
            if (Target == null) return;
            ReadInput();
            var (position, rotation, fov, near) = Pose();
            float t = snap ? 1 : 1 - Mathf.Exp(-Time.unscaledDeltaTime * (mode == ArenaView.Eye ? 40f : 8f));
            snap = false;
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, t), Quaternion.Slerp(transform.rotation, rotation, t));
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, fov, t);
            view.nearClipPlane = near;
        }

        /// <summary>Where each viewpoint puts the camera now: position, turn, field of view and near clip (metres).</summary>
        (Vector3 position, Quaternion rotation, float fov, float near) Pose()
        {
            var robot = Target!.position;
            var forward = Vector3.ProjectOnPlane(Target.forward, Vector3.up);
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            switch (mode)
            {
                case ArenaView.Follow:
                {
                    var at = robot - forward * (0.45f * zoom) + Vector3.up * (0.28f * zoom);
                    return (at, Quaternion.LookRotation(robot + forward * 0.15f - at), 55, 0.01f);
                }
                case ArenaView.Top: // the arena's far wall at the top of the picture, as on a map
                    return (robot + Vector3.up * (1.1f * zoom), Quaternion.Euler(90, 0, 0), 55, 0.01f);
                case ArenaView.Side:
                {
                    var at = robot + right * (0.55f * zoom) + Vector3.up * (0.1f * zoom);
                    return (at, Quaternion.LookRotation(robot + Vector3.up * 0.03f - at), 55, 0.01f);
                }
                case ArenaView.Eye:
                {
                    // Just in front of the transducers, looking where they look and a little down, wide as an eye.
                    var look = Eye != null ? Eye.forward : Target.forward;
                    var at = Eye != null ? Eye.position + look * 0.004f : robot + forward * 0.1f + Vector3.up * 0.08f;
                    return (at, Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(6, 0, 0), 75, 0.004f);
                }
                case ArenaView.Arena: // the whole 3 m arena from high above its near side
                    return (new Vector3(0, 3.2f, -0.6f) * zoom, Quaternion.Euler(80f, 0, 0), 55, 0.05f);
                default:
                {
                    var turn = Quaternion.Euler(pitch, yaw, 0);
                    var pivot = robot + pan;
                    return (pivot - turn * Vector3.forward * distance, turn, 55, 0.01f);
                }
            }
        }

        void ReadInput()
        {
            if (!UI.CodeEditor.HasTypingFocus)
            {
                if (Input.GetKeyDown(KeyCode.C)) Show((ArenaView)(((int)mode + 1) % Views));
                for (int i = 0; i < Views; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Show((ArenaView)i);
            }
            var mouse = (Vector2)Input.mousePosition;
            bool inView = view.pixelRect.Contains(mouse) && (OverUi == null || !OverUi(mouse));
            if (inView && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
            {
                float factor = 1 - Mathf.Clamp(Input.mouseScrollDelta.y, -3, 3) * 0.1f;
                if (mode == ArenaView.Orbit) distance = Mathf.Clamp(distance * factor, 0.12f, 6f);
                else zoom = Mathf.Clamp(zoom * factor, 0.35f, 3f);
            }

            // A press over the view that moves more than a few pixels takes hold of it.
            if (!pressed)
            {
                for (int b = 0; b < 3 && !pressed; b++)
                {
                    if (!inView || !Input.GetMouseButtonDown(b)) continue;
                    pressed = true;
                    dragging = false;
                    button = b;
                    pressAt = lastMouse = mouse;
                }
                return;
            }
            if (!Input.GetMouseButton(button))
            {
                pressed = dragging = false;
                return;
            }
            var delta = mouse - lastMouse;
            lastMouse = mouse;
            if (!dragging)
            {
                if ((mouse - pressAt).magnitude <= 4) return;
                dragging = true;
                if (mode != ArenaView.Orbit) Show(ArenaView.Orbit);
            }
            if (button == 2 || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                // The spot under the mouse follows it: a pixel is this much at the pivot's distance.
                float k = 2 * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, view.pixelHeight);
                pan -= (transform.right * delta.x + transform.up * delta.y) * k;
                pan = Vector3.ClampMagnitude(pan, 1.5f);
            }
            else
            {
                yaw += delta.x * 0.3f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.2f, 3f, 89f);
            }
        }
    }
}
