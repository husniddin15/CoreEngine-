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
    /// The robot's look, built from its <see cref="RobotDesign"/>: the Body Studio plates, every part where it
    /// was placed, wheels on the motors and each jumper wire between its two pins. Primitive meshes without
    /// colliders, in the chassis frame of RobotSpike (origin at the chassis centre, 5 cm above the floor,
    /// +z forward). Used by the Garage turntable, its thumbnails, its edit modes and the arena robot.
    /// Prototype art: real proportions from docs/09, no textures.
    /// </summary>
    public sealed class RobotVisuals
    {
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
        readonly Dictionary<string, Material> wireMaterials = new Dictionary<string, Material>();
        readonly Dictionary<PinKind, Material> pinMaterials = new Dictionary<PinKind, Material>();
        readonly List<GameObject?> wireGroups = new List<GameObject?>();
        readonly Dictionary<string, PinKind> pinKinds = new Dictionary<string, PinKind>();
        BodyMeshes? body;
        Material black = null!, metal = null!, darkMetal = null!, activePin = null!;
        Transform wiresRoot = null!, pinsRoot = null!;
        string? hoveredPin, chosenPin;
        int highlightedWire = -1;

        public GameObject Root { get; }
        public Dictionary<string, GameObject> Parts { get; } = new Dictionary<string, GameObject>();
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

        /// <param name="leftWheel">Wheel bodies of the physics robot; null puts the wheels on the chassis (Garage).</param>
        /// <param name="pickable">Colliders on the parts, for the Garage's Build mode.</param>
        /// <param name="pins">Pin markers, for the Wire mode.</param>
        /// <param name="prebuiltBody">Plates already built (the Body Studio's worker); the visual owns them from now on.</param>
        public static RobotVisuals Build(Transform chassis, Transform? leftWheel, Transform? rightWheel, RobotProject project, Material template,
            bool pickable = false, bool pins = false, BodyMeshes? prebuiltBody = null)
        {
            EnsureMeshes();
            var v = new RobotVisuals(chassis, template);
            v.black = v.Mat(new Color(0.06f, 0.06f, 0.07f), 0.35f, 0f);
            v.metal = v.Mat(new Color(0.80f, 0.81f, 0.83f), 0.8f, 1f);
            v.darkMetal = v.Mat(new Color(0.25f, 0.26f, 0.28f), 0.6f, 1f);
            v.wiresRoot = Group(v.Root.transform, "Wires", Vector3.zero);
            v.pinsRoot = Group(v.Root.transform, "Pins", Vector3.zero);
            var design = project.Design;
            v.BuildBody(design.Body, prebuiltBody);
            foreach (var part in design.Parts) v.BuildPart(design, part, part.Slot == "right" ? rightWheel : leftWheel, pickable);
            v.BuildWires(design);
            if (pins) v.BuildPinMarkers(design);
            v.ApplyFinishes(project);
            return v;
        }

        /// <summary>Moves a deck part to its place in the design and redraws the wires (Build mode drag).</summary>
        public void MovePart(RobotDesign design, string partId)
        {
            var part = design.Find(partId);
            if (part == null || !Parts.TryGetValue(partId, out var go)) return;
            var place = DesignGeometry.Place(design, part);
            go.transform.localPosition = new Vector3(place.x, place.y, place.z) * 0.001f;
            go.transform.localRotation = Quaternion.Euler(0, place.rotation, 0);
            for (int i = 0; i < design.Wires.Count; i++)
            {
                var w = design.Wires[i];
                if (w.FromPart != partId && w.ToPart != partId) continue;
                if (i < wireGroups.Count && wireGroups[i] != null) Object.Destroy(wireGroups[i]);
                BuildWire(design, i);
            }
        }

        /// <summary>Makes one wire thicker and brighter (-1 clears it).</summary>
        public void HighlightWire(int index)
        {
            if (highlightedWire == index) return;
            ScaleWire(highlightedWire, 1 / 1.8f);
            highlightedWire = index;
            ScaleWire(index, 1.8f);
        }

        void ScaleWire(int index, float factor)
        {
            if (index < 0 || index >= wireGroups.Count || wireGroups[index] == null) return;
            foreach (Transform segment in wireGroups[index]!.transform)
            {
                var scale = segment.localScale;
                segment.localScale = new Vector3(scale.x * factor, scale.y, scale.z * factor);
            }
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
                marker.localScale = Vector3.one * (isChosen ? 0.0044f : isHovered ? 0.0038f : 0.0022f);
                marker.GetComponent<MeshRenderer>().sharedMaterial = isChosen ? activePin : pinMaterials[pinKinds[key]];
            }
        }

        public void ApplyFinishes(RobotProject project)
        {
            Apply(bodyMaterial, Finishes.Get(project.ActiveBodyFinish, FinishTarget.Body));
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
            body?.Destroy();
        }

        static void Apply(Material material, Finish finish)
        {
            material.SetColor("_BaseColor", finish.Color);
            material.SetFloat("_Smoothness", finish.Smoothness);
            material.SetFloat("_Metallic", finish.Metallic);
        }

        // ------------------------------------------------------------------ body

        void BuildBody(BodyDesign design, BodyMeshes? prebuilt)
        {
            body = prebuilt ?? BodyBuilder.Build(design);
            float mm = 0.001f;
            if (body.Bottom != null)
            {
                MeshObject("BottomPlate", Root.transform, body.Bottom, new Vector3(0, DesignGeometry.BottomPlateBottom(design) * mm, 0), bodyMaterial);
                MeshObject("TopDeck", Root.transform, body.Top, new Vector3(0, DesignGeometry.TopDeckBottom * mm, 0), bodyMaterial);
                var brass = Mat(new Color(0.78f, 0.62f, 0.25f), 0.7f, 1f);
                float height = (DesignGeometry.TopDeckBottom - DesignGeometry.BottomPlateTop) * mm;
                foreach (var (x, z) in StandoffPlaces(design))
                    Cylinder(Root.transform, new Vector3(x * mm, 0, z * mm), 0.005f, height, Axis.Y, brass, "Standoff");
            }
            else
            {
                MeshObject("Plate", Root.transform, body.Top, new Vector3(0, DesignGeometry.BottomPlateBottom(design) * mm, 0), bodyMaterial);
            }
        }

        static IEnumerable<(float x, float z)> StandoffPlaces(BodyDesign b)
        {
            if (b.Shape == BodyShape.Round)
            {
                float r = b.WidthMm / 2 - 10;
                for (int i = 0; i < 4; i++)
                {
                    float angle = (45 + 90 * i) * Mathf.Deg2Rad;
                    yield return (r * Mathf.Cos(angle), r * Mathf.Sin(angle));
                }
                yield break;
            }
            float inset = b.Shape == BodyShape.Rounded ? Mathf.Max(10, b.CornerRadiusMm * 0.6f) : 10;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    yield return (sx * (b.WidthMm / 2 - inset), sz * (b.EffectiveLength / 2 - inset));
        }

        // ------------------------------------------------------------------ parts

        void BuildPart(RobotDesign design, PartInstance part, Transform? wheelBody, bool pickable)
        {
            var def = PartCatalog.Get(part.Part);
            if (def == null) return;
            var place = DesignGeometry.Place(design, part);
            var root = Group(Root.transform, part.Id, new Vector3(place.x, place.y, place.z) * 0.001f);
            root.localRotation = Quaternion.Euler(0, place.rotation, 0);
            Parts[part.Id] = root.gameObject;
            switch (def.Kind)
            {
                case PartKind.Board: BuildUno(root); break;
                case PartKind.MotorDriver: BuildL298N(root); break;
                case PartKind.Ultrasonic: BuildSonar(root); break;
                case PartKind.Motor: BuildMotor(design, part, root, wheelBody); break;
                case PartKind.Battery: BuildBattery(root); break;
                case PartKind.Caster: BuildCaster(root); break;
            }
            if (pickable)
            {
                var collider = root.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(def.SizeX, def.SizeY, def.SizeZ) * 0.001f;
                collider.center = new Vector3(0, def.Mount == MountKind.Deck ? def.SizeY / 2000f : 0, 0);
                root.gameObject.AddComponent<Pickable>().PartId = part.Id;
            }
        }

        /// <summary>Arduino Uno R3 (docs/09 §2.1): 68.6 × 53.4 mm, USB-B at −x, digital header along +z.</summary>
        void BuildUno(Transform t)
        {
            Box(t, new Vector3(0, 0.0024f, 0), new Vector3(0.0686f, 0.0016f, 0.0534f), Mat(new Color(0.00f, 0.47f, 0.55f), 0.45f, 0f), "PCB");
            Box(t, new Vector3(0.012f, 0.0052f, -0.008f), new Vector3(0.035f, 0.004f, 0.0076f), black, "ATmega328P");
            Box(t, new Vector3(-0.0313f, 0.0085f, 0.0085f), new Vector3(0.016f, 0.011f, 0.012f), metal, "USB-B");
            Box(t, new Vector3(-0.0303f, 0.0085f, -0.017f), new Vector3(0.014f, 0.011f, 0.009f), black, "DCJack");
            Header(t, 23.0f, 40.8f, 24.1f);  // AREF … D8
            Header(t, 44.9f, 62.7f, 24.1f);  // D7 … D0
            Header(t, 30.5f, 43.2f, -24.2f); // RESET … VIN
            Header(t, 50.8f, 63.5f, -24.2f); // A0 … A5
        }

        void Header(Transform t, float fromLeft, float toLeft, float z)
        {
            float centre = (fromLeft + toLeft) / 2 - 34.3f, length = toLeft - fromLeft + 2.54f;
            Box(t, new Vector3(centre, 6.8f, z) * 0.001f, new Vector3(length, 8.5f, 2.5f) * 0.001f, black, "Header");
        }

        /// <summary>L298N module: 43 × 43 mm, header along +z, power terminal along −z, motor terminals at the sides.</summary>
        void BuildL298N(Transform t)
        {
            Box(t, new Vector3(0, 0.0024f, 0), new Vector3(0.043f, 0.0016f, 0.043f), Mat(new Color(0.75f, 0.08f, 0.08f), 0.45f, 0f), "PCB");
            Box(t, new Vector3(0, 0.0155f, 0.002f), new Vector3(0.023f, 0.025f, 0.012f), black, "Heatsink");
            var blue = Mat(new Color(0.10f, 0.35f, 0.80f), 0.4f, 0f);
            Box(t, new Vector3(0, 0.0072f, -0.017f), new Vector3(0.016f, 0.0095f, 0.008f), blue, "PowerTerminal");
            Box(t, new Vector3(-0.018f, 0.0072f, 0.0015f), new Vector3(0.008f, 0.0095f, 0.0125f), blue, "OUT1-2");
            Box(t, new Vector3(0.018f, 0.0072f, 0.0015f), new Vector3(0.008f, 0.0095f, 0.0125f), blue, "OUT3-4");
            Box(t, new Vector3(0, 0.0068f, 0.019f), new Vector3(0.0165f, 0.0085f, 0.0025f), black, "Header");
            Box(t, new Vector3(-0.00635f, 0.0118f, 0.019f), new Vector3(0.0035f, 0.003f, 0.0035f), black, "ENA jumper");
            Box(t, new Vector3(0.00635f, 0.0118f, 0.019f), new Vector3(0.0035f, 0.003f, 0.0035f), black, "ENB jumper");
        }

        /// <summary>HC-SR04 on its bracket, transducers toward +z.</summary>
        void BuildSonar(Transform t)
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
            Box(t, new Vector3(0, -0.0125f, -0.004f), new Vector3(0.03f, 0.007f, 0.010f), darkMetal, "Bracket");
        }

        /// <summary>TT gear motor on its mount, with the 65 mm wheel on the shaft.</summary>
        void BuildMotor(RobotDesign design, PartInstance part, Transform t, Transform? wheelBody)
        {
            var yellow = Mat(new Color(0.98f, 0.76f, 0.10f), 0.35f, 0f);
            Box(t, Vector3.zero, new Vector3(0.019f, 0.022f, 0.037f), yellow, "Gearbox");
            Cylinder(t, new Vector3(0, 0.002f, 0.0315f), 0.02f, 0.026f, Axis.Z, metal, "MotorCan");
            var wheelCentre = DesignGeometry.WheelCentre(design.Body, part.Slot);
            var motorCentre = DesignGeometry.MotorCentre(design.Body, part.Slot);
            float shaftLength = Mathf.Abs(wheelCentre.x - motorCentre.x) * 0.001f;
            float side = part.Slot == "right" ? 1 : -1;
            Cylinder(t, new Vector3(side * shaftLength / 2, 0.0085f, 0), 0.0054f, shaftLength, Axis.X, black, "Shaft");
            var wheel = wheelBody ?? Group(Root.transform, part.Id + ".wheel", new Vector3(wheelCentre.x, wheelCentre.y, wheelCentre.z) * 0.001f);
            BuildWheel(wheel);
        }

        void BuildWheel(Transform parent)
        {
            var tire = Mat(new Color(0.05f, 0.05f, 0.05f), 0.15f, 0f);
            Cylinder(parent, Vector3.zero, 0.065f, 0.026f, Axis.X, tire, "Tire");
            Cylinder(parent, Vector3.zero, 0.042f, 0.028f, Axis.X, hubMaterial, "Hub");
            // Three spokes on the hub make the wheel's rotation visible.
            var spoke = Mat(new Color(0.15f, 0.15f, 0.16f), 0.3f, 0f);
            for (int i = 0; i < 3; i++)
            {
                var s = Box(parent, Vector3.zero, new Vector3(0.0285f, 0.036f, 0.005f), spoke, "Spoke");
                s.transform.localRotation = Quaternion.Euler(i * 60f, 0, 0);
            }
        }

        /// <summary>4×AA holder with its cells and the red and black leads.</summary>
        void BuildBattery(Transform t)
        {
            Box(t, Vector3.zero, new Vector3(0.058f, 0.015f, 0.062f), black, "Holder");
            var cell = Mat(new Color(0.85f, 0.65f, 0.15f), 0.6f, 0.4f);
            for (int i = 0; i < 4; i++)
                Cylinder(t, new Vector3(-0.0217f + i * 0.0145f, 0.004f, 0), 0.0142f, 0.05f, Axis.Z, cell, "AA cell");
            Segment(t, new Vector3(0.012f, 0.004f, -0.031f), new Vector3(0.012f, 0.008f, -0.031f), WireMaterial("red"), 0.0018f);
            Segment(t, new Vector3(-0.012f, 0.004f, -0.031f), new Vector3(-0.012f, 0.008f, -0.031f), WireMaterial("black"), 0.0018f);
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

        void BuildWire(RobotDesign design, int index)
        {
            while (wireGroups.Count <= index) wireGroups.Add(null);
            while (WirePaths.Count <= index) WirePaths.Add(null);
            wireGroups[index] = null;
            WirePaths[index] = null;
            var wire = design.Wires[index];
            var a = DesignGeometry.PinPosition(design, wire.FromPart, wire.FromPin);
            var b = DesignGeometry.PinPosition(design, wire.ToPart, wire.ToPin);
            if (a == null || b == null) return;
            var from = new Vector3(a.Value.x, a.Value.y, a.Value.z) * 0.001f;
            var to = new Vector3(b.Value.x, b.Value.y, b.Value.z) * 0.001f;
            var group = Group(wiresRoot, $"Wire {index}", Vector3.zero);
            // A jumper arches over the parts: a quadratic curve through a raised middle point.
            float lift = 0.012f + 0.15f * Vector3.Distance(from, to);
            var middle = (from + to) / 2 + new Vector3(0, lift, 0);
            var material = WireMaterial(wire.Color);
            const int pieces = 6;
            var path = new Vector3[pieces + 1];
            path[0] = from;
            for (int i = 1; i <= pieces; i++)
            {
                float s = i / (float)pieces;
                path[i] = (1 - s) * (1 - s) * from + 2 * (1 - s) * s * middle + s * s * to;
                Segment(group, path[i - 1], path[i], material, 0.0016f);
            }
            wireGroups[index] = group.gameObject;
            WirePaths[index] = path;
            if (index == highlightedWire) ScaleWire(index, 1.8f);
        }

        void BuildPinMarkers(RobotDesign design)
        {
            pinMaterials[PinKind.Signal] = Mat(new Color(0.95f, 0.80f, 0.15f), 0.5f, 0f);
            pinMaterials[PinKind.Power] = Mat(new Color(0.95f, 0.20f, 0.15f), 0.5f, 0f);
            pinMaterials[PinKind.Ground] = Mat(new Color(0.25f, 0.25f, 0.28f), 0.5f, 0f);
            pinMaterials[PinKind.Motor] = Mat(new Color(0.95f, 0.55f, 0.10f), 0.5f, 0f);
            activePin = Mat(new Color(0.31f, 0.76f, 1.0f), 0.6f, 0f);
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                foreach (var pin in def.Pins)
                {
                    var p = DesignGeometry.PinPosition(design, part.Id, pin.Id);
                    if (p == null) continue;
                    string key = part.Id + "/" + pin.Id;
                    var marker = Sphere(pinsRoot, new Vector3(p.Value.x, p.Value.y, p.Value.z) * 0.001f, 0.0022f, pinMaterials[pin.Kind], "Pin " + key);
                    PinMarkers[key] = marker.transform;
                    pinKinds[key] = pin.Kind;
                }
            }
        }

        Material WireMaterial(string colour)
        {
            if (!wireMaterials.TryGetValue(colour, out var material))
            {
                material = Mat(WireColors.TryGetValue(colour, out var c) ? c : Color.yellow, 0.45f, 0f);
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

        static GameObject Box(Transform parent, Vector3 position, Vector3 size, Material material, string name) =>
            Shape(parent, cube!, position, size, Quaternion.identity, material, name);

        static GameObject Sphere(Transform parent, Vector3 position, float diameter, Material material, string name) =>
            Shape(parent, sphere!, position, Vector3.one * diameter, Quaternion.identity, material, name);

        /// <summary>A cylinder of the given diameter and length along a local axis (Unity's cylinder mesh is 2 units tall).</summary>
        static GameObject Cylinder(Transform parent, Vector3 position, float diameter, float length, Axis axis, Material material, string name)
        {
            var rotation = axis == Axis.X ? Quaternion.Euler(0, 0, 90) : axis == Axis.Z ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            return Shape(parent, cylinder!, position, new Vector3(diameter, length / 2, diameter), rotation, material, name);
        }

        static GameObject Segment(Transform parent, Vector3 a, Vector3 b, Material material, float thickness)
        {
            var direction = b - a;
            if (direction.sqrMagnitude < 1e-10f) direction = Vector3.up * 1e-5f;
            return Shape(parent, cylinder!, (a + b) / 2, new Vector3(thickness, direction.magnitude / 2, thickness),
                         Quaternion.FromToRotation(Vector3.up, direction), material, "Wire");
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
