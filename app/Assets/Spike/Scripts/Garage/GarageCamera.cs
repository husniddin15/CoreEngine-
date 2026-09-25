using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The Garage's camera, one set of controls in every mode, as in Tinkercad (docs/03 §3.1):
    /// <list type="bullet">
    /// <item>a drag turns the view round its pivot (the right button in Wire and the Studio, where the left one
    /// picks), Shift+drag or the middle button moves the pivot, the wheel zooms toward the mouse, and a
    /// double-click makes the spot under the mouse the pivot;</item>
    /// <item>the view cube names the robot's sides and turns the view to a face, edge or corner; Home and Fit put
    /// it back or frame the whole robot; each of these glides there;</item>
    /// <item>the pivot stays near the robot, the camera within reach of it, and in the showroom above the bench;
    /// in Wire and the Studio it may look from below, and the room is then left out of the picture.</item>
    /// </list>
    /// The turntable is only for the showroom: in Wire and the Studio the robot stands on the bench's measuring
    /// mat. In the showroom the depth of field blurs only what lies behind the robot, so every part of it stays
    /// sharp however close the camera comes.
    /// </summary>
    public sealed partial class GarageSpike
    {
        /// <summary>The white lab's shell (WhiteLab.ShellLayer) and, from start-up, the rest of the lab.</summary>
        const int ShellLayer = 8, LabLayer = 9;

        enum ViewDrag { None, Turn, Pan }

        struct View
        {
            public float Yaw, Pitch, Distance;
            public Vector3 Target;
        }

        View? viewGoal;               // where a glide is heading
        ViewDrag viewDrag;
        int viewDragButton;
        float lastClickTime = -10;
        Vector2 lastClickAt;
        Bounds robotBounds = new Bounds(new Vector3(0, 0.04f, 0), new Vector3(0.16f, 0.08f, 0.16f)); // the robot, in the anchor's frame
        ViewCube? viewCube;
        VisualElement? viewTools;

        float MinPitch => mode == EditMode.None ? 3f : -90f;
        // The part photos (-partShots) come closer to a part and step farther back into the room.
        float MinDistance => photographingParts ? 0.03f : mode == EditMode.None ? 0.22f : 0.1f;
        float MaxDistance => photographingParts ? 10f : 1.4f;

        // ------------------------------------------------------------------ the tools: view cube, Home, Fit

        void BuildViewTools()
        {
            viewTools = Layout("view-tools");
            viewCube = new ViewCube();
            viewCube.tooltip = Tr("view.hint");
            viewCube.Picked += LookFrom;
            viewCube.Dragged += delta =>
            {
                viewGoal = null;
                idleSeconds = 0;
                TurnBy(new Vector2(delta.x, -delta.y) * 1.2f);
            };
            viewTools.Add(viewCube);
            var buttons = Layout("view-buttons");
            buttons.Add(ViewButton(Icon.Garage, "view.home", HomeView));
            buttons.Add(ViewButton(Icon.Frame, "view.fit", FitView));
            viewTools.Add(buttons);
            PlaceViewTools();
        }

        Button ViewButton(Icon icon, string key, System.Action onClick)
        {
            var button = new Button(() =>
            {
                idleSeconds = 0;
                onClick();
            }) { focusable = false, tooltip = Tr(key) };
            button.AddToClassList("view-button");
            button.Add(new IconView(icon));
            return button;
        }

        /// <summary>In the Studio the tools sit in the corner of its free view; elsewhere left of the right column.</summary>
        void PlaceViewTools()
        {
            if (viewTools == null) return;
            bool inStudio = mode == EditMode.Body && studio != null;
            var parent = inStudio ? studioViewport : root;
            if (viewTools.parent != parent)
            {
                viewTools.RemoveFromHierarchy();
                if (inStudio) parent.Add(viewTools);
                else root.Insert(root.IndexOf(toast), viewTools); // under the toast, pages and tooltip
            }
            viewTools.EnableInClassList("view-tools--studio", inStudio);
            viewTools.EnableInClassList("view-tools--garage", !inStudio);
        }

        // ------------------------------------------------------------------ mouse

        /// <summary>
        /// The camera's part of this frame's mouse: presses on the scene that start a turn or a pan (the left
        /// button only where <paramref name="leftTurns"/>, in the showroom), the drag under way, and the wheel.
        /// </summary>
        void CameraControls(bool overUi, bool leftTurns)
        {
            var mouse = input.Position;
            if (!overUi)
            {
                if (input.MiddlePressed) BeginViewDrag(ViewDrag.Pan, 2, mouse);
                else if (input.RightPressed) BeginViewDrag(input.Shift ? ViewDrag.Pan : ViewDrag.Turn, 1, mouse);
                else if (leftTurns && input.LeftPressed)
                {
                    if (input.Scripted ? input.Double : DoubleClick(mouse)) PivotAt(mouse);
                    else BeginViewDrag(input.Shift ? ViewDrag.Pan : ViewDrag.Turn, 0, mouse);
                }
            }
            if (viewDrag != ViewDrag.None)
            {
                bool held = viewDragButton == 0 ? input.LeftHeld : viewDragButton == 1 ? input.RightHeld : input.MiddleHeld;
                if (!held)
                {
                    viewDrag = ViewDrag.None;
                }
                else
                {
                    var delta = mouse - (Vector2)lastMouse;
                    lastMouse = mouse;
                    if (viewDrag == ViewDrag.Turn) TurnBy(delta);
                    else PanBy(delta);
                }
            }
            if (!overUi && Mathf.Abs(input.Scroll) > 0.01f) ZoomAt(mouse, input.Scroll);
        }

        void BeginViewDrag(ViewDrag kind, int button, Vector2 mouse)
        {
            viewDrag = kind;
            viewDragButton = button;
            lastMouse = mouse;
            viewGoal = null;
            idleSeconds = 0;
        }

        /// <summary>A left drag on the scene in Wire or the Studio: turns the view, or with Shift moves it.</summary>
        void DragView(Vector2 mouse)
        {
            var delta = mouse - (Vector2)lastMouse;
            lastMouse = mouse;
            viewGoal = null;
            if (input.Shift) PanBy(delta);
            else TurnBy(delta);
        }

        /// <summary>A second click within a third of a second, near the first.</summary>
        bool DoubleClick(Vector2 mouse)
        {
            bool second = Time.unscaledTime - lastClickTime < 0.35f && (mouse - lastClickAt).magnitude < 6;
            lastClickTime = second ? -10 : Time.unscaledTime;
            lastClickAt = mouse;
            return second;
        }

        void TurnBy(Vector2 delta)
        {
            yaw += delta.x * 0.3f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.2f, MinPitch, 89f);
            idleSeconds = 0;
        }

        void PanBy(Vector2 delta)
        {
            // The spot under the mouse follows it: a pixel is this much at the pivot's distance.
            float k = 2 * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            MoveTarget(orbitTarget - view.transform.right * delta.x * k - view.transform.up * delta.y * k);
            idleSeconds = 0;
        }

        /// <summary>The wheel: in toward the spot under the mouse, out toward the robot's middle.</summary>
        void ZoomAt(Vector2 mouse, float scroll)
        {
            viewGoal = null;
            float old = distance;
            distance = Mathf.Clamp(distance * (1f - scroll * 0.12f), MinDistance, MaxDistance);
            if (distance < old && PointUnder(mouse, out var spot)) MoveTarget(orbitTarget + (spot - orbitTarget) * (1 - distance / old));
            else if (distance > old) MoveTarget(orbitTarget + (RobotCentre() - orbitTarget) * (1 - old / distance));
            idleSeconds = 0;
        }

        /// <summary>A double-click: the spot under the mouse becomes the pivot, and the view glides to it.</summary>
        void PivotAt(Vector2 mouse)
        {
            if (!PointUnder(mouse, out var spot)) return;
            float away = Vector3.Distance(view.transform.position, spot);
            GlideTo(yaw, pitch, Mathf.Clamp(Mathf.Min(distance, away), MinDistance, MaxDistance), spot);
        }

        /// <summary>
        /// The robot's surface under the mouse: a collider's (the Studio's parts and shapes), else the nearest box
        /// of one of its pieces, else the floor it stands on.
        /// </summary>
        bool PointUnder(Vector2 mouse, out Vector3 point)
        {
            var ray = view.ScreenPointToRay(mouse);
            if (Physics.Raycast(ray, out var hit, 5f))
            {
                point = hit.point;
                return true;
            }
            float nearest = float.MaxValue;
            if (shown != null)
                foreach (var renderer in shown.Root.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.IntersectRay(ray, out float d) && d < nearest && d > 0)
                        nearest = d;
            if (nearest < float.MaxValue)
            {
                point = ray.GetPoint(nearest);
                return true;
            }
            if (new Plane(robotAnchor.up, robotAnchor.position).Raycast(ray, out float enter) && enter < 5f)
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = default;
            return false;
        }

        /// <summary>Moves the pivot at once, kept near the robot.</summary>
        void MoveTarget(Vector3 target) => orbitTarget = ClampTarget(target);

        /// <summary>Within 12 cm of the robot's sides, from its floor to 12 cm above its top.</summary>
        Vector3 ClampTarget(Vector3 target)
        {
            var local = robotAnchor.InverseTransformPoint(target);
            var min = robotBounds.min - new Vector3(0.12f, 0.02f, 0.12f);
            var max = robotBounds.max + new Vector3(0.12f, 0.12f, 0.12f);
            local = new Vector3(Mathf.Clamp(local.x, min.x, max.x), Mathf.Clamp(local.y, min.y, max.y), Mathf.Clamp(local.z, min.z, max.z));
            return robotAnchor.TransformPoint(local);
        }

        Vector3 RobotCentre() => robotAnchor.TransformPoint(robotBounds.center);

        // ------------------------------------------------------------------ views

        /// <summary>The view cube: from the robot's side, edge or corner in <paramref name="direction"/> (its frame).</summary>
        void LookFrom(Vector3 direction)
        {
            var from = robotAnchor.TransformDirection(direction).normalized;
            var front = robotAnchor.forward;
            float frontYaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg; // looking at the robot's front
            float newPitch = Mathf.Asin(Mathf.Clamp(from.y, -1, 1)) * Mathf.Rad2Deg;
            // Straight above or below, the robot's front is at the bottom (or top) of the picture.
            float newYaw = new Vector2(from.x, from.z).magnitude < 1e-3f ? frontYaw : Mathf.Atan2(-from.x, -from.z) * Mathf.Rad2Deg;
            GlideTo(newYaw, Mathf.Clamp(newPitch, MinPitch, 90f), distance, orbitTarget);
        }

        /// <summary>The view each mode starts with.</summary>
        void HomeView()
        {
            if (mode == EditMode.None) GlideTo(215f, 14f, 0.62f, DefaultTarget);
            else GlideTo(200f, 42f, mode == EditMode.Wire ? 0.40f : 0.52f, DefaultTarget);
        }

        /// <summary>The whole robot, filling most of the picture, from where the camera looks now.</summary>
        void FitView()
        {
            float radius = Mathf.Max(0.04f, robotBounds.extents.magnitude);
            float half = Mathf.Min(view.fieldOfView, Camera.VerticalToHorizontalFieldOfView(view.fieldOfView, view.aspect)) * 0.5f * Mathf.Deg2Rad;
            GlideTo(yaw, pitch, Mathf.Clamp(radius / Mathf.Sin(half) * 1.05f, MinDistance, MaxDistance), RobotCentre());
        }

        void GlideTo(float toYaw, float toPitch, float toDistance, Vector3 toTarget)
        {
            viewGoal = new View { Yaw = toYaw, Pitch = toPitch, Distance = toDistance, Target = ClampTarget(toTarget) };
            idleSeconds = 0;
        }

        /// <summary>Jumps to a view (entering or leaving a mode).</summary>
        void SetView(float toYaw, float toPitch, float toDistance, Vector3 toTarget)
        {
            viewGoal = null;
            yaw = toYaw;
            pitch = toPitch;
            distance = toDistance;
            orbitTarget = toTarget;
        }

        // ------------------------------------------------------------------ every frame

        void UpdateCamera()
        {
            if (viewGoal != null)
            {
                var goal = viewGoal.Value;
                float t = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 9f);
                yaw = Mathf.LerpAngle(yaw, goal.Yaw, t);
                pitch = Mathf.Lerp(pitch, goal.Pitch, t);
                distance = Mathf.Lerp(distance, goal.Distance, t);
                orbitTarget = Vector3.Lerp(orbitTarget, goal.Target, t);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, goal.Yaw)) < 0.05f && Mathf.Abs(pitch - goal.Pitch) < 0.05f &&
                    Mathf.Abs(distance - goal.Distance) < 0.0002f && (orbitTarget - goal.Target).sqrMagnitude < 1e-8f)
                {
                    SetView(goal.Yaw, goal.Pitch, goal.Distance, goal.Target);
                }
            }
            pitch = Mathf.Clamp(pitch, MinPitch, 90f);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            view.transform.SetPositionAndRotation(orbitTarget - rotation * Vector3.forward * distance, rotation);

            // From below the bench (Wire and the Studio only) the room would hide the robot: it is left out.
            bool below = mode != EditMode.None && view.transform.position.y < robotAnchor.position.y - 0.004f;
            view.cullingMask = below ? ~((1 << ShellLayer) | (1 << LabLayer)) : ~0;

            UpdateDepthOfField();
            viewCube?.Refresh(Quaternion.Inverse(view.transform.rotation) * robotAnchor.rotation);
            ApplyStudioProjection();
        }

        /// <summary>
        /// A photo's soft background without a soft robot: in the showroom only what lies behind the robot's far
        /// side is blurred, more with distance; the edit modes and part photos are sharp all over.
        /// </summary>
        void UpdateDepthOfField()
        {
            if (depthOfField == null) return;
            bool on = mode == EditMode.None && !photographingParts;
            depthOfField.active = on;
            if (!on) return;
            var forward = view.transform.forward;
            var eye = view.transform.position;
            float far = distance;
            var c = robotBounds.center;
            var e = robotBounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = robotAnchor.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                far = Mathf.Max(far, Vector3.Dot(corner - eye, forward));
            }
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(far + 0.03f);
            depthOfField.gaussianEnd.Override(far + 0.9f);
            depthOfField.gaussianMaxRadius.Override(1.0f);
            depthOfField.highQualitySampling.Override(true);
        }

        /// <summary>The robot's box in the anchor's frame, after it is rebuilt: for the pivot's limits, Fit and the blur.</summary>
        void MeasureRobot()
        {
            if (shown == null) return;
            var bounds = new Bounds();
            bool any = false;
            foreach (var renderer in shown.Root.GetComponentsInChildren<MeshRenderer>())
            {
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = robotAnchor.InverseTransformPoint(corner);
                    if (!any) bounds = new Bounds(local, Vector3.zero);
                    else bounds.Encapsulate(local);
                    any = true;
                }
            }
            robotBounds = any ? bounds : new Bounds(new Vector3(0, 0.04f, 0), new Vector3(0.16f, 0.08f, 0.16f));
        }

        /// <summary>The turntable is the showroom's: in Wire and the Studio the robot stands on the mat.</summary>
        void ShowTurntable(bool on)
        {
            foreach (Transform child in turntable)
                if (child != robotAnchor) child.gameObject.SetActive(on);
        }

        /// <summary>Puts the lab (not its shell, already on its own layer) on the lab's layer, to leave out from below.</summary>
        static void LayerTheLab()
        {
            var set = GameObject.Find("LabSet");
            if (set == null) return;
            foreach (var t in set.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0) t.gameObject.layer = LabLayer;
        }
    }
}
