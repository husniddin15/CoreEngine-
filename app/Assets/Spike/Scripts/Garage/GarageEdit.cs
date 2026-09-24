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

        // Mouse
        Vector2 pressPosition;
        bool leftDown, rightDown, panning, dragging, dragMoved;
        Vector2 dragOffset;
        RobotDesign? dragBefore;

        // Undo, saving and the body rebuild, which runs on a worker thread so the sliders stay smooth
        readonly List<RobotDesign> undo = new List<RobotDesign>();
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
            // Body looks down on the plates so the hole grid shows; Wire comes closer to the small pins.
            distance = next == EditMode.Wire ? 0.40f : next == EditMode.Body ? 0.45f : 0.50f;
            pitch = next == EditMode.Body ? 50f : 42f;
            yaw = 200f;
            selectedPart = null;
            selectedWire = -1;
            wireStart = null;
            ShowRobot();
            OpenSide(titleKey, render);
            UpdateHint();
        }

        /// <summary>Back to the showroom: saves, rebuilds the plain model and the robot's thumbnail.</summary>
        void LeaveMode(bool show = true)
        {
            if (mode == EditMode.None) return;
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
            distance = 0.72f;
            pitch = 12f;
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
        }

        // ------------------------------------------------------------------ changes, undo, saving

        void PushUndo()
        {
            undo.Add(Design.Clone());
            if (undo.Count > 60) undo.RemoveAt(0);
        }

        void Undo()
        {
            if (undo.Count == 0)
            {
                ShowToast(Tr("edit.nothingToUndo"));
                return;
            }
            Robot.Design = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            if (selectedPart != null && Design.Find(selectedPart) == null) selectedPart = null;
            selectedWire = -1;
            wireStart = null;
            DesignChanged();
        }

        /// <summary>After any change: new model, card and panel; the save follows shortly.</summary>
        void DesignChanged()
        {
            designEpoch++;
            ShowRobot();
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
            bodyTask = Task.Run(() => BodyBuilder.BuildData(snapshot));
        }

        /// <summary>For the benchmark: until the model shows the current body.</summary>
        IEnumerator WaitForBody()
        {
            while (bodyDirty || bodyTask != null) yield return null;
        }

        // ------------------------------------------------------------------ mouse and keys

        void UpdateEditing()
        {
            if (overlay.style.display == DisplayStyle.Flex) return;
            bool overUi = IsPointerOverUi();
            Vector2 mouse = Input.mousePosition;

            // Left button: parts are dragged in Build; a click picks; a drag elsewhere turns the view.
            // A click in the scene ends typing in a number box, so the keys work on the robot again.
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !overUi)
                (root.panel?.focusController?.focusedElement as Focusable)?.Blur();
            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                leftDown = true;
                pressPosition = mouse;
                lastMouse = mouse;
                if (mode == EditMode.Build) BeginPartDrag(mouse);
            }
            if (leftDown && Input.GetMouseButton(0))
            {
                if (dragging) UpdatePartDrag(mouse);
                else if ((mouse - pressPosition).magnitude > 4) Orbit(mouse);
            }
            if (leftDown && !Input.GetMouseButton(0))
            {
                leftDown = false;
                if (dragging) EndPartDrag();
                else if ((mouse - pressPosition).magnitude <= 4) SceneClick(mouse);
            }

            // Right button turns the view, the middle button pans it.
            if (Input.GetMouseButtonDown(1) && !overUi)
            {
                rightDown = true;
                lastMouse = mouse;
            }
            if (rightDown && Input.GetMouseButton(1)) Orbit(mouse);
            else rightDown = false;
            if (Input.GetMouseButtonDown(2) && !overUi)
            {
                panning = true;
                lastMouse = mouse;
            }
            if (panning && Input.GetMouseButton(2))
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
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !overUi)
            {
                float old = distance;
                distance = Mathf.Clamp(distance * (1f - scroll * 0.12f), 0.12f, 1.3f);
                if (distance < old && DeckPointWorld(mouse, out var point)) MoveTarget(orbitTarget + (point - orbitTarget) * (1 - distance / old));
                else if (distance > old) MoveTarget(orbitTarget + (DefaultTarget - orbitTarget) * (1 - old / distance));
            }

            UpdateHover(mouse, overUi);
            if (!IsTyping()) UpdateKeys();
        }

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
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (wireStart != null) CancelWire();
                else if (selectedPart != null || selectedWire >= 0) SelectNothing();
                else CloseSide();
                return;
            }
            bool delete = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
            if (mode == EditMode.Build)
            {
                if (Input.GetKeyDown(KeyCode.R)) RotateSelected();
                if (delete) RemoveSelectedPart();
                if (Input.GetKeyDown(KeyCode.F) && selectedPart != null && shown != null && shown.Parts.TryGetValue(selectedPart, out var go))
                    MoveTarget(go.transform.position);
            }
            if (mode == EditMode.Wire && delete && selectedWire >= 0) RemoveWire(selectedWire);
        }

        void SceneClick(Vector2 mouse)
        {
            if (mode == EditMode.Build && !PartUnder(mouse, out _)) SelectNothing();
            if (mode != EditMode.Wire) return;
            string? pin = PinUnder(mouse);
            if (pin != null)
            {
                PinClicked(pin);
                return;
            }
            int wire = WireUnder(mouse);
            if (wire >= 0) SelectWire(wire);
            else if (wireStart != null) CancelWire();
            else SelectNothing();
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
            string text = "";
            hoveredPin = null;
            if (!overUi && !leftDown && !rightDown && !panning)
            {
                if (mode == EditMode.Build && PartUnder(mouse, out var partId))
                {
                    text = PartCatalog.Get(Design.Find(partId)?.Part ?? "")?.Name ?? partId;
                }
                else if (mode == EditMode.Wire)
                {
                    hoveredPin = PinUnder(mouse);
                    if (hoveredPin != null)
                    {
                        text = PinDescription(hoveredPin);
                    }
                    else
                    {
                        int wire = WireUnder(mouse);
                        if (wire >= 0) text = WireText(Design.Wires[wire]);
                    }
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
                undo.Add(dragBefore);
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
                sideContent.Add(Classed(new Label($"{partDef.SizeX:0.#} × {partDef.SizeZ:0.#} × {partDef.SizeY:0.#} mm · {partDef.MassG:0.#} g"), "info-text"));
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

        /// <summary>The nearest pin marker within 14 pixels of the mouse, as "part/pin".</summary>
        string? PinUnder(Vector2 mouse)
        {
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
            return best;
        }

        /// <summary>The wire whose curve passes within 7 pixels of the mouse, or -1.</summary>
        int WireUnder(Vector2 mouse)
        {
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
            return best;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + t * ab);
        }

        /// <summary>The first click chooses where a wire starts, the second where it ends.</summary>
        void PinClicked(string key)
        {
            if (wireStart == null)
            {
                wireStart = key;
                selectedWire = -1;
                shown?.HighlightWire(-1);
                renderSide?.Invoke();
                return;
            }
            if (wireStart == key)
            {
                CancelWire();
                return;
            }
            var (fromPart, fromPin) = SplitKey(wireStart);
            var (toPart, toPin) = SplitKey(key);
            wireStart = null;
            PushUndo();
            if (Design.AddWire(fromPart, fromPin, toPart, toPin, ColourFor(fromPart, fromPin, toPart, toPin)) == null)
            {
                undo.RemoveAt(undo.Count - 1);
                ShowToast(Tr("wire.duplicate"));
                renderSide?.Invoke();
                return;
            }
            selectedWire = Design.Wires.Count - 1;
            DesignChanged();
            shown?.HighlightWire(selectedWire);
        }

        void CancelWire()
        {
            wireStart = null;
            shown?.HighlightPins(hoveredPin, null);
            renderSide?.Invoke();
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
                var data = BodyBuilder.BuildData(Design.Body.Clone()); // exactly the current body, whatever the model shows
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

            // Two classic mistakes: the sensor's ground wire missing, then the battery on the Uno's 5V pin.
            int sensorGround = Design.Wires.FindIndex(w => w.FromPart == "sonar1" && w.FromPin == "GND");
            RemoveWire(sensorGround);
            var noGround = CircuitAnalysis.Analyse(Design);
            Undo();
            PinClicked("battery1/+");
            PinClicked("uno1/5V");
            var burning = CircuitAnalysis.Analyse(Design);
            renderSide?.Invoke();
            yield return Frames(4);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-wire-warning"));
            Undo();
            bool restored = !CircuitAnalysis.Analyse(Design).HasProblems && Design.Wires.Count == 16;
            report.AppendLine($"  mistakes: without the sensor's GND wire the check says {string.Join(", ", noGround.Warnings.FindAll(w => !w.Info))}; " +
                              $"battery + on the Uno 5V pin says {string.Join(", ", burning.Warnings.FindAll(w => !w.Info))} (board damaged: {burning.BoardDamaged}); " +
                              $"both undone: {(restored ? "yes" : "NO")}");

            // The robot runs the obstacle-avoider sketch; START then takes it to the arena.
            robot.SketchFile = RobotProject.GoldenSketch;
            robot.SketchText = "";
            robot.UploadedText = "";
            robot.ProgramBytes = 2954;
            CloseSide();
            yield return Frames(6);
            yield return SpikeReport.Capture(SpikeReport.Shot("garage-scratch"));
            report.AppendLine($"  back on the turntable: {robot.PartCount} parts, {robot.MassKg * 1000:F0} g; screenshots -garage-body, -garage-build, " +
                              "-garage-wire, -garage-wire-warning, -garage-scratch");
        }
    }
}
