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
    /// The Garage's editing modes (docs/03 §5–7, docs/08, ADR-0009). Build places catalogue parts on the
    /// chassis, Wire joins pins with jumper wires and checks the circuit as it grows, Body shapes the chassis
    /// plates with Manifold and exports them as STL. Every change goes into the robot's <see cref="RobotDesign"/>,
    /// which the arena turns into the physics robot and its circuit.
    /// </summary>
    public sealed partial class GarageSpike
    {
        enum EditMode { None, Build, Wire, Body }

        static readonly Vector3 DefaultTarget = new Vector3(0, 0.10f, 0);
        static readonly string[] Palette = { "auto", "red", "black", "yellow", "green", "blue", "white", "orange", "purple", "grey", "brown" };
        static readonly string[] SignalColours = { "yellow", "green", "blue", "orange", "white", "purple", "grey", "brown" };

        EditMode mode;
        Vector3 orbitTarget = DefaultTarget;
        Label hint = null!, tooltip = null!;

        // Selection and the wire being drawn
        string? selectedPart;
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
        }

        readonly Queue<PointerFrame> scriptedInput = new Queue<PointerFrame>();
        PointerFrame input;
        Vector2 pressPosition;
        bool leftDown, rightDown, panning, dragging, dragMoved;
        bool wireGesture, wireStartedByPress; // a press on a pin: a drag to another pin makes a wire, a click starts one
        Vector2 dragOffset;
        RobotDesign? dragBefore;

        // "Look at" a part in Wire: the camera comes close and the pins show their names
        string? focusedPart;
        readonly List<Label> pinTags = new List<Label>();
        string listFrom = "", listTo = "";

        // Undo and redo, saving and the body rebuild, which runs on a worker thread so the sliders stay smooth
        readonly List<RobotDesign> undo = new List<RobotDesign>();
        readonly List<RobotDesign> redo = new List<RobotDesign>();
        float saveAt = -1, lastBodyEdit = -10, bodyRebuildAt;
        bool bodyDirty;
        Task<BodyData>? bodyTask;
        int designEpoch, bodyTaskEpoch; // a build started before an undo must not replace the model after it
        int bodyBuildsShown;
        VisualElement? bodyReadout;
        string lastExport = "";

        static RobotDesign Design => Robot.Design;

        void BuildEditUi()
        {
            tooltip = Classed(new Label { pickingMode = PickingMode.Ignore }, "edit-tooltip");
            tooltip.style.display = DisplayStyle.None;
            root.Add(tooltip);
        }

        void UpdateHint() => hint.text = Tr(mode switch
        {
            EditMode.Build => "garage.hint.build",
            EditMode.Wire => "garage.hint.wire",
            EditMode.Body => "garage.hint.body",
            _ => "garage.hint",
        });

        // ------------------------------------------------------------------ entering and leaving

        void EnterMode(EditMode next, string titleKey, Action render)
        {
            if (mode != next) LeaveMode(show: false);
            mode = next;
            // The turntable stops facing the camera's side so that the deck frame and the view agree.
            turntable.localRotation = Quaternion.identity;
            orbitTarget = DefaultTarget;
            // Wire comes closer to the small pins; the Body Studio keeps some room around the robot for new shapes.
            distance = next == EditMode.Wire ? 0.40f : next == EditMode.Body ? 0.52f : 0.50f;
            pitch = 42f;
            yaw = 200f;
            selectedPart = null;
            selectedWire = -1;
            wireStart = null;
            focusedPart = null;
            if (next == EditMode.Body) OpenStudio();
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
            selectedPart = null;
            selectedWire = -1;
            wireStart = null;
            hoveredPin = null;
            dragging = false;
            tooltip.style.display = DisplayStyle.None;
            if (wirePreview != null) wirePreview.enabled = false;
            orbitTarget = DefaultTarget;
            distance = 0.62f;
            pitch = 14f;
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
            selectedPart = null;
            selectedWire = -1;
            wireStart = null;
            hoveredPin = null;
            dragging = false;
            undo.Clear();
            redo.Clear();
            selectedFeature = null;
            CancelDrawing();
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
            if (selectedPart != null && Design.Find(selectedPart) == null) selectedPart = null;
            if (selectedFeature != null && Design.Body.Feature(selectedFeature) == null) selectedFeature = null;
            selectedWire = -1;
            wireStart = null;
            DesignChanged();
        }

        /// <summary>
        /// After any change: new model, card and panel; the save follows shortly. In the Body Studio the model is
        /// rebuilt on the worker thread, so an undo does not stop the frame for Manifold.
        /// </summary>
        void DesignChanged()
        {
            designEpoch++;
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
                    FillBodyReadout();
                }
            }
            if (bodyDirty && Time.unscaledTime >= bodyRebuildAt) FlushBody();
            UpdateStudioScene();
            UpdatePinTags();
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
                if (mode == EditMode.Build) BeginPartDrag(mouse);
                if (mode == EditMode.Wire) BeginWireGesture(mouse);
                if (mode == EditMode.Body) BeginStudioPress(mouse);
            }
            if (leftDown && input.LeftHeld)
            {
                if (dragging) UpdatePartDrag(mouse);
                else if (drag.grip != Grip.None) UpdateStudioDrag(mouse);
                else if (!wireGesture && (mouse - pressPosition).magnitude > 4) Orbit(mouse);
            }
            if (leftDown && !input.LeftHeld)
            {
                leftDown = false;
                bool click = (mouse - pressPosition).magnitude <= 4;
                if (dragging) EndPartDrag();
                else if (drag.grip != Grip.None) EndStudioDrag();
                else if (wireGesture) EndWireGesture(mouse, click);
                else if (click) SceneClick(mouse);
            }

            // Right button turns the view, the middle button pans it.
            if (input.RightPressed && !overUi)
            {
                rightDown = true;
                lastMouse = mouse;
            }
            if (rightDown && input.RightHeld) Orbit(mouse);
            else rightDown = false;
            if (input.MiddlePressed && !overUi)
            {
                panning = true;
                lastMouse = mouse;
            }
            if (panning && input.MiddleHeld)
            {
                var delta = mouse - (Vector2)lastMouse;
                lastMouse = mouse;
                float k = distance * 0.0012f;
                MoveTarget(orbitTarget - view.transform.right * delta.x * k - view.transform.up * delta.y * k);
            }
            else
            {
                panning = false;
            }

            // The wheel zooms toward the point under the mouse, so small pins can be reached.
            if (Mathf.Abs(input.Scroll) > 0.01f && !overUi)
            {
                float old = distance;
                distance = Mathf.Clamp(distance * (1f - input.Scroll * 0.12f), 0.12f, 1.3f);
                if (distance < old && DeckPointWorld(mouse, out var point)) MoveTarget(orbitTarget + (point - orbitTarget) * (1 - distance / old));
                else if (distance > old) MoveTarget(orbitTarget + (DefaultTarget - orbitTarget) * (1 - old / distance));
            }

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

        void Orbit(Vector2 mouse)
        {
            var delta = mouse - (Vector2)lastMouse;
            lastMouse = mouse;
            yaw += delta.x * 0.3f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.2f, 3f, 85f);
        }

        void MoveTarget(Vector3 target)
        {
            var offset = Vector3.ClampMagnitude(target - DefaultTarget, 0.25f);
            orbitTarget = DefaultTarget + offset;
        }

        bool IsTyping() => root.panel?.focusController?.focusedElement != null;

        void UpdateKeys()
        {
            if (mode == EditMode.Body && StudioKeys()) return;
            if (input.Ctrl && KeyPressed(KeyCode.Z)) Undo();
            if (input.Ctrl && KeyPressed(KeyCode.Y)) Redo();
            if (KeyPressed(KeyCode.Escape))
            {
                if (wireStart != null) CancelWire();
                else if (selectedPart != null || selectedWire >= 0) SelectNothing();
                else CloseSide();
                return;
            }
            bool delete = KeyPressed(KeyCode.Delete) || KeyPressed(KeyCode.Backspace);
            if (mode == EditMode.Build)
            {
                if (KeyPressed(KeyCode.R)) RotateSelected();
                if (delete) RemoveSelectedPart();
                if (KeyPressed(KeyCode.F) && selectedPart != null && shown != null && shown.Parts.TryGetValue(selectedPart, out var go))
                    MoveTarget(go.transform.position);
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
            if (mode == EditMode.Build && !PartUnder(mouse, out _)) SelectNothing();
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
            selectedPart = null;
            selectedWire = -1;
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
            if (!overUi && (!leftDown || wireGesture) && !rightDown && !panning)
            {
                if (mode == EditMode.Build && PartUnder(mouse, out var partId))
                {
                    text = PartCatalog.Get(Design.Find(partId)?.Part ?? "")?.Name ?? partId;
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

        // ------------------------------------------------------------------ Build

        bool PartUnder(Vector2 mouse, out string partId)
        {
            partId = "";
            Physics.SyncTransforms();
            if (!Physics.Raycast(view.ScreenPointToRay(mouse), out var hit, 5f)) return false;
            var pick = hit.collider.GetComponentInParent<Pickable>();
            if (pick == null) return false;
            partId = pick.PartId;
            return true;
        }

        /// <summary>The point under the mouse on the plane of the top deck, in the chassis frame (mm).</summary>
        bool DeckPoint(Vector2 mouse, out Vector2 mm)
        {
            mm = default;
            if (!DeckPointWorld(mouse, out var world)) return false;
            var local = robotAnchor.InverseTransformPoint(world) * 1000f;
            mm = new Vector2(local.x, local.z);
            return true;
        }

        bool DeckPointWorld(Vector2 mouse, out Vector3 world)
        {
            world = default;
            float y = robotAnchor.position.y + DesignGeometry.DeckTop(Design.Body) * 0.001f;
            var ray = view.ScreenPointToRay(mouse);
            if (!new Plane(Vector3.up, new Vector3(0, y, 0)).Raycast(ray, out float enter) || enter > 5f) return false;
            world = ray.GetPoint(enter);
            return true;
        }

        void BeginPartDrag(Vector2 mouse)
        {
            if (!PartUnder(mouse, out var partId)) return;
            SelectPart(partId);
            var part = Design.Find(partId);
            var def = part == null ? null : PartCatalog.Get(part.Part);
            if (part == null || def == null || def.Mount != MountKind.Deck || !DeckPoint(mouse, out var p)) return;
            dragging = true;
            dragMoved = false;
            dragOffset = new Vector2(part.X - p.x, part.Z - p.y);
            dragBefore = Design.Clone();
        }

        void UpdatePartDrag(Vector2 mouse)
        {
            if (selectedPart == null || !DeckPoint(mouse, out var p)) return;
            // 5 mm steps, like the holes of a real deck; the part stops at the deck's edge and at its neighbours.
            if (TryMovePart(selectedPart, Mathf.Round((p.x + dragOffset.x) / 5) * 5, Mathf.Round((p.y + dragOffset.y) / 5) * 5))
                dragMoved = true;
        }

        void EndPartDrag()
        {
            dragging = false;
            if (dragMoved && dragBefore != null)
            {
                RecordUndo(dragBefore);
                saveAt = Time.unscaledTime + 0.5f;
                renderSide?.Invoke();
            }
            dragBefore = null;
        }

        /// <summary>Moves a deck part if the place is on the deck and free. Used by dragging and the benchmark.</summary>
        bool TryMovePart(string partId, float x, float z)
        {
            var part = Design.Find(partId);
            var def = part == null ? null : PartCatalog.Get(part.Part);
            if (part == null || def == null || def.Mount != MountKind.Deck) return false;
            (x, z) = DesignGeometry.ClampToDeck(Design.Body, def, x, z, part.Rotation);
            if (Mathf.Approximately(x, part.X) && Mathf.Approximately(z, part.Z)) return false;
            if (DesignGeometry.OverlapsDeckPart(Design, def, x, z, part.Rotation, part.Id)) return false;
            part.X = x;
            part.Z = z;
            shown?.MovePart(Design, part.Id);
            return true;
        }

        void SelectPart(string partId)
        {
            selectedPart = partId;
            shown?.Highlight(partId);
            renderSide?.Invoke();
        }

        void AddPart(string partId)
        {
            PushUndo();
            var part = Design.AddPart(partId);
            if (part == null)
            {
                undo.RemoveAt(undo.Count - 1);
                ShowToast(Tr("build.full"));
                return;
            }
            if (partId == PartCatalog.Uno && !Robot.HasSketch)
            {
                Robot.EnsureSketch();
                ShowToast(Tr("build.newBoard"));
            }
            selectedPart = part.Id;
            DesignChanged();
        }

        void RotateSelected()
        {
            var part = selectedPart == null ? null : Design.Find(selectedPart);
            var def = part == null ? null : PartCatalog.Get(part.Part);
            if (part == null || def == null || def.Mount != MountKind.Deck) return;
            int next = (part.Rotation + 90) % 360;
            var (x, z) = DesignGeometry.ClampToDeck(Design.Body, def, part.X, part.Z, next);
            if (DesignGeometry.OverlapsDeckPart(Design, def, x, z, next, part.Id))
            {
                ShowToast(Tr("build.noRoom"));
                return;
            }
            PushUndo();
            part.Rotation = next;
            part.X = x;
            part.Z = z;
            shown?.MovePart(Design, part.Id);
            renderSide?.Invoke();
            saveAt = Time.unscaledTime + 0.5f;
        }

        void RemoveSelectedPart()
        {
            if (selectedPart == null) return;
            PushUndo();
            Design.RemovePart(selectedPart);
            selectedPart = null;
            ShowToast(Tr("build.removed"));
            DesignChanged();
        }

        void RenderBuild()
        {
            var design = Design;
            Section("build.bin");
            foreach (var def in PartCatalog.All)
            {
                string id = def.Id;
                int count = design.Count(id);
                var row = Layout("bin-row");
                var text = Layout("bin-text");
                text.Add(Classed(new Label(def.Name), "bin-name"));
                text.Add(Classed(new Label(SpikeStrings.Format("build.count", count, def.MaxCount, def.MassG)), "bin-sub"));
                row.Add(text);
                var add = SmallButton("build.add", () => AddPart(id));
                add.SetEnabled(count < def.MaxCount);
                row.Add(add);
                sideContent.Add(row);
            }

            Section("build.selected");
            var part = selectedPart == null ? null : design.Find(selectedPart);
            var partDef = part == null ? null : PartCatalog.Get(part.Part);
            if (part == null || partDef == null)
            {
                Info("build.selectHint");
            }
            else
            {
                sideContent.Add(Classed(new Label(partDef.Name), "part-title"));
                sideContent.Add(Classed(new Label(PlaceText(design, part, partDef)), "info-text"));
                sideContent.Add(Classed(new Label($"{partDef.SizeX:0.#} × {partDef.SizeZ:0.#} × {partDef.SizeY:0.#} {Tr("unit.mm")} · {partDef.MassG:0.#} {Tr("unit.g")}"), "info-text"));
                var buttons = Layout("repair-buttons");
                if (partDef.Mount == MountKind.Deck) buttons.Add(SmallButton("build.rotate", RotateSelected));
                buttons.Add(SmallButton("build.remove", RemoveSelectedPart));
                sideContent.Add(buttons);
            }

            foreach (var (a, b) in Overlaps(design)) sideContent.Add(Classed(new Label(SpikeStrings.Format("build.overlap", a, b)), "warn-line"));
            sideContent.Add(Classed(new Label(SpikeStrings.Format("build.mass", design.MassKg() * 1000)), "info-text"));
            sideContent.Add(SmallButton("edit.undo", Undo));
        }

        string PlaceText(RobotDesign design, PartInstance part, PartDef def) => def.Mount switch
        {
            MountKind.Deck => SpikeStrings.Format("build.onDeck", part.X, part.Z, part.Rotation),
            MountKind.Motor => Tr(part.Slot == "right" ? "build.place.right" : "build.place.left"),
            MountKind.Front => Tr("build.place.front"),
            MountKind.Caster => Tr("build.place.caster"),
            _ => Tr(design.Body.Decks >= 2 ? "build.place.between" : "build.place.back"),
        };

        static List<(string, string)> Overlaps(RobotDesign design)
        {
            var found = new List<(string, string)>();
            var parts = design.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var def = PartCatalog.Get(parts[i].Part);
                if (def == null || !DesignGeometry.TakesDeckRoom(design.Body, def)) continue;
                var place = DesignGeometry.Place(design, parts[i]);
                for (int j = i + 1; j < parts.Count; j++)
                {
                    var other = PartCatalog.Get(parts[j].Part);
                    if (other == null || !DesignGeometry.TakesDeckRoom(design.Body, other)) continue;
                    var otherPlace = DesignGeometry.Place(design, parts[j]);
                    var (hx, hz) = DesignGeometry.Footprint(def, place.rotation);
                    var (ox, oz) = DesignGeometry.Footprint(other, otherPlace.rotation);
                    if (Math.Abs(place.x - otherPlace.x) < hx + ox && Math.Abs(place.z - otherPlace.z) < hz + oz)
                        found.Add((def.Name, other.Name));
                }
            }
            return found;
        }

        /// <summary>Keeps every deck part on the deck after the body changed size or shape.</summary>
        void ClampDeckParts()
        {
            foreach (var part in Design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null || def.Mount != MountKind.Deck) continue;
                (part.X, part.Z) = DesignGeometry.ClampToDeck(Design.Body, def, part.X, part.Z, part.Rotation);
            }
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
            shown?.HighlightWire(-1);
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
            DesignChanged();
            shown?.HighlightWire(selectedWire);
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
            MoveTarget(shown.FocusPoint(partId));
            var facing = Vector3.zero;
            foreach (var entry in shown.PinMarkers)
            {
                if (!entry.Key.StartsWith(partId + "/")) continue;
                var (part, pin) = SplitKey(entry.Key);
                var exit = DesignGeometry.PinExit(Design, part, pin);
                if (exit != null) facing += robotAnchor.TransformDirection(new Vector3(exit.Value.x, exit.Value.y, exit.Value.z));
            }
            if (facing.sqrMagnitude > 1e-6f)
            {
                facing.Normalize();
                if (new Vector2(facing.x, facing.z).magnitude > 0.2f) yaw = Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(Mathf.Asin(facing.y) * Mathf.Rad2Deg, 30f, 75f);
            }
            distance = 0.2f;
            renderSide?.Invoke();
        }

        void ViewWholeRobot()
        {
            focusedPart = null;
            orbitTarget = DefaultTarget;
            distance = mode == EditMode.Wire ? 0.40f : 0.50f;
            pitch = 42f;
            yaw = 200f;
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
            selectedWire = index;
            wireStart = null;
            shown?.HighlightWire(index);
            renderSide?.Invoke();
        }

        void RemoveWire(int index)
        {
            if (index < 0 || index >= Design.Wires.Count) return;
            PushUndo();
            Design.Wires.RemoveAt(index);
            selectedWire = -1;
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
                PartKind.Motor => Tr(part.Slot == "right" ? "side.right" : "side.left") + " TT",
                PartKind.Battery => "4×AA",
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
                args[i] = a == "left" ? Tr("side.left") : a == "right" ? Tr("side.right") : a;
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

            Section("wire.lookAt");
            var look = Layout("seg-row");
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null || def.Pins.Count == 0) continue;
                string id = part.Id;
                var button = new Button(() => FocusPart(id)) { text = PartLabel(id), focusable = false };
                button.AddToClassList("seg-button");
                button.EnableInClassList("seg-button--active", id == focusedPart);
                look.Add(button);
            }
            var whole = new Button(ViewWholeRobot) { text = Tr("wire.whole"), focusable = false };
            whole.AddToClassList("seg-button");
            whole.EnableInClassList("seg-button--active", focusedPart == null);
            look.Add(whole);
            sideContent.Add(look);

            Section("wire.byList");
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
            sideContent.Add(SmallButton("wire.addFromList", () => ConnectPins(listFrom, listTo)));

            Section("wire.colour");
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

            Section("wire.check");
            var circuit = CircuitAnalysis.Analyse(design);
            if (!circuit.HasProblems) sideContent.Add(Classed(new Label(Tr("wire.ok")), "ok-line"));
            foreach (var warning in circuit.Warnings)
                sideContent.Add(Classed(new Label((warning.Info ? "" : "⚠ ") + WarningText(warning)), warning.Info ? "info-line" : "warn-line"));

            sideContent.Add(Classed(new Label(SpikeStrings.Format("wire.list", design.Wires.Count)), "section-title"));
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
            sideContent.Add(SmallButton("edit.undo", Undo));
        }

        // ------------------------------------------------------------------ Body

        void RenderBody()
        {
            var b = Design.Body;
            Section("body.shape");
            var shapes = Layout("seg-row");
            shapes.Add(Segment("body.rect", b.Shape == BodyShape.Rectangle, () => b.Shape = BodyShape.Rectangle));
            shapes.Add(Segment("body.rounded", b.Shape == BodyShape.Rounded, () => b.Shape = BodyShape.Rounded));
            shapes.Add(Segment("body.round", b.Shape == BodyShape.Round, () => b.Shape = BodyShape.Round));
            sideContent.Add(shapes);

            bool round = b.Shape == BodyShape.Round;
            if (!round) BodySlider("body.length", 100, 250, 5, b.LengthMm, v => b.LengthMm = v);
            BodySlider(round ? "body.diameter" : "body.width", 80, 200, 5, b.WidthMm, v => b.WidthMm = v);
            BodySlider("body.thickness", 2, 6, 0.5f, b.ThicknessMm, v => b.ThicknessMm = v);
            if (b.Shape == BodyShape.Rounded) BodySlider("body.corner", 3, 40, 1, b.CornerRadiusMm, v => b.CornerRadiusMm = v);
            if (!round) BodySlider("body.walls", 0, 40, 1, b.WallHeightMm, v => b.WallHeightMm = v);

            Section("body.decks");
            var decks = Layout("seg-row");
            decks.Add(Segment("1", b.Decks == 1, () => b.Decks = 1, literal: true));
            decks.Add(Segment("2", b.Decks >= 2, () => b.Decks = 2, literal: true));
            sideContent.Add(decks);

            var holes = new Toggle(Tr("body.holes")) { value = b.HoleGrid, focusable = false };
            holes.AddToClassList("body-toggle");
            holes.RegisterValueChangedCallback(e => BodyEdit(() => b.HoleGrid = e.newValue, rerender: true));
            sideContent.Add(holes);
            if (b.HoleGrid) BodySlider("body.pitch", 10, 30, 1, b.HolePitchMm, v => b.HolePitchMm = v);

            Section("body.material");
            var materials = Layout("seg-row");
            materials.Add(Segment("body.acrylic", b.Material == BodyMaterial.Acrylic, () => b.Material = BodyMaterial.Acrylic));
            materials.Add(Segment("body.pla", b.Material == BodyMaterial.Pla, () => b.Material = BodyMaterial.Pla));
            materials.Add(Segment("body.plywood", b.Material == BodyMaterial.Plywood, () => b.Material = BodyMaterial.Plywood));
            sideContent.Add(materials);

            bodyReadout = Layout("body-readout");
            sideContent.Add(bodyReadout);
            FillBodyReadout();

            var buttons = Layout("repair-buttons");
            buttons.Add(SmallButton("body.export", () => ExportStl(null)));
            if (lastExport.Length > 0) buttons.Add(SmallButton("body.openFolder", OpenExportFolder));
            buttons.Add(SmallButton("edit.undo", Undo));
            sideContent.Add(buttons);
            Info("body.stlNote");
        }

        Button Segment(string key, bool active, Action apply, bool literal = false)
        {
            var button = new Button(() => BodyEdit(apply, rerender: true)) { text = literal ? key : Tr(key), focusable = false };
            button.AddToClassList("seg-button");
            button.EnableInClassList("seg-button--active", active);
            return button;
        }

        void BodySlider(string key, float low, float high, float step, float value, Action<float> set)
        {
            var slider = new Slider(Tr(key), low, high) { value = value, showInputField = true, focusable = false };
            slider.AddToClassList("body-slider");
            slider.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp(Mathf.Round(e.newValue / step) * step, low, high);
                if (!Mathf.Approximately(v, e.newValue)) slider.SetValueWithoutNotify(v);
                BodyEdit(() => set(v), rerender: false);
            });
            sideContent.Add(slider);
        }

        /// <summary>
        /// One body change. Slider moves close together make one undo step; the rebuild is throttled; the panel
        /// is redrawn only for changes that show or hide controls (redrawing would stop a slider being dragged).
        /// </summary>
        void BodyEdit(Action apply, bool rerender)
        {
            if (rerender || Time.unscaledTime - lastBodyEdit > 0.6f) PushUndo();
            lastBodyEdit = Time.unscaledTime;
            apply();
            ClampDeckParts();
            bodyDirty = true;
            saveAt = Time.unscaledTime + 0.8f;
            if (rerender)
            {
                FlushBody();
                renderSide?.Invoke();
            }
        }

        void FillBodyReadout()
        {
            if (bodyReadout == null || mode != EditMode.Body) return;
            bodyReadout.Clear();
            var b = Design.Body;
            var meshes = shown?.Body;
            int plates = Math.Max(1, b.Decks);
            double volumeCm3 = (meshes?.VolumeMm3 ?? 0) / 1000.0;
            string material = Tr(b.Material switch { BodyMaterial.Pla => "body.pla", BodyMaterial.Plywood => "body.plywood", _ => "body.acrylic" });
            bodyReadout.Add(Classed(new Label(SpikeStrings.Format("body.size", b.WidthMm, b.EffectiveLength, b.ThicknessMm)), "info-text"));
            bodyReadout.Add(Classed(new Label(SpikeStrings.Format("body.plates", plates, meshes?.Holes ?? 0)), "info-text"));
            bodyReadout.Add(Classed(new Label(SpikeStrings.Format("body.mass", volumeCm3 * BodyDesign.DensityGPerCm3(b.Material), volumeCm3, material)), "info-text"));
            if (meshes != null) bodyReadout.Add(Classed(new Label(SpikeStrings.Format("body.kernel", meshes.BuildMs)), "bin-sub"));
            foreach (var (a, c) in Overlaps(Design)) bodyReadout.Add(Classed(new Label(SpikeStrings.Format("build.overlap", a, c)), "warn-line"));
        }

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
        /// Builds a robot the way a player would, through the same functions the mouse uses: a new chassis in
        /// Body, parts from the bin in Build, every wire pin by pin in Wire, with two wiring mistakes checked
        /// and undone. START then drives this robot in the arena.
        /// </summary>
        IEnumerator BuildFromScratch()
        {
            var report = SpikeReport.Text;
            report.AppendLine("robot built from scratch in the Garage (Body, Build and Wire modes):");
            NewRobot();
            var robot = Robot;

            OnAction("act.body");
            var b = Design.Body;
            BodyEdit(() =>
            {
                b.Shape = BodyShape.Rounded;
                b.LengthMm = 170;
                b.WidthMm = 125;
                b.CornerRadiusMm = 18;
                b.HoleGrid = true;
                b.HolePitchMm = 15;
                b.Material = BodyMaterial.Pla;
            }, rerender: true);
            var sliderWatch = Stopwatch.StartNew();
            yield return WaitForBody();
            double firstBuildMs = sliderWatch.Elapsed.TotalMilliseconds;

            // A slider dragged for one second: the rebuilds run on the worker while frames keep coming.
            var frames = new List<double>();
            int buildsBefore = bodyBuildsShown;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1f)
            {
                float t = (Time.realtimeSinceStartup - start) / 1f;
                BodyEdit(() => b.LengthMm = Mathf.Round((150 + 40 * Mathf.Sin(t * Mathf.PI)) / 5) * 5, rerender: false);
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000.0);
            }
            int builds = bodyBuildsShown - buildsBefore;
            BodyEdit(() => b.LengthMm = 170, rerender: true);
            yield return WaitForBody();
            yield return Frames(3);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-body"));
            report.AppendLine($"  body slider dragged for 1 s: {SpikeReport.FrameStats(frames)}; the model was rebuilt about {builds} times " +
                              $"(first build seen after {firstBuildMs:F0} ms)");
            string stl = ExportStl(Path.GetDirectoryName(SpikeReport.Shot("x")));
            var meshes = shown?.Body;
            long expected = 84 + 50L * ((meshes?.StlTriangles.Length ?? 0) / 3);
            long actual = stl.Length > 0 && File.Exists(stl) ? new FileInfo(stl).Length : -1;
            report.AppendLine($"  body: rounded 170 × 125 × 3 mm PLA, two decks, M3 grid of {meshes?.Holes ?? 0} holes per plate, " +
                              $"{(meshes?.VolumeMm3 ?? 0) / 1000:F1} cm³, rebuilt by Manifold in {meshes?.BuildMs ?? 0:F1} ms");
            report.AppendLine($"  STL export: {Path.GetFileName(stl)}, {actual} bytes for {(meshes?.StlTriangles.Length ?? 0) / 3} triangles " +
                              $"(expected {expected}): {(actual == expected ? "OK" : "WRONG")}");

            yield return StudioByMouse();

            OnAction("act.build");
            foreach (string part in new[] { PartCatalog.Uno, PartCatalog.L298N, PartCatalog.HcSr04, PartCatalog.TtMotor, PartCatalog.TtMotor, PartCatalog.Battery4AA, PartCatalog.Caster })
                AddPart(part);
            bool fullRefused = Design.AddPart(PartCatalog.TtMotor) == null; // a third motor has no mount
            bool driverMoved = TryMovePart("driver1", 30, 25);
            SelectPart("uno1");
            for (int i = 0; i < 3; i++) RotateSelected();
            bool unoMoved = TryMovePart("uno1", -28, -40);
            bool blocked = !TryMovePart("driver1", -28, -40); // onto the Uno
            ShowRobot();
            renderSide?.Invoke();
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-build"));
            report.AppendLine($"  build: {Design.Parts.Count} parts from the bin, {Design.MassKg() * 1000:F0} g; a third motor refused: {(fullRefused ? "yes" : "NO")}; " +
                              $"Uno turned to {Design.Find("uno1")?.Rotation}° and moved: {unoMoved}; L298N moved: {driverMoved}; " +
                              $"dropping it on the Uno refused: {(blocked ? "yes" : "NO")}; overlaps: {Overlaps(Design).Count}");

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
            report.AppendLine($"  back on the turntable: {robot.PartCount} parts, {robot.MassKg * 1000:F0} g; screenshots -garage-body, -garage-build, " +
                              "-garage-wire, -garage-wire-warning, -garage-wire-mouse, -garage-look, -garage-scratch");
        }

        /// <summary>
        /// Wiring and building as a player does it, through the same mouse and key code: a drag from pin to pin,
        /// two clicks, a drag onto a pin that already has its jumper, a click on a wire, Del and Ctrl+Z, a part
        /// dragged in Build, "Look at" with the pin names, and a Body slider dragged with UI Toolkit events.
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

            FocusPart("uno1");
            yield return Frames(4);
            int tags = 0;
            foreach (var tag in pinTags) if (tag.style.display == DisplayStyle.Flex) tags++;
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-look"));
            ViewWholeRobot();

            // Build: drag the L298N 20 mm to the left with the mouse.
            OnAction("act.build");
            yield return Frames(3);
            float before = Design.Find("driver1")!.X;
            var part = shown!.Parts["driver1"].transform;
            var grab = part.position + part.up * 0.012f;
            var dropAt = grab + robotAnchor.TransformDirection(Vector3.left) * 0.020f;
            yield return MouseDrag(view.WorldToScreenPoint(grab), view.WorldToScreenPoint(dropAt));
            float moved = Design.Find("driver1")!.X - before;

            // Body: drag the length slider's handle 40 pixels to the right with UI Toolkit pointer events.
            OnAction("act.body");
            yield return Frames(3);
            float length = Design.Body.LengthMm;
            var slider = sideContent.Q<Slider>();
            var handle = slider?.Q("unity-dragger");
            if (handle != null)
            {
                Vector2 from = handle.worldBound.center, to = from + new Vector2(40, 0);
                SendPointer(EventType.MouseDown, from);
                for (int i = 1; i <= 8; i++)
                {
                    SendPointer(EventType.MouseDrag, Vector2.Lerp(from, to, i / 8f));
                    yield return null;
                }
                SendPointer(EventType.MouseUp, to);
            }
            yield return WaitForBody();
            float lengthAfter = Design.Body.LengthMm;
            Undo();
            yield return WaitForBody();
            OnAction("act.wire");

            report.AppendLine($"  by mouse: drag from D9 to TRIG made a wire: {Yes(dragged)}; click ECHO then D10: {Yes(waiting && clicked)}; " +
                              $"a drag from D4 onto IN1, which has its jumper, refused: {Yes(refused)}; click on a wire selected it: {Yes(selected)}, " +
                              $"Del removed it: {Yes(deleted)}, Ctrl+Z brought it back: {Yes(undone)}; Look at Uno showed {tags} pin names; " +
                              $"Build drag moved the L298N by {moved:F0} mm; the Body slider went from {length:F0} to {lengthAfter:F0} mm");
        }

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
