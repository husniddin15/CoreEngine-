using System.Collections;
using System.Collections.Generic;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The benchmark's checks (-spikeBench) of the camera, the view cube, the code editor's editing and the wire
    /// router, through the same mouse, key and pointer paths a player uses.
    /// </summary>
    public sealed partial class GarageSpike
    {
        /// <summary>Until a camera glide has arrived (at most two seconds).</summary>
        IEnumerator WaitForGlide()
        {
            float start = Time.realtimeSinceStartup;
            while (viewGoal != null && Time.realtimeSinceStartup - start < 2f) yield return null;
            yield return null;
        }

        /// <summary>
        /// The showroom camera: a click on the view cube's top looks straight down, its corner from 35° up; a
        /// middle-button drag moves the pivot and a very long one stops near the robot; a double-click on the Uno
        /// makes it the pivot; the wheel zooms in to the closest view with the robot sharp all over.
        /// </summary>
        IEnumerator CameraCheck()
        {
            var report = SpikeReport.Text;
            holdTurntable = true; // the robot must not turn away while its parts are being clicked
            HomeView();
            yield return WaitForGlide();

            // The cube's TOP, clicked through the panel as the mouse would.
            yield return Frames(2);
            var cube = viewCube!;
            var topFace = cube.FaceCentre(new Vector3Int(0, 1, 0));
            if (topFace != null)
            {
                var at = cube.LocalToWorld(topFace.Value);
                SendPointer(EventType.MouseMove, at);
                SendPointer(EventType.MouseDown, at);
                yield return null;
                SendPointer(EventType.MouseUp, at);
            }
            yield return Frames(2);
            yield return WaitForGlide();
            float topPitch = pitch;
            bool straightDown = pitch > 89.5f && view.transform.position.y > robotAnchor.position.y + 0.3f;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-top"));
            cube.Pick(new Vector3Int(1, 1, 1));
            yield return WaitForGlide();
            float cornerPitch = pitch;

            // A middle-button drag across a sixth of the screen moves the pivot; a very long one stops near the robot.
            var middle = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var before = orbitTarget;
            yield return Play(ViewDragFrames(middle, middle + new Vector2(Screen.width / 6f, 0), middleButton: true));
            float moved = Vector3.Distance(before, orbitTarget);
            yield return Play(ViewDragFrames(middle, middle + new Vector2(Screen.width * 3f, 0), middleButton: true));
            var local = robotAnchor.InverseTransformPoint(orbitTarget);
            float beyond = Mathf.Max(robotBounds.min.x - local.x, local.x - robotBounds.max.x, robotBounds.min.z - local.z, local.z - robotBounds.max.z);

            // A double-click on the Uno: it becomes the pivot.
            HomeView();
            yield return WaitForGlide();
            float pivotGap = -1;
            if (shown != null && shown.Parts.TryGetValue("uno1", out var uno))
            {
                var bounds = uno.GetComponentInChildren<MeshRenderer>().bounds;
                var at = (Vector2)view.WorldToScreenPoint(bounds.center + Vector3.up * bounds.extents.y * 0.5f);
                yield return Play(new List<PointerFrame>
                {
                    new PointerFrame { Position = at },
                    new PointerFrame { Position = at, LeftPressed = true, LeftHeld = true },
                    new PointerFrame { Position = at },
                    new PointerFrame { Position = at, LeftPressed = true, LeftHeld = true, Double = true },
                    new PointerFrame { Position = at },
                });
                yield return WaitForGlide();
                pivotGap = Mathf.Sqrt(bounds.SqrDistance(orbitTarget)) * 1000;
            }

            // The wheel, in as far as it goes at the robot's middle.
            HomeView();
            yield return WaitForGlide();
            var zoom = new List<PointerFrame>();
            for (int i = 0; i < 30; i++) zoom.Add(new PointerFrame { Position = middle, Scroll = 1 });
            yield return Play(zoom);
            yield return Frames(3);
            float closest = distance;
            bool gaussian = depthOfField != null && depthOfField.active && depthOfField.mode.value == DepthOfFieldMode.Gaussian;
            float blurFrom = depthOfField != null ? depthOfField.gaussianStart.value : 0;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-close"));
            HomeView();
            yield return WaitForGlide();
            holdTurntable = false;
            idleSeconds = 0;
            report.AppendLine($"  camera: the view cube's TOP, clicked, looked straight down: {Yes(straightDown)} (pitch {topPitch:F1}°); its front-top-right corner " +
                              $"from {cornerPitch:F1}° up (35.3° expected); a middle-button drag moved the pivot {moved * 100:F1} cm, a very long one stopped " +
                              $"{Mathf.Max(0, beyond) * 100:F1} cm beside the robot (12 cm allowed); a double-click on the Uno made it the pivot, {pivotGap:F1} mm from it; " +
                              $"the wheel stopped at {closest * 100:F0} cm with only what lies behind the robot blurred (from {blurFrom * 100:F0} cm): {Yes(gaussian && blurFrom > closest)}; " +
                              "screenshots -garage-top, -garage-close");
        }

        /// <summary>A drag with the middle (or right) button, as the camera sees it.</summary>
        static List<PointerFrame> ViewDragFrames(Vector2 from, Vector2 to, bool middleButton)
        {
            var frames = new List<PointerFrame> { new PointerFrame { Position = from } };
            frames.Add(middleButton
                ? new PointerFrame { Position = from, MiddlePressed = true, MiddleHeld = true }
                : new PointerFrame { Position = from, RightPressed = true, RightHeld = true });
            for (int i = 1; i <= 10; i++)
            {
                var at = Vector2.Lerp(from, to, i / 10f);
                frames.Add(middleButton ? new PointerFrame { Position = at, MiddleHeld = true } : new PointerFrame { Position = at, RightHeld = true });
            }
            frames.Add(new PointerFrame { Position = to });
            return frames;
        }

        /// <summary>
        /// Wire and the Studio: the turntable put away with the robot on the mat, and in the Studio the view from
        /// below the bench (the room left out of the picture).
        /// </summary>
        IEnumerator StudioViewCheck()
        {
            var report = SpikeReport.Text;
            bool putAway = true;
            foreach (Transform child in turntable)
                if (child != robotAnchor && child.gameObject.activeSelf) putAway = false;
            bool onMat = Mathf.Abs(robotAnchor.position.y - turntable.position.y) < 0.0005f;
            viewCube!.Pick(new Vector3Int(0, -1, 0));
            yield return WaitForGlide();
            yield return Frames(2);
            bool below = pitch < -89.5f && view.transform.position.y < robotAnchor.position.y && (view.cullingMask & (1 << LabLayer)) == 0;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-studio-below"));
            HomeView();
            yield return WaitForGlide();
            bool roomBack = (view.cullingMask & (1 << LabLayer)) != 0;
            report.AppendLine($"  in the Studio the turntable is put away: {Yes(putAway)}, the robot stands on the mat: {Yes(onMat)}; the cube's BOTTOM looked up " +
                              $"from under the bench with the room left out: {Yes(below)}, and Home brought the room back: {Yes(roomBack)}; screenshot -garage-studio-below");
        }

        /// <summary>The kit's sixteen wires laid again from nothing: how many found a way, and how long it took.</summary>
        void WireRoutingReport()
        {
            RobotVisuals.ClearRoutes();
            ShowRobot();
            if (shown == null) return;
            SpikeReport.Text.AppendLine($"  wires laid round the parts and the plates (WireRouter): {shown.RoutedWires} of {Design.Wires.Count}, " +
                                        $"{shown.PlainWires} left as plain arches; {shown.RouteMs:F0} ms for all of them");
        }

        /// <summary>
        /// The Code window, by keys as a player types them: Ctrl+A and Ctrl+C copy the sketch, Ctrl+End and Ctrl+V
        /// paste it at the end, Ctrl+Z takes that back, Ctrl+X with nothing selected cuts a line, a typed letter
        /// replaces a selection. The clipboard is given back afterwards.
        /// </summary>
        IEnumerator CodeEditingCheck()
        {
            var report = SpikeReport.Text;
            var code = editor;
            if (code == null) yield break;
            string saved = GUIUtility.systemCopyBuffer;
            string original = code.Text;
            code.Focus();
            yield return null;
            yield return Key(code, KeyCode.A, ctrl: true);
            yield return Key(code, KeyCode.C, ctrl: true);
            bool copied = GUIUtility.systemCopyBuffer == original;
            yield return Key(code, KeyCode.End, ctrl: true);
            yield return Key(code, KeyCode.V, ctrl: true);
            bool pasted = code.Text == original + original;
            yield return Key(code, KeyCode.Z, ctrl: true);
            bool undone = code.Text == original;
            code.MoveCaret(0, 0);
            yield return Key(code, KeyCode.X, ctrl: true);
            string firstLine = original.Split('\n')[0];
            bool cutLine = GUIUtility.systemCopyBuffer == firstLine + "\n" && code.Text == original.Substring(firstLine.Length + 1);
            yield return Key(code, KeyCode.Z, ctrl: true);
            int row = System.Array.FindIndex(original.Split('\n'), line => line.Length >= 4);
            code.Select(row, 0, row, 4);
            yield return Typed(code, 'q');
            string[] typed = code.Text.Split('\n');
            bool replaced = row >= 0 && typed[row].StartsWith("q") && code.Text.Length == original.Length - 3;
            yield return Key(code, KeyCode.Z, ctrl: true);
            bool restored = code.Text == original;
            // A picture of a selection with the right-click menu over it.
            code.Select(2, 0, 6, 0);
            code.ShowMenu(code.LocalToWorld(new Vector2(260, 5.5f * CodeEditor.LineHeight)));
            yield return Frames(3);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-code-edit"));
            code.HideMenu();
            GUIUtility.systemCopyBuffer = saved;
            code.MoveCaret(0, 0);
            report.AppendLine($"  code editor, by keys: Ctrl+A and Ctrl+C copied the {original.Length}-character sketch: {Yes(copied)}; Ctrl+V pasted it at the end: {Yes(pasted)}; " +
                              $"Ctrl+Z took it back: {Yes(undone)}; Ctrl+X with nothing selected cut the first line: {Yes(cutLine)}; a typed letter replaced a selection: {Yes(replaced)}; " +
                              $"everything undone: {Yes(restored)}; screenshot -garage-code-edit");
        }

        static IEnumerator Key(VisualElement target, KeyCode key, bool ctrl = false, bool shift = false)
        {
            var modifiers = (ctrl ? EventModifiers.Control : EventModifiers.None) | (shift ? EventModifiers.Shift : EventModifiers.None);
            using (var e = KeyDownEvent.GetPooled('\0', key, modifiers)) target.SendEvent(e);
            yield return null;
        }

        static IEnumerator Typed(VisualElement target, char c)
        {
            using (var e = KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None)) target.SendEvent(e);
            yield return null;
        }
    }
}
