using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>Marks a part's collider for the Garage's Build mode; pins and wires are picked on the screen.</summary>
    public sealed class Pickable : MonoBehaviour
    {
        public string PartId = "";
    }

    /// <summary>
    /// The robot's look, built from its <see cref="RobotDesign"/>: the body's shapes in their materials, every
    /// part where the player put it and turned it, each motor's wheel on its shaft, and each jumper wire between
    /// its two pins, in the chassis frame (y up from the floor the robot stands on, +z forward). Used by the
    /// Garage turntable, its thumbnails, its edit modes and the arena robot. Real proportions from docs/09;
    /// details such as header holes, male pins, terminal screws and Dupont housings show where wires go.
    /// Jumpers are smooth tubes that leave each pin the way a real wire does (up from a header, sideways
    /// out of a screw terminal, along a lead).
    /// </summary>
    public sealed class RobotVisuals
    {
        const float Mm = 0.001f;
        const float WireRadius = 0.0008f;   // a jumper is about 1.6 mm thick
        const float MarkerSize = 0.0024f;

        static Mesh? cube, cylinder, sphere;

        public static readonly Dictionary<string, Color> WireColors = new Dictionary<string, Color>
        {
            ["red"] = new Color(0.85f, 0.10f, 0.10f),
            ["black"] = new Color(0.08f, 0.08f, 0.09f),
            ["yellow"] = new Color(0.95f, 0.80f, 0.10f),
            ["green"] = new Color(0.15f, 0.70f, 0.25f),
            ["blue"] = new Color(0.15f, 0.40f, 0.90f),
            ["white"] = new Color(0.92f, 0.92f, 0.92f),
            ["orange"] = new Color(0.95f, 0.45f, 0.10f),
            ["purple"] = new Color(0.55f, 0.25f, 0.75f),
            ["grey"] = new Color(0.55f, 0.56f, 0.58f),
            ["brown"] = new Color(0.45f, 0.26f, 0.12f),
        };

        readonly Material template;
        readonly Material bodyMaterial;
        readonly Material hubMaterial;
        readonly List<Material> owned = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly Dictionary<string, Material> wireMaterials = new Dictionary<string, Material>();
        readonly Dictionary<PinKind, Material> pinMaterials = new Dictionary<PinKind, Material>();
        readonly List<GameObject?> wireGroups = new List<GameObject?>();
        readonly List<MeshRenderer?> wireRenderers = new List<MeshRenderer?>();
        readonly List<Mesh?> wireMeshes = new List<Mesh?>();
        readonly Dictionary<string, PinKind> pinKinds = new Dictionary<string, PinKind>();
        readonly List<(MeshRenderer renderer, BodyMaterial material, string colour)> bodyRenderers = new List<(MeshRenderer, BodyMaterial, string)>();
        BodyMeshes? body;
        Material black = null!, holes = null!, metal = null!, darkMetal = null!, gold = null!, tin = null!, activePin = null!, selectedWire = null!;
        Transform wiresRoot = null!, pinsRoot = null!;
        string importFolder = "";
        string? hoveredPin, chosenPin;
        int highlightedWire = -1;
        RobotDesign design = null!;

        public GameObject Root { get; }
        public Dictionary<string, GameObject> Parts { get; } = new Dictionary<string, GameObject>();

        /// <summary>The LEDs of each part by part id and LED name ("ON", "L", "TX", "RX" on the Uno), for the arena to light.</summary>
        public Dictionary<string, Dictionary<string, MeshRenderer>> Lights { get; } = new Dictionary<string, Dictionary<string, MeshRenderer>>();

        /// <summary>Each servo's horn by part id, on its output spline: the arena turns it about its own y.</summary>
        public Dictionary<string, Transform> Horns { get; } = new Dictionary<string, Transform>();
        public double BodyVolumeMm3 => body?.VolumeMm3 ?? 0;
        public BodyMeshes? Body => body;

        /// <summary>Pin markers by "part/pin" key (Wire mode).</summary>
        public Dictionary<string, Transform> PinMarkers { get; } = new Dictionary<string, Transform>();

        /// <summary>Each wire's curve in the robot's local frame (metres), by wire index; null for a wire whose pins are gone.</summary>
        public List<Vector3[]?> WirePaths { get; } = new List<Vector3[]?>();

        /// <summary>Unity's cylinder mesh: 1 unit across, 2 units tall. The arena uses it for a round body's collider.</summary>
        public static Mesh CylinderMesh
        {
            get
            {
                EnsureMeshes();
                return cylinder!;
            }
        }

        RobotVisuals(Transform chassis, Material template)
        {
            this.template = template;
            Root = new GameObject("RobotVisual");
            Root.transform.SetParent(chassis, false);
            bodyMaterial = Mat(Color.white, 0.5f, 0f);
            hubMaterial = Mat(Color.white, 0.3f, 0f);
        }

        /// <param name="wheels">The physics robot's wheel bodies by motor id; null puts each wheel on its motor (Garage).</param>
        /// <param name="pickable">Colliders on the parts, for the Garage's Body Studio.</param>
        /// <param name="pins">Pin markers, for the Wire mode.</param>
        /// <param name="prebuiltBody">A body already built (the Body Studio's worker); the visual owns it from now on.</param>
        public static RobotVisuals Build(Transform chassis, IReadOnlyDictionary<string, Transform>? wheels, RobotProject project, Material template,
            bool pickable = false, bool pins = false, BodyMeshes? prebuiltBody = null)
        {
            EnsureMeshes();
            BodyLook.EnsureInit(template);
            var v = new RobotVisuals(chassis, template);
            v.design = project.Design;
            v.importFolder = project.ImportFolder;
            v.black = v.Mat(new Color(0.06f, 0.06f, 0.07f), 0.35f, 0f);
            v.holes = v.Mat(new Color(0.01f, 0.01f, 0.012f), 0.1f, 0f);
            v.metal = v.Mat(new Color(0.80f, 0.81f, 0.83f), 0.8f, 1f);
            v.darkMetal = v.Mat(new Color(0.25f, 0.26f, 0.28f), 0.6f, 1f);
            v.gold = v.Mat(new Color(0.86f, 0.70f, 0.32f), 0.75f, 1f);
            v.tin = v.Mat(new Color(0.72f, 0.72f, 0.70f), 0.7f, 1f);
            v.selectedWire = v.Mat(new Color(0.31f, 0.76f, 1.0f), 0.7f, 0f);
            v.wiresRoot = Group(v.Root.transform, "Wires", Vector3.zero);
            v.pinsRoot = Group(v.Root.transform, "Pins", Vector3.zero);
            var design = project.Design;
            v.BuildBody(design.Body, prebuiltBody);
            foreach (var part in design.Parts) v.BuildPart(design, part, wheels != null && wheels.TryGetValue(part.Id, out var wheel) ? wheel : null, pickable);
            v.BuildWires(design);
            if (pins) v.BuildPinMarkers(design);
            v.ApplyFinishes(project);
            v.LightFromWiring();
            return v;
        }

        /// <summary>Moves a part to its place and turn in the design and redraws its wires (a drag in the Body Studio).</summary>
        public void MovePart(RobotDesign design, string partId)
        {
            this.design = design;
            var part = design.Find(partId);
            if (part == null || !Parts.TryGetValue(partId, out var go)) return;
            go.transform.localPosition = new Vector3(part.X, part.Y, part.Z) * Mm;
            go.transform.localRotation = Quaternion.Euler(part.RotX, part.Rotation, part.RotZ);
            for (int i = 0; i < design.Wires.Count; i++)
            {
                var w = design.Wires[i];
                if (w.FromPart == partId || w.ToPart == partId) BuildWire(design, i, quick: true);
            }
        }

        /// <summary>Shows one wire in the selection colour (-1 clears it).</summary>
        public void HighlightWire(int index)
        {
            if (highlightedWire == index) return;
            SetWireMaterial(highlightedWire, false);
            highlightedWire = index;
            SetWireMaterial(index, true);
        }

        void SetWireMaterial(int index, bool selected)
        {
            if (index < 0 || index >= wireRenderers.Count || wireRenderers[index] == null || index >= design.Wires.Count) return;
            wireRenderers[index]!.sharedMaterial = selected ? selectedWire : WireMaterial(design.Wires[index].Color);
        }

        /// <summary>Enlarges the pin under the mouse and marks the pin a new wire starts from.</summary>
        public void HighlightPins(string? hovered, string? chosen)
        {
            if (hovered == hoveredPin && chosen == chosenPin) return;
            string? oldHovered = hoveredPin, oldChosen = chosenPin;
            hoveredPin = hovered;
            chosenPin = chosen;
            foreach (string? key in new[] { oldHovered, oldChosen, hovered, chosen })
            {
                if (key == null || !PinMarkers.TryGetValue(key, out var marker)) continue;
                bool isChosen = key == chosenPin, isHovered = key == hoveredPin;
                marker.localScale = Vector3.one * MarkerSize * (isChosen ? 1.9f : isHovered ? 1.6f : 1f);
                marker.GetComponent<MeshRenderer>().sharedMaterial = isChosen ? activePin : pinMaterials[pinKinds[key]];
            }
        }

        /// <summary>Where to point the camera to wire a part: the middle of its pins (world space).</summary>
        public Vector3 FocusPoint(string partId)
        {
            var sum = Vector3.zero;
            int n = 0;
            foreach (var entry in PinMarkers)
            {
                if (!entry.Key.StartsWith(partId + "/")) continue;
                sum += entry.Value.position;
                n++;
            }
            if (n > 0) return sum / n;
            return Parts.TryGetValue(partId, out var go) ? go.transform.position : Root.transform.position;
        }

        /// <summary>
        /// The wheels' finish, and the body's: as built (each shape in its own material) or one paint over all of it.
        /// </summary>
        public void ApplyFinishes(RobotProject project)
        {
            bool asBuilt = project.ActiveBodyFinish == Finishes.AsBuilt;
            if (!asBuilt) Apply(bodyMaterial, Finishes.Get(project.ActiveBodyFinish, FinishTarget.Body));
            foreach (var (renderer, material, colour) in bodyRenderers)
                renderer.sharedMaterial = asBuilt ? BodyLook.Get(material, colour) : bodyMaterial;
            Apply(hubMaterial, Finishes.Get(project.ActiveWheelFinish, FinishTarget.Wheels));
        }

        GameObject? selectionFrame;

        /// <summary>Draws a bright frame around the selected part (null clears it).</summary>
        public void Highlight(string? partId)
        {
            if (selectionFrame != null) Object.Destroy(selectionFrame);
            selectionFrame = null;
            if (partId == null || !Parts.TryGetValue(partId, out var part)) return;
            var collider = part.GetComponent<BoxCollider>();
            if (collider == null) return;
            selectionFrame = new GameObject("SelectionFrame");
            selectionFrame.transform.SetParent(part.transform, false);
            var material = Mat(new Color(0.31f, 0.76f, 1.0f), 0.2f, 0f);
            Vector3 c = collider.center, h = collider.size / 2 + Vector3.one * 0.0015f;
            const float bar = 0.0012f;
            foreach (float y in new[] { -h.y, h.y })
            {
                foreach (float z in new[] { -h.z, h.z }) Box(selectionFrame.transform, c + new Vector3(0, y, z), new Vector3(2 * h.x, bar, bar), material, "Edge");
                foreach (float x in new[] { -h.x, h.x }) Box(selectionFrame.transform, c + new Vector3(x, y, 0), new Vector3(bar, bar, 2 * h.z), material, "Edge");
            }
            foreach (float x in new[] { -h.x, h.x })
                foreach (float z in new[] { -h.z, h.z }) Box(selectionFrame.transform, c + new Vector3(x, 0, z), new Vector3(bar, 2 * h.y, bar), material, "Edge");
        }

        public void Destroy()
        {
            Object.Destroy(Root);
            foreach (var material in owned) Object.Destroy(material);
            foreach (var mesh in ownedMeshes) Object.Destroy(mesh);
            foreach (var mesh in wireMeshes) if (mesh != null) Object.Destroy(mesh);
            body?.Destroy();
        }

        static void Apply(Material material, Finish finish)
        {
            material.SetColor("_BaseColor", finish.Color);
            material.SetFloat("_Smoothness", finish.Smoothness);
            material.SetFloat("_Metallic", finish.Metallic);
        }

        // ------------------------------------------------------------------ body

        /// <summary>Each solid piece of the body in its material; open imported meshes as PLA.</summary>
        void BuildBody(BodyDesign design, BodyMeshes? prebuilt)
        {
            body = prebuilt ?? BodyBuilder.Build(design, importFolder);
            foreach (var solid in body.Solids)
            {
                var go = MeshObject("Body " + solid.Id, Root.transform, solid.Mesh, Vector3.zero, BodyLook.Get(solid.Material, solid.Colour));
                bodyRenderers.Add((go.GetComponent<MeshRenderer>(), solid.Material, solid.Colour));
            }
            foreach (var loose in body.Loose)
            {
                var go = MeshObject("Imported", Root.transform, loose, Vector3.zero, BodyLook.Get(BodyMaterial.Pla, ""));
                bodyRenderers.Add((go.GetComponent<MeshRenderer>(), BodyMaterial.Pla, ""));
            }
        }

        // ------------------------------------------------------------------ parts

        void BuildPart(RobotDesign design, PartInstance part, Transform? wheelBody, bool pickable)
        {
            var def = PartCatalog.Get(part.Part);
            if (def == null) return;
            var root = Group(Root.transform, part.Id, new Vector3(part.X, part.Y, part.Z) * Mm);
            root.localRotation = Quaternion.Euler(part.RotX, part.Rotation, part.RotZ);
            Parts[part.Id] = root.gameObject;
            var lights = new Dictionary<string, MeshRenderer>();
            if (CoreEngine.Spike.Parts.PartModels.Build(part.Part, root, lights))
            {
                if (lights.Count > 0) Lights[part.Id] = lights;
                if (def.Kind == PartKind.Motor) BuildModelWheel(part, root, wheelBody);
                if (def.Kind == PartKind.MotorDriver) BuildJumpers(design, part, root, def);
                if (def.Kind == PartKind.Servo) BuildHorn(part, root);
            }
            else switch (def.Kind)
            {
                case PartKind.Board: BuildUno(root, def); break;
                case PartKind.MotorDriver: BuildL298N(design, part, root, def); break;
                case PartKind.Ultrasonic: BuildSonar(root, def); break;
                case PartKind.Motor: BuildMotor(part, root, wheelBody); break;
                case PartKind.Battery: BuildBattery(root); break;
                case PartKind.Caster: BuildCaster(root); break;
            }
            if (pickable)
            {
                var collider = root.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(def.SizeX, def.SizeY, def.SizeZ) * Mm;
                collider.center = new Vector3(def.BoxCentre.x, def.BoxCentre.y, def.BoxCentre.z) * Mm;
                root.gameObject.AddComponent<Pickable>().PartId = part.Id;
            }
        }

        /// <summary>The motor's wheel on its shaft: its own mesh, since it turns; the hub in the wheels' finish.</summary>
        void BuildModelWheel(PartInstance part, Transform t, Transform? wheelBody)
        {
            var hub = DesignGeometry.WheelInMotor;
            var wheel = wheelBody ?? Group(t, part.Id + ".wheel", new Vector3(hub.x, hub.y, hub.z) * Mm);
            var go = new GameObject("Wheel");
            go.transform.SetParent(wheel, false);
            go.AddComponent<MeshFilter>().sharedMesh = CoreEngine.Spike.Parts.MotorModel.WheelMesh;
            var materials = (Material[])CoreEngine.Spike.Parts.MotorModel.WheelMaterials.Clone();
            materials[CoreEngine.Spike.Parts.MotorModel.HubSlot] = hubMaterial;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        void BuildHorn(PartInstance part, Transform t)
        {
            var horn = new GameObject("Horn").transform;
            horn.SetParent(t, false);
            horn.localPosition = CoreEngine.Spike.Parts.ServoModel.HornPivot * Mm;
            horn.gameObject.AddComponent<MeshFilter>().sharedMesh = CoreEngine.Spike.Parts.ServoModel.HornMesh;
            horn.gameObject.AddComponent<MeshRenderer>().sharedMaterials = CoreEngine.Spike.Parts.ServoModel.HornMaterials;
            Horns[part.Id] = horn;
        }

        /// <summary>Switches one of a part's LEDs ("ON", "L", "PWR", "LED"…) on or off.</summary>
        public void SetLight(string partId, string name, bool on)
        {
            if (!Lights.TryGetValue(partId, out var lights) || !lights.TryGetValue(name, out var renderer)) return;
            var part = design.Find(partId);
            if (part != null) CoreEngine.Spike.Parts.PartModels.SetLight(part.Part, renderer, name, on);
        }

        /// <summary>
        /// The power LEDs as the wiring leaves them, before any sketch runs: the Uno's ON and the L298N's PWR glow
        /// when their board has power, as on a real robot with its batteries in.
        /// </summary>
        void LightFromWiring()
        {
            if (Lights.Count == 0) return;
            var circuit = CircuitAnalysis.Analyse(design);
            foreach (var entry in Lights)
                foreach (var light in entry.Value)
                    SetLight(entry.Key, light.Key, (light.Key == "ON" && circuit.BoardPowered) || (light.Key == "PWR" && circuit.DriverPowered));
        }

        /// <summary>The L298N's ENA and ENB jumpers, each on its pin and the one behind it until a wire takes the pin.</summary>
        void BuildJumpers(RobotDesign design, PartInstance part, Transform t, PartDef def)
        {
            foreach (string id in new[] { "ENA", "ENB" })
            {
                var pin = def.Pin(id);
                if (pin == null || design.WiresOn(part.Id, id) > 0) continue;
                MeshObject(id + " jumper", t, CoreEngine.Spike.Parts.L298NModel.JumperMesh, new Vector3(pin.X, CoreEngine.Spike.Parts.L298NModel.PinFoot, pin.Z) * Mm, black);
            }
        }

        /// <summary>Arduino Uno R3 (docs/09 §2.1): 68.6 × 53.4 mm, USB-B at −x, digital header along +z.</summary>
        void BuildUno(Transform t, PartDef def)
        {
            Box(t, new Vector3(0, 0.0024f, 0), new Vector3(0.0686f, 0.0016f, 0.0534f), Mat(new Color(0.00f, 0.47f, 0.55f), 0.45f, 0f), "PCB");
            Box(t, new Vector3(0.012f, 0.0052f, -0.008f), new Vector3(0.035f, 0.004f, 0.0076f), black, "ATmega328P");
            foreach (float side in new[] { -1f, 1f }) // the chip's two rows of legs
                Box(t, new Vector3(0.012f, 0.0042f, -0.008f + side * 0.0042f), new Vector3(0.033f, 0.0022f, 0.0012f), tin, "Legs");
            Box(t, new Vector3(-0.0313f, 0.0085f, 0.0085f), new Vector3(0.016f, 0.011f, 0.012f), metal, "USB-B");
            Box(t, new Vector3(-0.0303f, 0.0085f, -0.017f), new Vector3(0.014f, 0.011f, 0.009f), black, "DCJack");
            Box(t, new Vector3(-0.009f, 0.0045f, -0.012f), new Vector3(0.0045f, 0.0026f, 0.0012f), metal, "Crystal16MHz");
            Box(t, new Vector3(-0.026f, 0.0045f, 0.020f), new Vector3(0.006f, 0.0026f, 0.006f), metal, "ResetButton");
            Box(t, new Vector3(-0.026f, 0.0059f, 0.020f), new Vector3(0.003f, 0.001f, 0.003f), Mat(new Color(0.75f, 0.1f, 0.1f), 0.4f, 0f), "ResetCap");
            Header(t, 23.0f, 40.8f, 24.1f);  // AREF … D8
            Header(t, 44.9f, 62.7f, 24.1f);  // D7 … D0
            Header(t, 30.5f, 43.2f, -24.2f); // RESET … VIN
            Header(t, 50.8f, 63.5f, -24.2f); // A0 … A5
            // A square hole on top of the female header for every pin: where a jumper goes in.
            foreach (var pin in def.Pins)
                Box(t, new Vector3(pin.X, pin.Y + 0.06f, pin.Z) * Mm, new Vector3(1.1f, 0.12f, 1.1f) * Mm, holes, "Hole " + pin.Id);
        }

        void Header(Transform t, float fromLeft, float toLeft, float z)
        {
            float centre = (fromLeft + toLeft) / 2 - 34.3f, length = toLeft - fromLeft + 2.54f;
            Box(t, new Vector3(centre, 6.8f, z) * Mm, new Vector3(length, 8.5f, 2.5f) * Mm, black, "Header");
        }

        /// <summary>
        /// L298N module: 43 × 43 mm; male logic header along +z (ENA and ENB carry jumpers unless a wire took the
        /// jumper's place), power terminal along −z, motor terminals at the sides.
        /// </summary>
        void BuildL298N(RobotDesign design, PartInstance part, Transform t, PartDef def)
        {
            Box(t, new Vector3(0, 0.0024f, 0), new Vector3(0.043f, 0.0016f, 0.043f), Mat(new Color(0.75f, 0.08f, 0.08f), 0.45f, 0f), "PCB");
            // Heatsink with fins over the L298 bridge.
            Box(t, new Vector3(0, 0.0045f, 0.002f), new Vector3(0.023f, 0.0026f, 0.012f), black, "HeatsinkBase");
            for (int i = 0; i < 6; i++)
                Box(t, new Vector3(-0.0105f + i * 0.0042f, 0.0165f, 0.002f), new Vector3(0.0012f, 0.022f, 0.012f), black, "Fin");
            foreach (float x in new[] { -0.0155f, 0.0155f }) // electrolytic capacitors
            {
                Cylinder(t, new Vector3(x, 0.0085f, -0.0105f), 0.0063f, 0.011f, Axis.Y, Mat(new Color(0.08f, 0.16f, 0.45f), 0.5f, 0f), "Capacitor");
                Cylinder(t, new Vector3(x, 0.0142f, -0.0105f), 0.0055f, 0.0006f, Axis.Y, metal, "CapacitorTop");
            }
            var blue = Mat(new Color(0.10f, 0.35f, 0.80f), 0.4f, 0f);
            Terminal(t, new Vector3(0, 7.2f, -17), new Vector3(16, 9.5f, 8), blue, def, "+12V", "GND", "+5V");
            Terminal(t, new Vector3(-18, 7.2f, 1.5f), new Vector3(8, 9.5f, 12.5f), blue, def, "OUT1", "OUT2");
            Terminal(t, new Vector3(18, 7.2f, 1.5f), new Vector3(8, 9.5f, 12.5f), blue, def, "OUT3", "OUT4");
            // Male logic header: a black strip with gold pins.
            Box(t, new Vector3(0, 0.00445f, 0.019f), new Vector3(0.0165f, 0.0025f, 0.0025f), black, "HeaderBase");
            foreach (var pin in def.Pins)
            {
                if (pin.Style != PinStyle.Pin) continue;
                Box(t, new Vector3(pin.X, 7.1f, pin.Z) * Mm, new Vector3(0.64f, 7.8f, 0.64f) * Mm, gold, "Pin " + pin.Id);
                // The enable jumpers stay on ENA and ENB until a wire takes that pin.
                if ((pin.Id == "ENA" || pin.Id == "ENB") && design.WiresOn(part.Id, pin.Id) == 0)
                    Box(t, new Vector3(pin.X, 9.5f, pin.Z) * Mm, new Vector3(2.5f, 5f, 2.5f) * Mm, black, pin.Id + " jumper");
            }
            Box(t, new Vector3(0.0135f, 0.0075f, -0.0105f), new Vector3(0.0025f, 0.005f, 0.005f), black, "5V-EN jumper");
        }

        /// <summary>A screw terminal block: blue body, a screw on top and a dark wire opening for each pin.</summary>
        void Terminal(Transform t, Vector3 centreMm, Vector3 sizeMm, Material body, PartDef def, params string[] pins)
        {
            Box(t, centreMm * Mm, sizeMm * Mm, body, "Terminal");
            foreach (string id in pins)
            {
                var pin = def.Pin(id);
                if (pin == null) continue;
                var exit = new Vector3(pin.ExitX, pin.ExitY, pin.ExitZ);
                // A thin dark square on the face the wire enters: 2.6 mm across, 0.26 mm deep.
                Box(t, new Vector3(pin.X, pin.Y, pin.Z) * Mm - exit * 0.0001f, Vector3.Scale(new Vector3(2.6f, 2.6f, 2.6f) * Mm, Vector3.one - Abs(exit) * 0.9f), holes, "Opening " + id);
                // The screw sits on top, above the opening, pushed in from the face.
                var screw = new Vector3(pin.X, centreMm.y + sizeMm.y / 2, pin.Z) * Mm - exit * (Mathf.Min(sizeMm.x, sizeMm.z) * 0.5f * Mm);
                Cylinder(t, screw + new Vector3(0, 0.0004f, 0), 0.0035f, 0.0008f, Axis.Y, metal, "Screw " + id);
                Box(t, screw + new Vector3(0, 0.00085f, 0), new Vector3(0.0028f, 0.0003f, 0.0006f), darkMetal, "Slot " + id);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>HC-SR04 on its bracket, transducers toward +z, four male pins pointing back.</summary>
        void BuildSonar(Transform t, PartDef def)
        {
            Box(t, Vector3.zero, new Vector3(0.045f, 0.02f, 0.0016f), Mat(new Color(0.10f, 0.40f, 0.80f), 0.45f, 0f), "PCB");
            var mesh = Mat(new Color(0.12f, 0.12f, 0.13f), 0.2f, 0f);
            foreach (float x in new[] { -0.013f, 0.013f })
            {
                Cylinder(t, new Vector3(x, 0, 0.0068f), 0.016f, 0.012f, Axis.Z, metal, "Transducer");
                Cylinder(t, new Vector3(x, 0, 0.0070f), 0.0125f, 0.0122f, Axis.Z, mesh, "TransducerMesh");
            }
            Box(t, new Vector3(0, 0.006f, -0.002f), new Vector3(0.010f, 0.003f, 0.004f), metal, "Crystal");
            Box(t, new Vector3(0, -0.009f, -0.002f), new Vector3(0.011f, 0.0025f, 0.0025f), black, "PinHeader");
            foreach (var pin in def.Pins)
                Box(t, new Vector3(pin.X, pin.Y, -6.1f) * Mm, new Vector3(0.64f, 0.64f, 5.8f) * Mm, gold, "Pin " + pin.Id);
            Box(t, new Vector3(0, -0.0125f, -0.004f), new Vector3(0.03f, 0.007f, 0.010f), darkMetal, "Bracket");
        }

        /// <summary>TT gear motor, with the 65 mm wheel on its shaft (−x side) and its red and black leads (+z end).</summary>
        void BuildMotor(PartInstance part, Transform t, Transform? wheelBody)
        {
            var yellow = Mat(new Color(0.98f, 0.76f, 0.10f), 0.35f, 0f);
            Box(t, Vector3.zero, new Vector3(0.019f, 0.022f, 0.037f), yellow, "Gearbox");
            var can = MeshObject("MotorCan", t, ProceduralMeshes.Hub, new Vector3(0, 0.002f, 0.0315f), metal);
            can.transform.localRotation = Quaternion.Euler(0, 90, 0); // the hub mesh turns about x; lay it along z
            can.transform.localScale = new Vector3(0.026f / 0.024f, 0.010f / 0.021f, 0.010f / 0.021f);
            Box(t, new Vector3(0, 0.002f, 0.0448f), new Vector3(0.012f, 0.012f, 0.0012f), black, "EndCap");
            foreach (float y in new[] { 0.004f, -0.004f })
                Box(t, new Vector3(0, y, 0.0452f), new Vector3(0.0022f, 0.0012f, 0.0012f), tin, "Tab");
            var hub = DesignGeometry.WheelInMotor;
            float shaftLength = Mathf.Abs(hub.x) * Mm;
            Cylinder(t, new Vector3(hub.x / 2, hub.y, hub.z) * Mm, 0.0054f, shaftLength, Axis.X, black, "Shaft");
            var wheel = wheelBody ?? Group(t, part.Id + ".wheel", new Vector3(hub.x, hub.y, hub.z) * Mm);
            BuildWheel(wheel);
        }

        /// <summary>A 65 mm wheel: smooth rubber tyre, a hub in the chosen finish and three spokes that show it turning.</summary>
        void BuildWheel(Transform parent)
        {
            var rubber = Mat(new Color(0.045f, 0.045f, 0.05f), 0.28f, 0f);
            MeshObject("Tyre", parent, ProceduralMeshes.Tyre, Vector3.zero, rubber);
            MeshObject("Hub", parent, ProceduralMeshes.Hub, Vector3.zero, hubMaterial);
            var spoke = Mat(new Color(0.15f, 0.15f, 0.16f), 0.3f, 0f);
            for (int i = 0; i < 3; i++)
            {
                var s = Box(parent, Vector3.zero, new Vector3(0.0255f, 0.036f, 0.004f), spoke, "Spoke");
                s.transform.localRotation = Quaternion.Euler(i * 60f, 0, 0);
            }
            Cylinder(parent, Vector3.zero, 0.009f, 0.0262f, Axis.X, darkMetal, "Axle");
        }

        /// <summary>4×AA holder with its cells; the red and black leads leave from its back end.</summary>
        void BuildBattery(Transform t)
        {
            Box(t, new Vector3(0, -0.0045f, 0), new Vector3(0.058f, 0.006f, 0.062f), black, "HolderFloor");
            foreach (float x in new[] { -0.0285f, 0.0285f })
                Box(t, new Vector3(x, 0, 0), new Vector3(0.001f, 0.015f, 0.062f), black, "HolderSide");
            foreach (float z in new[] { -0.0305f, 0.0305f })
                Box(t, new Vector3(0, 0, z), new Vector3(0.058f, 0.015f, 0.001f), black, "HolderEnd");
            var cell = Mat(new Color(0.85f, 0.65f, 0.15f), 0.6f, 0.4f);
            var wrapper = Mat(new Color(0.08f, 0.08f, 0.09f), 0.5f, 0.2f);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.0217f + i * 0.0145f;
                Cylinder(t, new Vector3(x, 0.001f, -0.004f), 0.0142f, 0.042f, Axis.Z, cell, "AA cell");
                Cylinder(t, new Vector3(x, 0.001f, 0.021f), 0.0142f, 0.008f, Axis.Z, wrapper, "AA cell end");
            }
        }

        void BuildCaster(Transform t)
        {
            Cylinder(t, new Vector3(0, 0.014f, 0), 0.022f, 0.022f, Axis.Y, darkMetal, "CasterHolder");
            Sphere(t, Vector3.zero, 0.02f, metal, "CasterBall");
        }

        // ------------------------------------------------------------------ wires and pins

        void BuildWires(RobotDesign design)
        {
            for (int i = 0; i < design.Wires.Count; i++) BuildWire(design, i);
        }

        // Routes by the design's shapes and the wire's pins (WireRouter.Key and the pin names), kept between builds:
        // adding a wire, switching modes or drawing a thumbnail lays out only what changed.
        static readonly Dictionary<string, Vector3[]> routes = new Dictionary<string, Vector3[]>();
        WireRouter? router;

        /// <summary>Wires laid round the parts and the body in this build, and those left as plain arches.</summary>
        public int RoutedWires { get; private set; }
        public int PlainWires { get; private set; }

        /// <summary>Milliseconds spent finding new routes in this build (routes kept from earlier builds cost nothing).</summary>
        public double RouteMs { get; private set; }

        /// <summary>Forgets every kept route (for measuring).</summary>
        public static void ClearRoutes() => routes.Clear();

        /// <summary>The wire's way round the parts and the body (robot frame, metres), or null when there is none.</summary>
        Vector3[]? Route(RobotDesign design, WireInstance wire)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            router ??= new WireRouter(design);
            string key = $"{router.Key}|{wire.FromPart}.{wire.FromPin}|{wire.ToPart}.{wire.ToPin}";
            if (routes.TryGetValue(key, out var cached)) return cached;
            var points = router.Route(design, wire);
            RouteMs += watch.Elapsed.TotalMilliseconds;
            if (points == null) return null;
            var route = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++) route[i] = new Vector3(points[i].x, points[i].y, points[i].z) * Mm;
            if (routes.Count > 512) routes.Clear();
            routes[key] = route;
            return route;
        }

        /// <summary>
        /// One jumper: a Dupont housing on header pins (on top of a female header, over a male pin), a tinned end in
        /// a screw terminal, nothing extra on a part's own lead; between the ends a smooth tube laid by
        /// <see cref="WireRouter"/>: out of the pin along its exit, then over the parts, round the plates' edges or
        /// through a hole, never through anything. While a part is being dragged (<paramref name="quick"/>) its
        /// wires are plain arches, laid properly once it is let go.
        /// </summary>
        void BuildWire(RobotDesign design, int index, bool quick = false)
        {
            while (wireGroups.Count <= index) wireGroups.Add(null);
            while (wireRenderers.Count <= index) wireRenderers.Add(null);
            while (wireMeshes.Count <= index) wireMeshes.Add(null);
            while (WirePaths.Count <= index) WirePaths.Add(null);
            if (wireGroups[index] != null) Object.Destroy(wireGroups[index]);
            if (wireMeshes[index] != null) Object.Destroy(wireMeshes[index]);
            wireGroups[index] = null;
            wireRenderers[index] = null;
            wireMeshes[index] = null;
            WirePaths[index] = null;

            var wire = design.Wires[index];
            var a = PinFrame(design, wire.FromPart, wire.FromPin);
            var b = PinFrame(design, wire.ToPart, wire.ToPin);
            if (a == null || b == null) return;
            var group = Group(wiresRoot, $"Wire {index}", Vector3.zero);
            Vector3 startA = WireEnd(group, a.Value.position, a.Value.exit, a.Value.style);
            Vector3 startB = WireEnd(group, b.Value.position, b.Value.exit, b.Value.style);

            var route = quick ? null : Route(design, wire);
            List<Vector3> curve;
            if (route != null)
            {
                curve = new List<Vector3>(route);
                RoutedWires++;
            }
            else
            {
                PlainWires++;
                // A plain arch: a 10 cm jumper rises about 4 cm, as a real one does when it is not pressed flat.
                float distance = Vector3.Distance(startA, startB);
                float reach = Mathf.Clamp(distance * 0.3f, 0.008f, 0.04f);
                var lift = Vector3.up * (0.004f + 0.08f * distance);
                Vector3 p1 = startA + a.Value.exit * reach + lift, p2 = startB + b.Value.exit * reach + lift;
                const int samples = 28;
                curve = new List<Vector3>(samples + 1);
                for (int i = 0; i <= samples; i++)
                {
                    float s = i / (float)samples, u = 1 - s;
                    curve.Add(u * u * u * startA + 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s * startB);
                }
            }
            var mesh = ProceduralMeshes.Tube(curve, WireRadius, 8);
            var tube = MeshObject("Jumper", group, mesh, Vector3.zero, index == highlightedWire ? selectedWire : WireMaterial(wire.Color));

            var path = new List<Vector3> { a.Value.position };
            path.AddRange(curve);
            path.Add(b.Value.position);
            wireGroups[index] = group.gameObject;
            wireRenderers[index] = tube.GetComponent<MeshRenderer>();
            wireMeshes[index] = mesh;
            WirePaths[index] = path.ToArray();
        }

        /// <summary>A pin's place, exit direction and style in the robot's frame (metres).</summary>
        static (Vector3 position, Vector3 exit, PinStyle style)? PinFrame(RobotDesign design, string partId, string pinId)
        {
            var p = DesignGeometry.PinPosition(design, partId, pinId);
            var e = DesignGeometry.PinExit(design, partId, pinId);
            var part = design.Find(partId);
            var pin = part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
            if (p == null || e == null || pin == null) return null;
            return (new Vector3(p.Value.x, p.Value.y, p.Value.z) * Mm, new Vector3(e.Value.x, e.Value.y, e.Value.z).normalized, pin.Style);
        }

        /// <summary>Draws the connector at a pin and returns where the bare wire starts.</summary>
        Vector3 WireEnd(Transform group, Vector3 pin, Vector3 exit, PinStyle style)
        {
            var orientation = Quaternion.LookRotation(exit, Mathf.Abs(exit.y) > 0.9f ? Vector3.forward : Vector3.up);
            switch (style)
            {
                case PinStyle.Header: // male jumper end in a female header: the housing stands on top
                {
                    var housing = Box(group, pin + exit * 0.007f, new Vector3(0.0025f, 0.0025f, 0.014f), black, "Dupont");
                    housing.transform.localRotation = orientation;
                    return pin + exit * WireRouter.StartOffset(style) * Mm;
                }
                case PinStyle.Pin: // female jumper end over a male pin: the housing covers the pin
                {
                    var housing = Box(group, pin + exit * 0.001f, new Vector3(0.0025f, 0.0025f, 0.014f), black, "Dupont");
                    housing.transform.localRotation = orientation;
                    return pin + exit * WireRouter.StartOffset(style) * Mm;
                }
                case PinStyle.Terminal: // a tinned end clamped in the terminal
                {
                    var end = Box(group, pin + exit * 0.0005f, new Vector3(0.001f, 0.001f, 0.004f), tin, "TinnedEnd");
                    end.transform.localRotation = orientation;
                    return pin + exit * WireRouter.StartOffset(style) * Mm;
                }
                default: // the part's own lead
                    return pin;
            }
        }

        /// <summary>Round markers just outside each pin, coloured by what the pin carries (Wire mode).</summary>
        void BuildPinMarkers(RobotDesign design)
        {
            pinMaterials[PinKind.Signal] = Mat(new Color(1.00f, 0.85f, 0.15f), 0.5f, 0f);
            pinMaterials[PinKind.Power] = Mat(new Color(1.00f, 0.22f, 0.15f), 0.5f, 0f);
            pinMaterials[PinKind.Ground] = Mat(new Color(0.55f, 0.58f, 0.64f), 0.5f, 0f);
            pinMaterials[PinKind.Motor] = Mat(new Color(1.00f, 0.55f, 0.10f), 0.5f, 0f);
            activePin = Mat(new Color(0.31f, 0.76f, 1.0f), 0.6f, 0f);
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                foreach (var pin in def.Pins)
                {
                    var frame = PinFrame(design, part.Id, pin.Id);
                    if (frame == null) continue;
                    string key = part.Id + "/" + pin.Id;
                    var position = frame.Value.position + frame.Value.exit * (MarkerSize * 0.5f + 0.0004f);
                    var marker = Sphere(pinsRoot, position, MarkerSize, pinMaterials[pin.Kind], "Pin " + key);
                    PinMarkers[key] = marker.transform;
                    pinKinds[key] = pin.Kind;
                }
            }
        }

        Material WireMaterial(string colour)
        {
            if (!wireMaterials.TryGetValue(colour, out var material))
            {
                material = Mat(WireColors.TryGetValue(colour, out var c) ? c : Color.yellow, 0.55f, 0f);
                wireMaterials[colour] = material;
            }
            return material;
        }

        // ------------------------------------------------------------------ helpers

        enum Axis { X, Y, Z }

        Material Mat(Color color, float smoothness, float metallic)
        {
            var material = new Material(template);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            owned.Add(material);
            return material;
        }

        static Transform Group(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Vector3 position, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>A box with slightly rounded edges (a fifth of its thinnest side, at most 0.6 mm), like a real part.</summary>
        static GameObject Box(Transform parent, Vector3 position, Vector3 size, Material material, string name)
        {
            float thinnest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (thinnest < 0.0004f) return Shape(parent, cube!, position, size, Quaternion.identity, material, name);
            return Shape(parent, ProceduralMeshes.RoundedBox(size, Mathf.Min(0.0006f, thinnest * 0.2f)), position, Vector3.one, Quaternion.identity, material, name);
        }

        static GameObject Sphere(Transform parent, Vector3 position, float diameter, Material material, string name) =>
            Shape(parent, sphere!, position, Vector3.one * diameter, Quaternion.identity, material, name);

        /// <summary>A cylinder of the given diameter and length along a local axis (Unity's cylinder mesh is 2 units tall).</summary>
        static GameObject Cylinder(Transform parent, Vector3 position, float diameter, float length, Axis axis, Material material, string name)
        {
            var rotation = axis == Axis.X ? Quaternion.Euler(0, 0, 90) : axis == Axis.Z ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            return Shape(parent, cylinder!, position, new Vector3(diameter, length / 2, diameter), rotation, material, name);
        }

        static GameObject Shape(Transform parent, Mesh mesh, Vector3 position, Vector3 scale, Quaternion rotation, Material material, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static void EnsureMeshes()
        {
            if (cube != null) return;
            cube = PrimitiveMesh(PrimitiveType.Cube);
            cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            sphere = PrimitiveMesh(PrimitiveType.Sphere);
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.SetActive(false); // its collider must not meet the robot before the end of the frame
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }
    }
}
