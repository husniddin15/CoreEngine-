using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The robot's look, built from primitive meshes without colliders, in the chassis frame of RobotSpike
    /// (origin at the chassis centre, 5 cm above the floor, +z forward). Used by the Garage turntable,
    /// the Garage thumbnails and the arena robot, so a finish shows the same everywhere.
    /// Prototype art: real proportions from the part datasheets, no textures.
    /// </summary>
    public sealed class RobotVisuals
    {
        static Mesh? cube, cylinder, sphere;
        static Mesh? holedPlate;
        static bool holedPlateTried;

        readonly Material template;
        readonly Material bodyMaterial;
        readonly Material hubMaterial;
        readonly List<Material> owned = new List<Material>();

        public GameObject Root { get; }

        RobotVisuals(Transform chassis, Material template)
        {
            this.template = template;
            Root = new GameObject("RobotVisual");
            Root.transform.SetParent(chassis, false);
            bodyMaterial = Mat(Color.white, 0.5f, 0f);
            hubMaterial = Mat(Color.white, 0.3f, 0f);
        }

        /// <param name="leftWheel">Wheel transforms of the physics robot; null places the wheels on the chassis (Garage).</param>
        public static RobotVisuals Build(Transform chassis, Transform? leftWheel, Transform? rightWheel, RobotProject project, Material template)
        {
            EnsureMeshes();
            var v = new RobotVisuals(chassis, template);
            v.BuildBody(project);
            if (project.Electronics)
            {
                v.BuildElectronics();
                v.BuildWheel(leftWheel, -1);
                v.BuildWheel(rightWheel, 1);
            }
            v.ApplyFinishes(project);
            return v;
        }

        public void ApplyFinishes(RobotProject project)
        {
            Apply(bodyMaterial, Finishes.Get(project.ActiveBodyFinish, FinishTarget.Body));
            Apply(hubMaterial, Finishes.Get(project.ActiveWheelFinish, FinishTarget.Wheels));
        }

        public void Destroy()
        {
            Object.Destroy(Root);
            foreach (var material in owned) Object.Destroy(material);
        }

        static void Apply(Material material, Finish finish)
        {
            material.SetColor("_BaseColor", finish.Color);
            material.SetFloat("_Smoothness", finish.Smoothness);
            material.SetFloat("_Metallic", finish.Metallic);
        }

        // ------------------------------------------------------------------ parts

        void BuildBody(RobotProject project)
        {
            // Bottom deck: 3 mm plate under the battery holder.
            Box(Root.transform, new Vector3(0, -0.0135f, 0), new Vector3(0.12f, 0.003f, 0.16f), bodyMaterial, "BottomPlate");
            if (project.Body == BodyKind.EmptyPlate) return;

            var brass = Mat(new Color(0.78f, 0.62f, 0.25f), 0.7f, 1f);
            foreach (float x in new[] { -0.05f, 0.05f })
                foreach (float z in new[] { -0.065f, 0.065f })
                    Cylinder(Root.transform, new Vector3(x, 0, z), 0.005f, 0.024f, Axis.Y, brass, "Standoff");

            var holed = project.Body == BodyKind.HoledPlate ? HoledPlate() : null;
            if (holed != null)
            {
                // Top deck from the Body Studio kernel: 3 mm plate, 25 mm side walls, 48 holes (Manifold).
                var deck = new GameObject("TopDeck (Manifold)");
                deck.transform.SetParent(Root.transform, false);
                deck.transform.localPosition = new Vector3(0, 0.012f, 0);
                deck.AddComponent<MeshFilter>().sharedMesh = holed;
                deck.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            }
            else
            {
                Box(Root.transform, new Vector3(0, 0.0135f, 0), new Vector3(0.12f, 0.003f, 0.16f), bodyMaterial, "TopPlate");
            }
        }

        void BuildElectronics()
        {
            var t = Root.transform;
            var black = Mat(new Color(0.06f, 0.06f, 0.07f), 0.35f, 0f);
            var metal = Mat(new Color(0.80f, 0.81f, 0.83f), 0.8f, 1f);
            var darkMetal = Mat(new Color(0.25f, 0.26f, 0.28f), 0.6f, 1f);
            var yellow = Mat(new Color(0.98f, 0.76f, 0.10f), 0.35f, 0f);

            // 4×AA holder between the decks.
            Box(t, new Vector3(0, -0.0045f, -0.005f), new Vector3(0.058f, 0.015f, 0.062f), black, "BatteryHolder");

            // TT gear motors under the bottom deck, shafts on the wheel axle.
            foreach (float side in new[] { -1f, 1f })
            {
                Box(t, new Vector3(side * 0.072f, -0.026f, -0.03f), new Vector3(0.019f, 0.022f, 0.037f), yellow, "TTGearbox");
                Cylinder(t, new Vector3(side * 0.072f, -0.024f, 0.0015f), 0.02f, 0.026f, Axis.Z, metal, "TTMotorCan");
            }

            // Caster: holder and steel ball (the physics sphere is 1 cm in radius at the same place).
            Cylinder(t, new Vector3(0, -0.021f, 0.065f), 0.022f, 0.012f, Axis.Y, darkMetal, "CasterHolder");
            Sphere(t, new Vector3(0, -0.04f, 0.065f), 0.02f, metal, "CasterBall");

            BuildUno(t, black, metal);
            BuildL298N(t, black);
            BuildSonar(t, metal, darkMetal);

            // Jumper wires: D5-D8 to IN1-IN4, D9/D10 to TRIG/ECHO.
            Color[] colors = { new Color(0.95f, 0.80f, 0.10f), new Color(0.15f, 0.70f, 0.25f), new Color(0.15f, 0.40f, 0.90f), new Color(0.55f, 0.25f, 0.75f) };
            for (int i = 0; i < 4; i++)
                Wire(t, new Vector3(-0.003f, 0.026f, -0.030f + 0.004f * i), new Vector3(0.016f + 0.004f * i, 0.019f, 0.004f), Mat(colors[i], 0.4f, 0f));
            Wire(t, new Vector3(-0.003f, 0.026f, -0.014f), new Vector3(-0.004f, 0.021f, 0.084f), Mat(new Color(0.95f, 0.45f, 0.10f), 0.4f, 0f));
            Wire(t, new Vector3(-0.003f, 0.026f, -0.010f), new Vector3(0.004f, 0.021f, 0.084f), Mat(new Color(0.92f, 0.92f, 0.92f), 0.4f, 0f));
        }

        void BuildUno(Transform parent, Material black, Material metal)
        {
            // Arduino Uno R3: 68.6 × 53.4 mm board, USB-B and DC jack on the rear edge.
            var uno = Group(parent, "ArduinoUno", new Vector3(-0.028f, 0.0158f, -0.035f));
            Box(uno, Vector3.zero, new Vector3(0.0534f, 0.0016f, 0.0686f), Mat(new Color(0.00f, 0.47f, 0.55f), 0.45f, 0f), "PCB");
            Box(uno, new Vector3(-0.002f, 0.0028f, 0.006f), new Vector3(0.0076f, 0.004f, 0.035f), black, "ATmega328P");
            Box(uno, new Vector3(0.012f, 0.0063f, -0.031f), new Vector3(0.012f, 0.011f, 0.016f), metal, "USB-B");
            Box(uno, new Vector3(-0.017f, 0.0063f, -0.03f), new Vector3(0.009f, 0.011f, 0.014f), black, "DCJack");
            foreach (float x in new[] { -0.024f, 0.025f })
                Box(uno, new Vector3(x, 0.0050f, 0.004f), new Vector3(0.0025f, 0.0085f, 0.045f), black, "Header");
        }

        void BuildL298N(Transform parent, Material black)
        {
            // L298N module: 43 × 43 mm red board, black heatsink, blue screw terminals.
            var driver = Group(parent, "L298N", new Vector3(0.03f, 0.0158f, 0.022f));
            Box(driver, Vector3.zero, new Vector3(0.043f, 0.0016f, 0.043f), Mat(new Color(0.75f, 0.08f, 0.08f), 0.45f, 0f), "PCB");
            Box(driver, new Vector3(0, 0.0133f, 0.004f), new Vector3(0.023f, 0.025f, 0.015f), black, "Heatsink");
            var blue = Mat(new Color(0.10f, 0.35f, 0.80f), 0.4f, 0f);
            Box(driver, new Vector3(-0.0165f, 0.006f, 0.004f), new Vector3(0.008f, 0.010f, 0.012f), blue, "OUT1-2");
            Box(driver, new Vector3(0.0165f, 0.006f, 0.004f), new Vector3(0.008f, 0.010f, 0.012f), blue, "OUT3-4");
            Box(driver, new Vector3(0, 0.006f, -0.0165f), new Vector3(0.018f, 0.010f, 0.008f), blue, "Power");
        }

        void BuildSonar(Transform parent, Material metal, Material darkMetal)
        {
            // HC-SR04 on its bracket; the centre matches the physics sonar mount (0, 0.03, 0.085).
            var sonar = Group(parent, "HC-SR04", new Vector3(0, 0.03f, 0.085f));
            Box(sonar, Vector3.zero, new Vector3(0.045f, 0.02f, 0.0016f), Mat(new Color(0.10f, 0.40f, 0.80f), 0.45f, 0f), "PCB");
            var mesh = Mat(new Color(0.12f, 0.12f, 0.13f), 0.2f, 0f);
            foreach (float x in new[] { -0.013f, 0.013f })
            {
                Cylinder(sonar, new Vector3(x, 0, 0.0068f), 0.016f, 0.012f, Axis.Z, metal, "Transducer");
                Cylinder(sonar, new Vector3(x, 0, 0.0070f), 0.0125f, 0.0122f, Axis.Z, mesh, "TransducerMesh");
            }
            Box(sonar, new Vector3(0, 0.006f, -0.002f), new Vector3(0.010f, 0.003f, 0.004f), metal, "Crystal");
            Box(sonar, new Vector3(0, -0.0125f, -0.004f), new Vector3(0.03f, 0.007f, 0.010f), darkMetal, "Bracket");
        }

        void BuildWheel(Transform? wheel, int side)
        {
            Transform parent = wheel != null ? wheel : Group(Root.transform, side < 0 ? "LeftWheel" : "RightWheel", new Vector3(side * 0.095f, -0.0175f, -0.03f));
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

        /// <summary>A jumper wire: two straight pieces over a raised midpoint.</summary>
        static void Wire(Transform parent, Vector3 from, Vector3 to, Material material)
        {
            var mid = (from + to) / 2 + new Vector3(0, 0.012f, 0);
            Segment(parent, from, mid, material);
            Segment(parent, mid, to, material);
        }

        static void Segment(Transform parent, Vector3 a, Vector3 b, Material material)
        {
            var direction = b - a;
            var piece = Shape(parent, cylinder!, (a + b) / 2, new Vector3(0.0016f, direction.magnitude / 2, 0.0016f),
                              Quaternion.FromToRotation(Vector3.up, direction), material, "Wire");
            piece.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
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
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }

        static Mesh? HoledPlate()
        {
            if (!holedPlateTried)
            {
                holedPlateTried = true;
                holedPlate = CsgSpike.ChassisMesh();
                if (holedPlate != null) holedPlate.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            return holedPlate;
        }
    }
}
