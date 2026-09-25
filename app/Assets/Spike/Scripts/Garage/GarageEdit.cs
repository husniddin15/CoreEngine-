using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The Garage's editing modes (docs/03 §5–7, docs/08, ADR-0009). The Body Studio (GarageStudio.cs) is where
    /// the robot is built: its body from shapes and its real parts placed on it. Wire joins pins with jumper
    /// wires and checks the circuit as it grows. Every change goes into the robot's <see cref="RobotDesign"/>,
    /// which the arena turns into the physics robot and its circuit.
    /// </summary>
    public sealed partial class GarageSpike
    {
        enum EditMode { None, Wire, Body }

        static readonly Vector3 DefaultTarget = new Vector3(0, 0.08f, 0);
        static readonly string[] Palette = { "auto", "red", "black", "yellow", "green", "blue", "white", "orange", "purple", "grey", "brown" };
        static readonly string[] SignalColours = { "yellow", "green", "blue", "orange", "white", "purple", "grey", "brown" };

        EditMode mode;
        Vector3 orbitTarget = DefaultTarget;
        Label hint = null!, tooltip = null!;

        // Selection and the wire being drawn
        int selectedWire = -1;
        string? wireStart, hoveredPin;
        string wireColour = "auto";
        LineRenderer? wirePreview;
        Material? previewMaterial;

        // Mouse and keys of this frame. The benchmark queues scripted frames, which go through the same code.
        struct PointerFrame
        {
            public Vector2 Position;
            public bool LeftPressed, LeftHeld, RightPressed, RightHeld, MiddlePressed, MiddleHeld;
            public float Scroll;
            public KeyCode Key;
            public bool Ctrl, Shift, Alt, Scripted;
            public bool Double; // a scripted press that is the second of a double-click
        }

        readonly Queue<PointerFrame> scriptedInput = new Queue<PointerFrame>();
        PointerFrame input;
        Vector2 pressPosition;
        bool leftDown;
        bool wireGesture, wireStartedByPress; // a press on a pin: a drag to another pin makes a wire, a click starts one

        // "Look at" a part in Wire: the camera comes close and the pins show their names
        string? focusedPart;
        readonly List<Label> pinTags = new List<Label>();
        string listFrom = "", listTo = "";

        // Undo and redo, saving and the body rebuild, which runs on a worker thread so the sliders stay smooth
        readonly List<RobotDesign> undo = new List<RobotDesign>();
        readonly List<RobotDesign> redo = new List<RobotDesign>();
        float saveAt = -1, bodyRebuildAt;
        bool bodyDirty;
        Task<BodyData>? bodyTask;
        int designEpoch, bodyTaskEpoch; // a build started before an undo must not replace the model after it
        int bodyBuildsShown;
        string lastExport = "";

        static RobotDesign Design => Robot.Design;

        void BuildEditUi()
        {
            tooltip = Classed(new Label { pickingMode = PickingMode.Ignore }, "edit-tooltip");
            tooltip.style.display = DisplayStyle.None;
            root.Add(tooltip);
            BuildWireOverlay();
        }

        void UpdateHint() => hint.text = Tr(mode switch
        {
            EditMode.Wire => gluing ? "wire.gluingHint" : ChosenWire != null ? "garage.hint.wireShape" : "garage.hint.wire",
            EditMode.Body => "garage.hint.body",
            _ => "garage.hint",
        });

        // ------------------------------------------------------------------ entering and leaving

        void EnterMode(EditMode next, string titleKey, Action render, LibraryTab tab = LibraryTab.Shapes)
        {
            if (mode != next) LeaveMode(show: false);
            mode = next;
            // The turntable is put away: the robot stands on the mat, facing the way the deck frame does.
            turntable.localRotation = Quaternion.identity;
            ShowTurntable(false);
            // Wire comes closer to the small pins; the Body Studio keeps some room around the robot for new shapes.
            SetView(200f, 42f, next == EditMode.Wire ? 0.40f : next == EditMode.Body ? 0.52f : 0.50f, DefaultTarget);
            selectedWire = -1;
            ResetPointState();
            wireStart = null;
            focusedPart = null;
            if (next == EditMode.Body) OpenStudio(tab);
            PlaceViewTools();
            ShowRobot();
            OpenSide(titleKey, render);
            UpdateHint();
        }

        /// <summary>Back to the showroom: saves, rebuilds the plain model and the robot's thumbnail.</summary>
        void LeaveMode(bool show = true)
        {
            if (mode == EditMode.None) return;
            if (mode == EditMode.Body) CloseStudio();
            bodyTask?.Wait(); // the next model is built from the final design anyway
            bodyTask = null;
            bodyDirty = false;
            mode = EditMode.None;
            selectedWire = -1;
            ResetPointState();
            wireStart = null;
            hoveredPin = null;
            tooltip.style.display = DisplayStyle.None;
            if (wirePreview != null) wirePreview.enabled = false;
            ShowTurntable(true);
            PlaceViewTools();
            SetView(yaw, 14f, 0.62f, DefaultTarget);
            idleSeconds = 0;
            saveAt = -1;
            GarageState.Save();
            RefreshCard();
            StartCoroutine(RenderThumbnail(GarageState.Selected));
            UpdateHint();
            if (show) ShowRobot();
        }

        /// <summary>Another robot was chosen while editing: its own selection and undo history start fresh.</summary>
        void ResetEditState()
        {
            selectedWire = -1;
            ResetPointState();
            wireStart = null;
            hoveredPin = null;
            undo.Clear();
            redo.Clear();
            CancelCarry();
            CancelDrawing();
            selection.Clear();
        }

        // ------------------------------------------------------------------ changes, undo, saving

        void PushUndo() => RecordUndo(Design.Clone());

        /// <summary>Keeps the design as it was before a change; a new change forgets what could be redone.</summary>
        void RecordUndo(RobotDesign before)
        {
            undo.Add(before);
            if (undo.Count > 60) undo.RemoveAt(0);
            redo.Clear();
        }

        void Undo()
        {
            if (undo.Count == 0)
            {
                ShowToast(Tr("edit.nothingToUndo"));
                return;
            }
            redo.Add(Design.Clone());
            Robot.Design = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            AfterHistoryStep();
        }

        void Redo()
        {
            if (redo.Count == 0)
            {
                ShowToast(Tr("edit.nothingToRedo"));
                return;
            }
            undo.Add(Design.Clone());
            Robot.Design = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            AfterHistoryStep();
        }

        void AfterHistoryStep()
        {
            selection.RemoveAll(p => !Exists(p));
            selectedWire = -1;
            ResetPointState();
            wireStart = null;
            DesignChanged();
            UpdateHint();
        }

        /// <summary>
        /// After any change: new model, card and panel; the save follows shortly. In the Body Studio the model is
        /// rebuilt on the worker thread, so an undo does not stop the frame for Manifold.
        /// </summary>
        void DesignChanged()
        {
            designEpoch++;
            Design.DropLooseGlue(); // glue on a part or shape that is gone goes with it
            if (mode == EditMode.Body)
            {
                bodyDirty = true;
                FlushBody();
                RefreshStudioChrome();
            }
            else
            {
                ShowRobot();
            }
            RefreshCard();
            RefreshBar();
            renderSide?.Invoke();
            saveAt = Time.unscaledTime + 0.5f;
        }

        /// <summary>Called every frame: finished body builds, new ones, and the delayed save.</summary>
        void UpdateEditFrame()
        {
            if (bodyTask != null && bodyTask.IsCompleted)
            {
                var task = bodyTask;
                bodyTask = null;
                if (task.IsFaulted) Debug.LogWarning("Body Studio: " + task.Exception?.GetBaseException().Message);
                if (mode != EditMode.None && bodyTaskEpoch == designEpoch)
                {
                    ShowRobot(task.IsFaulted ? null : BodyBuilder.ToMeshes(task.Result));
                    bodyBuildsShown++;
                    RefreshCard();
                }
            }
            if (bodyDirty && Time.unscaledTime >= bodyRebuildAt) FlushBody();
            UpdateStudioScene();
            UpdateBalance();
            UpdatePinTags();
            UpdateWireHandles();
            if (saveAt > 0 && Time.unscaledTime >= saveAt)
            {
                saveAt = -1;
                GarageState.Save();
            }
        }

        /// <summary>
        /// Starts building the plates for the current body on a worker thread (one build at a time). The model
        /// switches to them when they are ready; slider moves meanwhile start the next build.
        /// </summary>
        void FlushBody()
        {
            if (!bodyDirty || bodyTask != null) return;
            bodyDirty = false;
            bodyRebuildAt = Time.unscaledTime + 0.03f;
            bodyTaskEpoch = designEpoch;
            var snapshot = Design.Body.Clone();
            string imports = Robot.ImportFolder; // Unity's paths are read on the main thread
            bodyTask = Task.Run(() => BodyBuilder.BuildData(snapshot, imports));
        }

        /// <summary>For the benchmark: until the model shows the current body.</summary>
        IEnumerator WaitForBody()
        {
            while (bodyDirty || bodyTask != null) yield return null;
        }

        // ------------------------------------------------------------------ mouse and keys

        void UpdateEditing()
        {
            input = ReadInput();
            if (overlay.style.display == DisplayStyle.Flex) return;
            Vector2 mouse = input.Position;
            bool overUi = IsPointerOverUi(mouse);

            // A click in the scene ends typing in a number box, so the keys work on the robot again.
            if ((input.LeftPressed || input.RightPressed) && !overUi) root.panel?.focusController?.focusedElement?.Blur();

            // Left button: parts are dragged in Build; in Wire a press on a pin starts a wire; a click picks;
            // a drag anywhere else turns the view.
            if (input.LeftPressed && !overUi)
            {
                leftDown = true;
                pressPosition = mouse;
                lastMouse = mouse;
                // With Glue on, a click glues and pins are left alone; a chosen wire's points and the wire itself
                // come before pins, so it can be shaped where it runs over a header.
                if (mode == EditMode.Wire && !gluing && !BeginPointPress(mouse)) BeginWireGesture(mouse);
                if (mode == EditMode.Body) BeginStudioPress(mouse);
            }
            if (leftDown && input.LeftHeld)
            {
                if (drag.grip != Grip.None) UpdateStudioDrag(mouse);
                else if (pointGrip != PointGrip.None) UpdatePointDrag(mouse);
                else if (!wireGesture && (mouse - pressPosition).magnitude > 4) DragView(mouse);
            }
            if (leftDown && !input.LeftHeld)
            {
                leftDown = false;
                bool click = (mouse - pressPosition).magnitude <= 4;
                if (drag.grip != Grip.None) EndStudioDrag();
                else if (pointGrip != PointGrip.None) EndPointDrag();
                else if (wireGesture) EndWireGesture(mouse, click);
                else if (click && (input.Scripted ? input.Double : DoubleClick(mouse))) PivotAt(mouse); // the first click picked already
                else if (click && gluing && mode == EditMode.Wire) GlueAt(mouse);
                else if (click) SceneClick(mouse);
            }

            // The right button turns the view (with Shift moves it), the middle button moves it, and the wheel
            // zooms toward the spot under the mouse, so small pins can be reached.
            CameraControls(overUi, leftTurns: false);

            UpdateHover(mouse, overUi);
            if (!IsTyping()) UpdateKeys();
        }

        PointerFrame ReadInput()
        {
            if (scriptedInput.Count > 0)
            {
                var frame = scriptedInput.Dequeue();
                frame.Scripted = true;
                return frame;
            }
            return new PointerFrame
            {
                Position = Input.mousePosition,
                LeftPressed = Input.GetMouseButtonDown(0),
                LeftHeld = Input.GetMouseButton(0),
                RightPressed = Input.GetMouseButtonDown(1),
                RightHeld = Input.GetMouseButton(1),
                MiddlePressed = Input.GetMouseButtonDown(2),
                MiddleHeld = Input.GetMouseButton(2),
                Scroll = Input.mouseScrollDelta.y,
                Ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                Shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                Alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
            };
        }

        bool KeyPressed(KeyCode key) => input.Scripted ? input.Key == key : Input.GetKeyDown(key);

        bool IsTyping() => root.panel?.focusController?.focusedElement != null;

        void UpdateKeys()
        {
            if (mode == EditMode.Body && StudioKeys()) return;
            if (input.Ctrl && KeyPressed(KeyCode.Z)) Undo();
            if (input.Ctrl && KeyPressed(KeyCode.Y)) Redo();
            bool delete = KeyPressed(KeyCode.Delete) || KeyPressed(KeyCode.Backspace);
            if (mode == EditMode.Wire && PointKeys(delete)) return;
            if (KeyPressed(KeyCode.Escape))
            {
                if (wireStart != null) CancelWire();
                else if (selectedWire >= 0) SelectNothing();
                else CloseSide();
                return;
            }
            if (mode == EditMode.Wire && delete && selectedWire >= 0) RemoveWire(selectedWire);
        }

        void SceneClick(Vector2 mouse)
        {
            if (mode == EditMode.Body)
            {
                StudioClick(mouse);
                return;
            }
            if (mode != EditMode.Wire) return;
            int wire = WireUnder(mouse);
            if (wire >= 0) SelectWire(wire);
            else if (wireStart != null) CancelWire();
            else SelectNothing();
        }

        /// <summary>A press on a pin: a new wire starts there unless one is already waiting for its other end.</summary>
        void BeginWireGesture(Vector2 mouse)
        {
            string? pin = WireModeTarget(mouse).pin;
            wireGesture = pin != null;
            wireStartedByPress = false;
            if (pin == null || wireStart != null) return;
            wireStartedByPress = StartWire(pin);
            if (!wireStartedByPress) wireGesture = false; // a full pin: the toast says why
        }

        /// <summary>
        /// The release: dragged onto another pin, the wire is made; a click on a pin starts a wire or, when one
        /// is waiting, ends it there; a click on the same pin again or a release on nothing cancels it.
        /// </summary>
        void EndWireGesture(Vector2 mouse, bool click)
        {
            wireGesture = false;
            string? target = PinUnder(mouse);
            if (click)
            {
                if (wireStartedByPress) return; // the first click: the wire waits for its other end
                if (target == null || target == wireStart) CancelWire();
                else CompleteWire(target);
                return;
            }
            bool made = target != null && target != wireStart && CompleteWire(target);
            if (!made && wireStart != null) CancelWire();
        }

        void SelectNothing()
        {
            selectedWire = -1;
            ResetPointState();
            UpdateHint();
            shown?.Highlight(null);
            shown?.HighlightWire(-1);
            renderSide?.Invoke();
        }

        /// <summary>The label that follows the mouse: part names in Build, pins and wires in Wire.</summary>
        void UpdateHover(Vector2 mouse, bool overUi)
        {
            if (mode == EditMode.Body)
            {
                UpdateStudioHover(mouse, overUi);
                return;
            }
            string text = "";
            hoveredPin = null;
            if (!overUi && (!leftDown || wireGesture) && viewDrag == ViewDrag.None)
            {
                if (mode == EditMode.Wire && gluing)
                {
                    text = GlueHoverText(mouse);
                }
                else if (mode == EditMode.Wire)
                {
                    // While a wire is being dragged only pins count: it is dropped on one.
                    var (pin, wire) = wireGesture ? (PinUnder(mouse), -1) : WireModeTarget(mouse);
                    hoveredPin = pin;
                    if (pin != null) text = PinDescription(pin);
                    else if (wire >= 0) text = WireText(Design.Wires[wire]);
                }
            }
            shown?.HighlightPins(hoveredPin, wireStart);
            UpdateWirePreview(mouse);
            if (text.Length == 0 || root.panel == null)
            {
                tooltip.style.display = DisplayStyle.None;
                return;
            }
            var point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
            tooltip.text = text;
            tooltip.style.left = point.x + 16;
            tooltip.style.top = point.y + 12;
            tooltip.style.display = DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ Wire

        /// <summary>
        /// What the mouse points at in Wire: the nearer of a pin and a wire. A pin wins when it is about as
        /// close (2 pixels), so a wire that passes over a row of pins can still be clicked where it runs.
        /// </summary>
        (string? pin, int wire) WireModeTarget(Vector2 mouse)
        {
            string? pin = PinUnder(mouse, out float pinDistance);
            int wire = WireUnder(mouse, out float wireDistance);
            return pin != null && (wire < 0 || pinDistance <= wireDistance + 2) ? (pin, -1) : (null, wire);
        }

        string? PinUnder(Vector2 mouse) => PinUnder(mouse, out _);

        /// <summary>The nearest pin marker within 14 pixels of the mouse, as "part/pin".</summary>
        string? PinUnder(Vector2 mouse, out float distance)
        {
            distance = float.MaxValue;
            if (shown == null) return null;
            string? best = null;
            float bestDistance = 14f;
            foreach (var entry in shown.PinMarkers)
            {
                var screen = view.WorldToScreenPoint(entry.Value.position);
                if (screen.z <= 0) continue;
                float d = Vector2.Distance(mouse, screen);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = entry.Key;
                }
            }
            if (best != null) distance = bestDistance;
            return best;
        }

        int WireUnder(Vector2 mouse) => WireUnder(mouse, out _);

        /// <summary>The wire whose curve passes within 7 pixels of the mouse, or -1.</summary>
        int WireUnder(Vector2 mouse, out float distance)
        {
            distance = float.MaxValue;
            if (shown == null) return -1;
            int best = -1;
            float bestDistance = 7f;
            var frame = shown.Root.transform;
            for (int i = 0; i < shown.WirePaths.Count; i++)
            {
                var path = shown.WirePaths[i];
                if (path == null) continue;
                Vector3 previous = view.WorldToScreenPoint(frame.TransformPoint(path[0]));
                for (int k = 1; k < path.Length; k++)
                {
                    Vector3 next = view.WorldToScreenPoint(frame.TransformPoint(path[k]));
                    if (previous.z > 0 && next.z > 0)
                    {
                        float d = DistanceToSegment(mouse, previous, next);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = i;
                        }
                    }
                    previous = next;
                }
            }
            if (best >= 0) distance = bestDistance;
            return best;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + t * ab);
        }

        /// <summary>One click on a pin as the mouse makes it: the first starts a wire, the second ends it.</summary>
        void PinClicked(string key)
        {
            if (wireStart == null) StartWire(key);
            else if (wireStart == key) CancelWire();
            else CompleteWire(key);
        }

        /// <summary>Makes a wire between two pins in one go (the lists in the Wire panel, the benchmark).</summary>
        bool ConnectPins(string from, string to)
        {
            wireStart = null;
            if (!StartWire(from)) return false;
            if (CompleteWire(to)) return true;
            CancelWire();
            return false;
        }

        /// <summary>Chooses where a new wire starts, if the pin has room for one more.</summary>
        bool StartWire(string key)
        {
            var (part, pin) = SplitKey(key);
            if (!Design.HasRoomOn(part, pin))
            {
                ShowToast(SpikeStrings.Format("wire.full", PartLabel(part) + " " + PinLabel(part, pin)));
                return false;
            }
            wireStart = key;
            selectedWire = -1;
            ResetPointState();
            shown?.HighlightWire(-1);
            UpdateHint();
            renderSide?.Invoke();
            return true;
        }

        /// <summary>Ends the waiting wire on a pin. A full pin is refused and the wire keeps waiting.</summary>
        bool CompleteWire(string key)
        {
            if (wireStart == null) return false;
            var (fromPart, fromPin) = SplitKey(wireStart);
            var (toPart, toPin) = SplitKey(key);
            if (!Design.HasRoomOn(toPart, toPin))
            {
                ShowToast(SpikeStrings.Format("wire.full", PartLabel(toPart) + " " + PinLabel(toPart, toPin)));
                return false;
            }
            PushUndo();
            if (Design.AddWire(fromPart, fromPin, toPart, toPin, ColourFor(fromPart, fromPin, toPart, toPin)) == null)
            {
                undo.RemoveAt(undo.Count - 1);
                ShowToast(Tr("wire.duplicate"));
                return false;
            }
            wireStart = null;
            selectedWire = Design.Wires.Count - 1;
            ResetPointState();
            DesignChanged();
            shown?.HighlightWire(selectedWire);
            UpdateHint();
            return true;
        }

        void CancelWire()
        {
            wireStart = null;
            shown?.HighlightPins(hoveredPin, null);
            renderSide?.Invoke();
        }

        /// <summary>
        /// Points the camera at a part from the side its pins face (from above for headers, from behind for the
        /// sensor's pins), close enough to tell 2.54 mm pins apart, and shows the pins' names.
        /// </summary>
        void FocusPart(string partId)
        {
            if (shown == null) return;
            focusedPart = partId;
            var facing = Vector3.zero;
            foreach (var entry in shown.PinMarkers)
            {
                if (!entry.Key.StartsWith(partId + "/")) continue;
                var (part, pin) = SplitKey(entry.Key);
                var exit = DesignGeometry.PinExit(Design, part, pin);
                if (exit != null) facing += robotAnchor.TransformDirection(new Vector3(exit.Value.x, exit.Value.y, exit.Value.z));
            }
            float toYaw = yaw, toPitch = pitch;
            if (facing.sqrMagnitude > 1e-6f)
            {
                facing.Normalize();
                if (new Vector2(facing.x, facing.z).magnitude > 0.2f) toYaw = Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg;
                toPitch = Mathf.Clamp(Mathf.Asin(facing.y) * Mathf.Rad2Deg, 30f, 75f);
            }
            GlideTo(toYaw, toPitch, 0.2f, shown.FocusPoint(partId));
            renderSide?.Invoke();
        }

        void ViewWholeRobot()
        {
            focusedPart = null;
            GlideTo(200f, 42f, mode == EditMode.Wire ? 0.40f : 0.50f, DefaultTarget);
            renderSide?.Invoke();
        }

        /// <summary>Small name tags on the pins of the part being looked at, like the printing on a real board.</summary>
        void UpdatePinTags()
        {
            int used = 0;
            var part = mode == EditMode.Wire && focusedPart != null ? Design.Find(focusedPart) : null;
            var def = part == null ? null : PartCatalog.Get(part.Part);
            if (def != null && shown != null && root.panel != null && shown.Parts.TryGetValue(part!.Id, out var partObject))
            {
                var frame = partObject.transform;
                var centre = ToPanel(view.WorldToScreenPoint(frame.position));
                foreach (var pin in def.Pins)
                {
                    if (!shown.PinMarkers.TryGetValue(part.Id + "/" + pin.Id, out var marker)) continue;
                    var screen = view.WorldToScreenPoint(marker.position);
                    if (screen.z <= 0) continue;
                    var at = ToPanel(screen);
                    // Header pins sit 2.54 mm apart in a row, so their names run across the row, away from the
                    // part; a terminal's or a lead's name runs the way its wire leaves.
                    bool header = pin.Style == PinStyle.Header || pin.Style == PinStyle.Pin;
                    var world = header ? frame.right : frame.TransformDirection(new Vector3(pin.ExitX, pin.ExitY, pin.ExitZ));
                    var step = ToPanel(view.WorldToScreenPoint(marker.position + world * 0.003f)) - at;
                    var along = header ? new Vector2(-step.y, step.x) : step;
                    along = along.sqrMagnitude > 1e-4f ? along.normalized : new Vector2(0, -1);
                    if (header && Vector2.Dot(along, at - centre) < 0) along = -along;
                    float angle = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                    bool flip = Mathf.Abs(angle) > 90; // keep the text upright: it then ends at the pin's side
                    if (used == pinTags.Count)
                    {
                        var label = Classed(new Label { pickingMode = PickingMode.Ignore }, "pin-tag");
                        root.Insert(root.IndexOf(tooltip), label);
                        pinTags.Add(label);
                    }
                    var tag = pinTags[used++];
                    tag.text = pin.ShortLabel;
                    tag.EnableInClassList("pin-tag--flip", flip);
                    tag.style.left = at.x + along.x * 6;
                    tag.style.top = at.y + along.y * 6;
                    tag.style.rotate = new Rotate(new Angle(flip ? angle - 180 : angle, AngleUnit.Degree));
                    tag.style.display = DisplayStyle.Flex;
                }
            }
            for (int i = used; i < pinTags.Count; i++) pinTags[i].style.display = DisplayStyle.None;
        }

        Vector2 ToPanel(Vector3 screen) => RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));

        /// <summary>Every pin as "Uno · D5" with its "part/pin" key; a dot marks pins that have no room left.</summary>
        static (List<string> labels, List<string> keys) PinChoices(RobotDesign design)
        {
            var labels = new List<string>();
            var keys = new List<string>();
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                foreach (var pin in def.Pins)
                {
                    labels.Add(PartLabel(part.Id) + " · " + pin.ShortLabel + (design.HasRoomOn(part.Id, pin.Id) ? "" : "  ●"));
                    keys.Add(part.Id + "/" + pin.Id);
                }
            }
            return (labels, keys);
        }

        static (string part, string pin) SplitKey(string key)
        {
            int slash = key.IndexOf('/');
            return (key.Substring(0, slash), key.Substring(slash + 1));
        }

        /// <summary>
        /// The chosen colour, or on Auto the maker's habit: red for supply, black for ground, the motor's own
        /// red and black leads, and a colour not used yet for each signal.
        /// </summary>
        string ColourFor(string fromPart, string fromPin, string toPart, string toPin)
        {
            if (wireColour != "auto") return wireColour;
            var a = PinOf(fromPart, fromPin);
            var b = PinOf(toPart, toPin);
            if (a?.Kind == PinKind.Ground || b?.Kind == PinKind.Ground || fromPin == "M-" || toPin == "M-") return "black";
            if (a?.Kind == PinKind.Power || b?.Kind == PinKind.Power || fromPin == "M+" || toPin == "M+") return "red";
            foreach (string colour in SignalColours)
                if (!Design.Wires.Exists(w => w.Color == colour)) return colour;
            return SignalColours[Design.Wires.Count % SignalColours.Length];
        }

        static PinDef? PinOf(string partId, string pinId)
        {
            var part = Design.Find(partId);
            return part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
        }

        void SelectWire(int index)
        {
            if (index != selectedWire) ResetPointState();
            selectedWire = index;
            wireStart = null;
            shown?.HighlightWire(index);
            UpdateHint();
            renderSide?.Invoke();
        }

        void RemoveWire(int index)
        {
            if (index < 0 || index >= Design.Wires.Count) return;
            PushUndo();
            Design.Wires.RemoveAt(index);
            selectedWire = -1;
            ResetPointState();
            UpdateHint();
            DesignChanged();
        }

        void UpdateWirePreview(Vector2 mouse)
        {
            bool show = mode == EditMode.Wire && wireStart != null && shown != null && shown.PinMarkers.ContainsKey(wireStart);
            if (!show)
            {
                if (wirePreview != null) wirePreview.enabled = false;
                return;
            }
            if (wirePreview == null)
            {
                previewMaterial = new Material(litMaterial);
                wirePreview = new GameObject("WirePreview").AddComponent<LineRenderer>();
                wirePreview.sharedMaterial = previewMaterial;
                wirePreview.positionCount = 2;
                wirePreview.widthMultiplier = 0.0016f;
                wirePreview.generateLightingData = true;
                wirePreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var (fromPart, fromPin) = SplitKey(wireStart!);
            string colour = ColourFor(fromPart, fromPin, fromPart, fromPin);
            previewMaterial!.SetColor("_BaseColor", RobotVisuals.WireColors.TryGetValue(colour, out var c) ? c : Color.yellow);
            var from = shown!.PinMarkers[wireStart!].position;
            Vector3 to;
            if (hoveredPin != null && shown.PinMarkers.TryGetValue(hoveredPin, out var target))
            {
                to = target.position;
            }
            else
            {
                var ray = view.ScreenPointToRay(mouse);
                to = new Plane(Vector3.up, from).Raycast(ray, out float enter) && enter < 5f ? ray.GetPoint(enter) : from;
            }
            wirePreview.enabled = true;
            wirePreview.SetPosition(0, from);
            wirePreview.SetPosition(1, to);
        }

        /// <summary>A short name for lists: "Uno", "L298N", "Left TT"…</summary>
        static string PartLabel(string partId)
        {
            var part = Design.Find(partId);
            var def = part == null ? null : PartCatalog.Get(part.Part);
            if (part == null || def == null) return partId;
            return def.Kind switch
            {
                PartKind.Board => "Uno",
                PartKind.MotorDriver => Design.Count(def.Id) > 1 ? "L298N " + part.Id.Replace("driver", "") : "L298N",
                PartKind.Ultrasonic => "HC-SR04",
                PartKind.Motor => Tr(DesignGeometry.SideOf(part) == "right" ? "side.right" : "side.left") + " TT", // where its wheel is
                PartKind.Battery => "4×AA",
                PartKind.Servo => Design.Count(def.Id) > 1 ? "SG90 " + part.Id.Replace("servo", "") : "SG90",
                PartKind.Led => Design.Count(def.Id) > 1 ? "LED " + part.Id.Replace("led", "") : "LED",
                _ => def.Name,
            };
        }

        /// <summary>A pin's printed name: "D9", "+12V", "GND"… (the part of its label before the first " · ").</summary>
        static string PinLabel(string partId, string pinId)
        {
            string label = PinOf(partId, pinId)?.Label ?? pinId;
            int dot = label.IndexOf(" · ", StringComparison.Ordinal);
            return dot > 0 ? label.Substring(0, dot) : label;
        }

        static string WireText(WireInstance w) =>
            SpikeStrings.Format("wire.length", PartLabel(w.FromPart) + " " + PinLabel(w.FromPart, w.FromPin),
                PartLabel(w.ToPart) + " " + PinLabel(w.ToPart, w.ToPin), DesignGeometry.WireLength(Design, w));

        /// <summary>The hover text of a pin: the part, the pin's full label and what it is wired to.</summary>
        static string PinDescription(string key)
        {
            var (partId, pinId) = SplitKey(key);
            var part = Design.Find(partId);
            string text = (PartCatalog.Get(part?.Part ?? "")?.Name ?? partId) + " · " + (PinOf(partId, pinId)?.Label ?? pinId);
            foreach (var w in Design.Wires)
            {
                if (w.FromPart == partId && w.FromPin == pinId) text += "\n→ " + PartLabel(w.ToPart) + " " + PinLabel(w.ToPart, w.ToPin);
                else if (w.ToPart == partId && w.ToPin == pinId) text += "\n→ " + PartLabel(w.FromPart) + " " + PinLabel(w.FromPart, w.FromPin);
            }
            return text;
        }

        static string WarningText(CircuitWarning warning)
        {
            var args = new object[warning.Args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                string a = warning.Args[i];
                args[i] = a == "left" ? Tr("side.left") : a == "right" ? Tr("side.right") : Design.Find(a) != null ? PartLabel(a) : a;
            }
            return SpikeStrings.Format("chk." + warning.Code, args);
        }

        void RenderWire()
        {
            var design = Design;
            if (design.Parts.Count == 0)
            {
                Info("wire.noParts");
                return;
            }
            if (wireStart != null)
            {
                var (part, pin) = SplitKey(wireStart);
                sideContent.Add(Classed(new Label(SpikeStrings.Format("wire.from", PartLabel(part) + " " + PinLabel(part, pin))), "try-line"));
            }
            RenderWireShape(); // first, when a wire is chosen: what can be done with it

            Section("wire.lookAt", Icon.Eye);
            var look = Layout("seg-row");
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null || def.Pins.Count == 0) continue;
                string id = part.Id;
                var button = new Button(() => FocusPart(id)) { focusable = false };
                button.AddToClassList("seg-button");
                button.AddToClassList("look-button");
                button.EnableInClassList("seg-button--active", id == focusedPart);
                button.Add(PartImage(def.Id, PartIcon(def.Kind), "look-picture"));
                button.Add(new Label(PartLabel(id)));
                look.Add(button);
            }
            var whole = new Button(ViewWholeRobot) { focusable = false };
            whole.Add(Classed(new IconView(Icon.Frame), "look-picture"));
            whole.Add(new Label(Tr("wire.whole")));
            whole.AddToClassList("seg-button");
            whole.AddToClassList("look-button");
            whole.EnableInClassList("seg-button--active", focusedPart == null);
            look.Add(whole);
            sideContent.Add(look);

            Section("wire.byList", Icon.Wire);
            var (labels, keys) = PinChoices(design);
            if (!keys.Contains(listFrom)) listFrom = keys.Count > 0 ? keys[0] : "";
            if (!keys.Contains(listTo)) listTo = keys.Count > 1 ? keys[1] : listFrom;
            var fromField = new DropdownField(labels, Math.Max(0, keys.IndexOf(listFrom))) { focusable = false };
            var toField = new DropdownField(labels, Math.Max(0, keys.IndexOf(listTo))) { focusable = false };
            fromField.RegisterValueChangedCallback(_ => { if (fromField.index >= 0) listFrom = keys[fromField.index]; });
            toField.RegisterValueChangedCallback(_ => { if (toField.index >= 0) listTo = keys[toField.index]; });
            fromField.AddToClassList("pin-field");
            toField.AddToClassList("pin-field");
            sideContent.Add(fromField);
            sideContent.Add(Classed(new Label("↓"), "bin-sub"));
            sideContent.Add(toField);
            sideContent.Add(IconSmallButton(Icon.Plus, "wire.addFromList", () => ConnectPins(listFrom, listTo)));

            Section("wire.colour", Icon.Customize);
            var palette = Layout("palette");
            foreach (string colour in Palette)
            {
                string chosen = colour;
                var chip = new Button(() =>
                {
                    wireColour = chosen;
                    renderSide?.Invoke();
                }) { focusable = false, text = colour == "auto" ? "A" : "" };
                chip.AddToClassList("palette-chip");
                chip.EnableInClassList("palette-chip--active", colour == wireColour);
                if (colour != "auto") chip.style.backgroundColor = RobotVisuals.WireColors[colour];
                palette.Add(chip);
            }
            sideContent.Add(palette);
            if (wireColour == "auto") sideContent.Add(Classed(new Label(Tr("wire.auto")), "bin-sub"));

            Section("wire.check", Icon.Check);
            var circuit = CircuitAnalysis.Analyse(design);
            if (!circuit.HasProblems) sideContent.Add(Classed(new Label(Tr("wire.ok")), "ok-line"));
            foreach (var warning in circuit.Warnings)
                sideContent.Add(Classed(new Label((warning.Info ? "" : "⚠ ") + WarningText(warning)), warning.Info ? "info-line" : "warn-line"));

            sideContent.Add(IconTitle(Icon.Wire, SpikeStrings.Format("wire.list", design.Wires.Count)));
            if (design.Wires.Count == 0) Info("wire.none");
            for (int i = 0; i < design.Wires.Count; i++)
            {
                int index = i;
                var wire = design.Wires[i];
                var row = new VisualElement();
                row.AddToClassList("wire-row");
                row.EnableInClassList("wire-row--selected", i == selectedWire);
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.target is not Button) SelectWire(index); // the ✕ button removes instead
                });
                var chip = Layout("wire-chip");
                chip.style.backgroundColor = RobotVisuals.WireColors.TryGetValue(wire.Color, out var c) ? c : Color.yellow;
                row.Add(chip);
                row.Add(Classed(new Label(WireText(wire)), "wire-text"));
                var remove = new Button(() => RemoveWire(index)) { text = "✕", focusable = false };
                remove.AddToClassList("wire-remove");
                row.Add(remove);
                sideContent.Add(row);
            }
            sideContent.Add(IconSmallButton(Icon.Undo, "edit.undo", Undo));
        }

        // ------------------------------------------------------------------ STL export

        static string ExportFolder()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return documents.Length > 0 ? Path.Combine(documents, "CoreEngine", "Exports") : Path.Combine(Application.persistentDataPath, "Exports");
        }

        /// <summary>Writes every plate of the current body as a binary STL (millimetres, z up) and returns its path.</summary>
        string ExportStl(string? folder)
        {
            folder ??= ExportFolder();
            try
            {
                var data = BodyBuilder.BuildData(Design.Body.Clone(), Robot.ImportFolder); // exactly the current body, whatever the model shows
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, SafeName(Robot.Name) + "-body.stl");
                using (var stream = File.Create(path))
                    StlWriter.Write(stream, data.StlPositions, data.StlTriangles, Robot.Name + " body");
                lastExport = path;
                ShowToast(SpikeStrings.Format("body.exported", path));
                renderSide?.Invoke();
                return path;
            }
            catch (Exception e)
            {
                ShowToast(e.Message);
                return "";
            }
        }

        void OpenExportFolder()
        {
            string folder = Path.GetDirectoryName(lastExport) ?? ExportFolder();
            if (Directory.Exists(folder)) Application.OpenURL(new Uri(folder).AbsoluteUri);
        }

        // ------------------------------------------------------------------ benchmark: a robot from scratch

        /// <summary>
        /// Builds a robot the way a player would, through the same mouse, key and UI Toolkit paths: a new robot is
        /// empty; in the Body Studio the kit's body is made from the library (blue acrylic plates with M3 holes,
        /// aluminium standoffs) and every part is carried from the library to its place with the mouse, in the order
        /// a real kit goes together: motors and the caster hang under the bottom plate, the battery stands on it,
        /// then the top deck, the boards on it and the sensor at its front edge. Every wire follows pin by pin in
        /// Wire, with two wiring mistakes checked and undone. START then drives this robot in the arena.
        /// </summary>
        IEnumerator BuildFromScratch()
        {
            var report = SpikeReport.Text;
            report.AppendLine("robot built from scratch in the Body Studio and Wire:");
            NewRobot();
            var robot = Robot;
            bool startsEmpty = Design.Body.Features.Count == 0 && Design.Parts.Count == 0;
            OnAction("act.body");
            yield return Frames(3);
            yield return HandlesByMouse();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // The bottom plate: blue acrylic from the picker, the plate tile, set down on the workplane, then
            // lifted to 35 mm, where the motors' tops will be.
            yield return ClickElement(MaterialButton(BodyMaterial.Acrylic));
            yield return ClickElement(SwatchButton("#2F6FD8"));
            yield return ClickElement(paletteButtons[0]);
            yield return CarryTo(new Vector3(0, 0, 0));
            var bottom = SelectedFeature!;
            bool onWorkplane = bottom.Kind == FeatureKind.Plate && Mathf.Abs(bottom.Y - bottom.SizeY / 2) < 0.01f && bottom.Material == BodyMaterial.Acrylic;
            yield return Type(1, 45f);
            yield return WaitForBody();
            yield return Frames(2);

            // Under the plate: two motors (the second turned round, so its wheel is on the right) and the caster;
            // on it, the battery holder. Each rides the mouse from its tile and lands where the mouse points.
            yield return ClickElement(tabButtons[LibraryTab.Parts]);
            yield return ClickElement(partButtons[PartCatalog.TtMotor]);
            yield return CarryTo(new Vector3(-50, 46.5f, -30));
            var left = SelectedPart!;
            yield return ClickElement(partButtons[PartCatalog.TtMotor]);
            yield return CarryTo(new Vector3(50, 46.5f, -30));
            var right = SelectedPart!;
            yield return Type(4, 180);
            yield return ClickElement(partButtons[PartCatalog.Caster]);
            yield return CarryTo(new Vector3(0, 46.5f, 65));
            yield return ClickElement(partButtons[PartCatalog.Battery4AA]);
            yield return CarryTo(new Vector3(0, 46.5f, -5));
            var battery = SelectedPart!;
            yield return Frames(2);
            bool partHandles = SizeHandleCount() == 0 && HandleScreen(Grip.Lift, 1, 1) != null && HandleScreen(Grip.Turn, 1, 1) != null;
            var leftWheel = DesignGeometry.WheelCentre(left);
            var rightWheel = DesignGeometry.WheelCentre(right);
            bool hung = Mathf.Abs(leftWheel.x + 77.5f) < 0.1f && Mathf.Abs(leftWheel.y - 32.5f) < 0.2f && Mathf.Abs(rightWheel.x - 77.5f) < 0.1f
                        && DesignGeometry.SideOf(right) == "right" && Mathf.Abs(DesignGeometry.LowestPoint(Design)) < 0.2f;
            bool onPlate = Mathf.Abs(battery.Y - 54f) < 0.2f;

            // The right motor's button puts its wheel on the other end of the shaft: under the robot, since this
            // motor is turned round; an undo puts it back outside.
            Select(new Pick(true, right.Id));
            yield return Frames(2);
            Button? wheelButton = null;
            sideContent.Query<Button>().ForEach(b => { if (b.Q<Label>()?.text == Tr("studio.wheelEnd")) wheelButton = b; });
            if (wheelButton != null) yield return ClickElement(wheelButton);
            float otherEndX = DesignGeometry.WheelCentre(Design.Find(right.Id)!).x;
            Undo();
            yield return WaitForBody();
            bool wheelEnd = wheelButton != null && Mathf.Abs(otherEndX - 22.5f) < 0.1f && Mathf.Abs(DesignGeometry.WheelCentre(Design.Find(right.Id)!).x - 77.5f) < 0.1f;
            right = Design.Find(right.Id)!;
            left = Design.Find(left.Id)!;
            battery = Design.Find(battery.Id)!;

            // Four aluminium standoffs and the top deck, typed in where the kit has them.
            yield return ClickElement(tabButtons[LibraryTab.Shapes]);
            yield return ClickElement(MaterialButton(BodyMaterial.Aluminium));
            foreach (var (x, z) in new[] { (-50f, -70f), (50f, -70f), (-50f, 70f), (50f, 70f) })
            {
                yield return ClickElement(paletteButtons[3]); // cylinder
                yield return CarryTo(new Vector3(x, 0, z));
                yield return Type(6, 5);
                yield return Type(7, 24);
                yield return Type(8, 5);
                yield return Type(0, x); // the mouse lands it on the plate in front; typing puts it exactly
                yield return Type(1, 58.5f);
                yield return Type(2, z);
            }
            yield return ClickElement(MaterialButton(BodyMaterial.Acrylic));
            yield return ClickElement(SwatchButton("#2F6FD8"));
            yield return ClickElement(paletteButtons[0]);
            yield return CarryTo(new Vector3(0, 0, 0));
            yield return Type(0, 0);
            yield return Type(1, 72f);
            yield return Type(2, 0);
            yield return WaitForBody();
            yield return Frames(2);

            // On the top deck: the Uno (turned a quarter), the L298N, and the sensor on its bracket at the front.
            yield return ClickElement(tabButtons[LibraryTab.Parts]);
            yield return ClickElement(partButtons[PartCatalog.Uno]);
            yield return CarryTo(new Vector3(-28, 73.5f, -35));
            yield return Type(4, 270);
            yield return ClickElement(partButtons[PartCatalog.L298N]);
            yield return CarryTo(new Vector3(30, 73.5f, 22));
            yield return ClickElement(partButtons[PartCatalog.HcSr04]);
            yield return CarryTo(new Vector3(0, 73.5f, 75));
            var uno = Design.Parts.Find(p => p.Part == PartCatalog.Uno);
            bool onDeck = uno != null && Mathf.Abs(uno.Y - 73.55f) < 0.2f && Mathf.Abs(Mathf.DeltaAngle(uno.Rotation, 270)) < 0.01f;
            var sensor = Design.Parts.Find(p => p.Part == PartCatalog.HcSr04);
            bool facing = sensor != null && DesignGeometry.SonarAim(sensor).z > 0.99f && Mathf.Abs(sensor.Y - 89.55f) < 0.2f;
            bool refused = Design.AddPart(PartCatalog.TtMotor) == null; // an L298N drives two motors
            selection.Clear();
            SelectionChanged();
            yield return WaitForBody();
            yield return Frames(4);
            double buildSeconds = watch.Elapsed.TotalSeconds;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-body"));
            yield return StudioViewCheck();
            var meshes = shown?.Body;
            var shapes = new System.Text.StringBuilder();
            foreach (var f in Design.Body.Features)
                shapes.Append($" {f.Kind} {f.Material}{(f.Colour.Length > 0 ? " " + f.Colour : "")} at ({f.X:0.#}, {f.Y:0.#}, {f.Z:0.#}) {f.SizeX:0.#}×{f.SizeY:0.#}×{f.SizeZ:0.#};");
            var parts = new System.Text.StringBuilder();
            foreach (var part in Design.Parts) parts.Append($" {part.Id} ({part.X:0.#}, {part.Y:0.#}, {part.Z:0.#}) turned ({part.RotX:0}, {part.Rotation:0}, {part.RotZ:0});");
            report.AppendLine("  shapes:" + shapes);
            report.AppendLine("  parts:" + parts);
            int overlaps = 0;
            foreach (var part in Design.Parts) if (DesignGeometry.Overlaps(Design, part)) overlaps++;
            report.AppendLine($"  a new robot is empty: {Yes(startsEmpty)}; plate set down on the workplane in blue acrylic: {Yes(onWorkplane)}; " +
                              $"motors hung under it with wheels at x = {leftWheel.x:0.#} and {rightWheel.x:0.#} mm, on the floor: {Yes(hung)}; " +
                              $"its button put the right wheel on the other end of the shaft and an undo put it back: {Yes(wheelEnd)}; battery on the plate: {Yes(onPlate)}; " +
                              $"Uno on the top deck, turned 270°: {Yes(onDeck)}; sensor on its bracket looking forward: {Yes(facing)}; a part has only the cone and the curls, no size handles: {Yes(partHandles)}; a third motor refused: {Yes(refused)}; parts touching: {overlaps}");
            report.AppendLine($"  body: {Design.Body.Members(null).Count} shapes, {meshes?.VolumeMm3 / 1000 ?? 0:F1} cm³, {meshes?.MassG ?? 0:F0} g, built by Manifold in {meshes?.BuildMs ?? 0:F1} ms; " +
                              $"robot {Design.MassKg() * 1000:F0} g; everything placed with the mouse in {buildSeconds:F1} s");
            string stl = ExportStl(Path.GetDirectoryName(SpikeReport.Shot("x")));
            long expected = 84 + 50L * ((meshes?.StlTriangles.Length ?? 0) / 3);
            long actual = stl.Length > 0 && File.Exists(stl) ? new FileInfo(stl).Length : -1;
            report.AppendLine($"  STL export: {Path.GetFileName(stl)}, {actual} bytes for {(meshes?.StlTriangles.Length ?? 0) / 3} triangles " +
                              $"(expected {expected}): {(actual == expected ? "OK" : "WRONG")}");

            // The kit's sixteen connections, made pin by pin as a player clicks them.
            OnAction("act.wire");
            foreach (var w in DesignPresets.ObstacleAvoiderKit().Wires)
            {
                PinClicked(w.FromPart + "/" + w.FromPin);
                PinClicked(w.ToPart + "/" + w.ToPin);
            }
            var circuit = CircuitAnalysis.Analyse(Design);
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-wire"));
            report.AppendLine($"  wire: {Design.Wires.Count} wires; check: {(circuit.HasProblems ? "problems: " + string.Join(", ", circuit.Warnings) : "no problems")}; " +
                              $"left motor on channel {circuit.Motor("left")?.Channel} polarity {circuit.Motor("left")?.Polarity}, " +
                              $"right on channel {circuit.Motor("right")?.Channel} polarity {circuit.Motor("right")?.Polarity}");
            WireRoutingReport();

            // Two classic mistakes: the sensor's ground wire missing, then the Uno's 5V jumper moved from the
            // L298N's +5V to its +12V terminal, which carries the battery's full voltage.
            int sensorGround = Design.Wires.FindIndex(w => w.FromPart == "sonar1" && w.FromPin == "GND");
            RemoveWire(sensorGround);
            var noGround = CircuitAnalysis.Analyse(Design);
            Undo();
            RemoveWire(Design.Wires.FindIndex(w => w.ToPart == "uno1" && w.ToPin == "5V"));
            bool fullRefused5v = !ConnectPins("battery1/+", "uno1/5V"); // the battery lead already goes to +12V
            ConnectPins("driver1/+12V", "uno1/5V");
            var burning = CircuitAnalysis.Analyse(Design);
            renderSide?.Invoke();
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-wire-warning"));
            Undo();
            Undo();
            bool restored = !CircuitAnalysis.Analyse(Design).HasProblems && Design.Wires.Count == 16;
            report.AppendLine($"  mistakes: without the sensor's GND wire the check says {string.Join(", ", noGround.Warnings.FindAll(w => !w.Info))}; " +
                              $"battery + on the Uno 5V pin says {string.Join(", ", burning.Warnings.FindAll(w => !w.Info))} (board damaged: {burning.BoardDamaged}); " +
                              $"both undone: {(restored ? "yes" : "NO")}; a second wire on the battery lead refused: {(fullRefused5v ? "yes" : "NO")}");

            yield return WireByMouse();

            // The robot runs the obstacle-avoider sketch; START then takes it to the arena.
            robot.SketchFile = RobotProject.GoldenSketch;
            robot.SketchText = "";
            robot.UploadedText = "";
            robot.ProgramBytes = 2954;
            CloseSide();
            yield return Frames(6);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-scratch"));
            report.AppendLine($"  back on the turntable: {robot.PartCount} parts, {robot.MassKg * 1000:F0} g; screenshots -garage-body, " +
                              "-garage-wire, -garage-wire-warning, -garage-wire-mouse, -garage-wire-points, -garage-look, -garage-studio-drag, -garage-scratch");
        }

        /// <summary>
        /// Wiring and building as a player does it, through the same mouse and key code: a drag from pin to pin,
        /// two clicks, a drag onto a pin that already has its jumper, a click on a wire, Del and Ctrl+Z, "Look at"
        /// with the pin names, and in the Body Studio the L298N dragged across the deck with the mouse.
        /// </summary>
        IEnumerator WireByMouse()
        {
            var report = SpikeReport.Text;
            RemoveWire(Design.Wires.FindIndex(w => w.ToPart == "sonar1" && w.ToPin == "TRIG"));
            RemoveWire(Design.Wires.FindIndex(w => w.ToPart == "sonar1" && w.ToPin == "ECHO"));
            ViewWholeRobot();
            yield return Frames(3);

            yield return MouseDrag(PinScreen("uno1/D9"), PinScreen("sonar1/TRIG"));
            bool dragged = HasWire("uno1/D9", "sonar1/TRIG");
            yield return MouseClick(PinScreen("sonar1/ECHO"));
            bool waiting = wireStart == "sonar1/ECHO";
            yield return MouseClick(PinScreen("uno1/D10"));
            bool clicked = HasWire("sonar1/ECHO", "uno1/D10");
            int count = Design.Wires.Count;
            yield return MouseDrag(PinScreen("uno1/D4"), PinScreen("driver1/IN1"));
            bool refused = Design.Wires.Count == count && wireStart == null;
            yield return Frames(3);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-wire-mouse"));

            int trig = Design.Wires.FindIndex(w => w.FromPart == "uno1" && w.FromPin == "D9");
            yield return MouseClick(WireScreen(trig));
            bool selected = selectedWire == trig;
            yield return Press(KeyCode.Delete, false);
            bool deleted = Design.Wires.Count == count - 1;
            yield return Press(KeyCode.Z, true);
            bool undone = Design.Wires.Count == count && HasWire("uno1/D9", "sonar1/TRIG");
            yield return ShapeWireByMouse();

            FocusPart("uno1");
            yield return Frames(4);
            int tags = 0;
            foreach (var tag in pinTags) if (tag.style.display == DisplayStyle.Flex) tags++;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-look"));
            ViewWholeRobot();

            // The Body Studio: drag the L298N 20 mm to the left over the deck with the mouse; it stays on the deck.
            OnAction("act.build");
            yield return Frames(3);
            var driver = Design.Find("driver1")!;
            float x0 = driver.X, y0 = driver.Y;
            var grab = new Vector3(driver.X, driver.Y + 26, driver.Z - 8); // on the heatsink's top
            yield return MouseDrag(Screen2(WorldOf(grab)), Screen2(WorldOf(grab + new Vector3(-20, 0, 0))));
            float moved = driver.X - x0;
            bool stayed = Mathf.Abs(driver.Y - y0) < 0.2f;
            bool wiresFollow = CircuitAnalysis.Analyse(Design) is { HasProblems: false };
            yield return WaitForBody();
            yield return Frames(3);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-studio-drag"));
            Undo();
            yield return WaitForBody();
            bool back = Mathf.Abs(Design.Find("driver1")!.X - x0) < 0.01f;
            OnAction("act.wire");

            report.AppendLine($"  by mouse: drag from D9 to TRIG made a wire: {Yes(dragged)}; click ECHO then D10: {Yes(waiting && clicked)}; " +
                              $"a drag from D4 onto IN1, which has its jumper, refused: {Yes(refused)}; click on a wire selected it: {Yes(selected)}, " +
                              $"Del removed it: {Yes(deleted)}, Ctrl+Z brought it back: {Yes(undone)}; Look at Uno showed {tags} pin names; " +
                              $"in the Studio the L298N dragged {moved:0.#} mm along the deck, on the deck: {Yes(stayed)}, wiring still right: {Yes(wiresFollow)}, undone: {Yes(back)}");
        }

        /// <summary>
        /// The Tinkercad handles, with the mouse, on a box set down on the empty workplane: a corner square makes it
        /// 10 mm longer and wider while the far corner stays; the top square makes it 10 mm taller with its base still
        /// on the workplane; the cone lifts it 15 mm; the curl under its front edge turns it a quarter (the protractor
        /// is photographed half way). Five undos then leave the robot empty again.
        /// </summary>
        IEnumerator HandlesByMouse()
        {
            var report = SpikeReport.Text;
            yield return ClickElement(paletteButtons[1]); // box
            yield return CarryTo(new Vector3(0, 0, 0));
            var box = SelectedFeature;
            yield return Frames(2);
            if (box == null || HandleScreen(Grip.Corner, 0, 3) == null)
            {
                report.AppendLine("  Tinkercad handles: NO box with handles to try");
                yield break;
            }
            var pick = selection[0];
            var s0 = box.Clone();

            var corner = new Vector3(box.X + box.SizeX / 2, box.Y - box.SizeY / 2, box.Z + box.SizeZ / 2);
            var cornerScreen = HandleScreen(Grip.Corner, 0, 3)!.Value;
            yield return MouseDrag(cornerScreen, Screen2(WorldOf(corner + new Vector3(10, 0, 10))));
            bool cornered = Near(box.SizeX, s0.SizeX + 10) && Near(box.SizeZ, s0.SizeZ + 10) && Near(box.SizeY, s0.SizeY)
                            && Near(box.X - box.SizeX / 2, s0.X - s0.SizeX / 2) && Near(box.Z - box.SizeZ / 2, s0.Z - s0.SizeZ / 2);
            yield return Frames(2);
            // Hovering a corner shows its two dimension lines (photographed while the scripted mouse is still there).
            var hoverAt = HandleScreen(Grip.Corner, 0, 3) ?? cornerScreen;
            for (int i = 0; i < 5; i++) scriptedInput.Enqueue(new PointerFrame { Position = hoverAt });
            while (scriptedInput.Count > 2) yield return null;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-handles"));
            while (scriptedInput.Count > 0) yield return null;

            var top = new Vector3(box.X, box.Y + box.SizeY / 2, box.Z);
            yield return MouseDrag(HandleScreen(Grip.Top, 1, 1) ?? Vector2.zero, Screen2(WorldOf(top + new Vector3(0, 10, 0))));
            bool taller = Near(box.SizeY, s0.SizeY + 10) && Near(box.Y - box.SizeY / 2, 0);
            yield return Frames(2);

            var up = robotAnchor.up;
            var line = WorldOf(PivotOf(pick));
            var coneScreen = HandleScreen(Grip.Lift, 1, 1) ?? Vector2.zero;
            float along = RayLineParameter(view.ScreenPointToRay(coneScreen), line, up);
            yield return MouseDrag(coneScreen, Screen2(line + up * (along + 0.015f)));
            bool lifted = Near(box.Y - box.SizeY / 2, 15);
            yield return Frames(2);

            // A quarter turn about the vertical, dragged along the curl's circle, photographed at 45°.
            var curlScreen = HandleScreen(Grip.Turn, 1, 1) ?? Vector2.zero;
            var pivot = WorldOf(PivotOf(pick));
            var ray = view.ScreenPointToRay(curlScreen);
            new Plane(up, pivot).Raycast(ray, out float enter);
            var arm = ray.GetPoint(enter) - pivot;
            Vector2 Turned(float degrees) => Screen2(pivot + Quaternion.AngleAxis(degrees, up) * arm);
            scriptedInput.Enqueue(new PointerFrame { Position = curlScreen });
            scriptedInput.Enqueue(new PointerFrame { Position = curlScreen, LeftPressed = true, LeftHeld = true });
            for (int i = 1; i <= 6; i++) scriptedInput.Enqueue(new PointerFrame { Position = Turned(7.5f * i), LeftHeld = true });
            for (int i = 0; i < 6; i++) scriptedInput.Enqueue(new PointerFrame { Position = Turned(45), LeftHeld = true });
            while (scriptedInput.Count > 3) yield return null;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-protractor"));
            for (int i = 1; i <= 6; i++) scriptedInput.Enqueue(new PointerFrame { Position = Turned(45 + 7.5f * i), LeftHeld = true });
            scriptedInput.Enqueue(new PointerFrame { Position = Turned(90) });
            scriptedInput.Enqueue(new PointerFrame { Position = Turned(90) });
            while (scriptedInput.Count > 0) yield return null;
            yield return null;
            bool turned = Mathf.Abs(Mathf.DeltaAngle(box.RotY, 90)) < 0.5f && Mathf.Abs(box.RotX) < 0.01f && Mathf.Abs(box.RotZ) < 0.01f;

            for (int i = 0; i < 5; i++) Undo();
            yield return WaitForBody();
            bool clean = Design.Body.Features.Count == 0 && Design.Parts.Count == 0;
            report.AppendLine($"  Tinkercad handles by mouse on a box: a corner made it 10 mm longer and wider, the far corner stayed: {Yes(cornered)}; " +
                              $"the top square 10 mm taller, its base on the workplane: {Yes(taller)}; the cone lifted it 15 mm: {Yes(lifted)}; " +
                              $"a curl turned it a quarter: {Yes(turned)} ({box.RotY:0.#}°); five undos left the robot empty: {Yes(clean)}; screenshots -garage-handles, -garage-protractor");
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

        /// <summary>Carries the item riding the mouse to a point (mm, chassis frame) and clicks it down there.</summary>
        IEnumerator CarryTo(Vector3 mm)
        {
            var at = Screen2(WorldOf(mm));
            yield return Play(new List<PointerFrame>
            {
                new PointerFrame { Position = at },
                new PointerFrame { Position = at },
                new PointerFrame { Position = at, LeftPressed = true, LeftHeld = true },
                new PointerFrame { Position = at },
                new PointerFrame { Position = at },
            });
        }

        /// <summary>Types a number into one of the inspector's position, rotation or size boxes (0-8).</summary>
        IEnumerator Type(int slot, float value)
        {
            var field = vecFields[slot];
            if (field != null) field.value = value;
            yield return null;
        }

        Button MaterialButton(BodyMaterial material) =>
            libraryBody.Query<Button>(className: "material-button").AtIndex(Array.IndexOf(MaterialOrder, material));

        Button SwatchButton(string hex) =>
            libraryBody.Query<Button>(className: "colour-swatch").AtIndex(Array.IndexOf(ColourSwatches, hex));

        static string Yes(bool ok) => ok ? "yes" : "NO";

        bool HasWire(string a, string b)
        {
            var (pa, na) = SplitKey(a);
            var (pb, nb) = SplitKey(b);
            return Design.Wires.Exists(w => (w.FromPart == pa && w.FromPin == na && w.ToPart == pb && w.ToPin == nb) ||
                                            (w.FromPart == pb && w.FromPin == nb && w.ToPart == pa && w.ToPin == na));
        }

        Vector2 PinScreen(string key) => shown != null && shown.PinMarkers.TryGetValue(key, out var marker)
            ? (Vector2)view.WorldToScreenPoint(marker.position) : Vector2.zero;

        /// <summary>The screen point halfway along a wire's curve.</summary>
        Vector2 WireScreen(int index)
        {
            var path = shown != null && index >= 0 && index < shown.WirePaths.Count ? shown.WirePaths[index] : null;
            return path == null ? Vector2.zero : (Vector2)view.WorldToScreenPoint(shown!.Root.transform.TransformPoint(path[path.Length / 2]));
        }

        IEnumerator Play(List<PointerFrame> frames)
        {
            foreach (var frame in frames) scriptedInput.Enqueue(frame);
            while (scriptedInput.Count > 0) yield return null;
            yield return null;
        }

        IEnumerator MouseDrag(Vector2 from, Vector2 to)
        {
            var frames = new List<PointerFrame>
            {
                new PointerFrame { Position = from },
                new PointerFrame { Position = from, LeftPressed = true, LeftHeld = true },
            };
            for (int i = 1; i <= 8; i++) frames.Add(new PointerFrame { Position = Vector2.Lerp(from, to, i / 8f), LeftHeld = true });
            frames.Add(new PointerFrame { Position = to });
            frames.Add(new PointerFrame { Position = to });
            return Play(frames);
        }

        IEnumerator MouseClick(Vector2 at) => Play(new List<PointerFrame>
        {
            new PointerFrame { Position = at },
            new PointerFrame { Position = at, LeftPressed = true, LeftHeld = true },
            new PointerFrame { Position = at },
            new PointerFrame { Position = at },
        });

        IEnumerator Press(KeyCode key, bool ctrl) => Play(new List<PointerFrame> { new PointerFrame { Position = input.Position, Key = key, Ctrl = ctrl } });

        /// <summary>A UI Toolkit pointer event at a panel position, as the Phase 0.6 dock test sends them.</summary>
        void SendPointer(EventType type, Vector2 position)
        {
            var systemEvent = new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
            EventBase pointerEvent = type == EventType.MouseDown ? PointerDownEvent.GetPooled(systemEvent)
                : type == EventType.MouseUp ? PointerUpEvent.GetPooled(systemEvent)
                : (EventBase)PointerMoveEvent.GetPooled(systemEvent);
            using (pointerEvent) root.panel.visualTree.SendEvent(pointerEvent);
        }
    }
}
