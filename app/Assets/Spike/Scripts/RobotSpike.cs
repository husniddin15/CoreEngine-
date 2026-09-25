using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CoreEngine.Sim.Avr;
using CoreEngine.Sim.Components;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.Garage;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace CoreEngine.Spike
{
    /// <summary>
    /// The arena robot (Phase 0.4 spike, grown for the Garage): PhysX articulation bodies built from the
    /// robot's <see cref="RobotDesign"/> (plates, wheels on the motors that were placed, caster, sensor, mass
    /// and centre of mass), driven by a real compiled Arduino sketch on the ATmega328P emulator through the
    /// circuit its wires make: power, which Uno pin drives which L298N input, where TRIG and ECHO go, and
    /// how each motor's leads sit on the driver. Wrong wiring behaves wrongly, as on the desk.
    /// Throwaway prototype of the docs/04 §4 architecture (100 Hz physics, emulator slices inside the step).
    /// </summary>
    public sealed class RobotSpike : MonoBehaviour
    {
        // Assigned by the editor setup so the build includes the URP shaders they use.
        public Material floorMaterial = null!;
        public Material wallMaterial = null!;
        public Material obstacleMaterial = null!;
        public Material chassisMaterial = null!;
        public Material wheelMaterial = null!;
        public Material sensorMaterial = null!;
        public Material? acrylicMaterial; // transparent URP Lit for acrylic body shapes (BodyLook)
        public Material? partMaterial;    // URP Lit with normal, metallic and emission maps on: the part models (PartLooks)
        public Material rayMaterial = null!;
        public string firmwareFile = "ObstacleAvoider.hex";

        // The robot from the Garage (ADR-0009): its design, look, firmware, battery and motor windings.
        RobotProject project = null!;
        RobotCircuit circuit = null!;
        RobotVisuals? visuals;
        double batteryAmps;

        const float Mm = 0.001f;
        const float WheelRadius = DesignGeometry.WheelRadius * Mm;
        const float WheelMass = DesignGeometry.WheelMassG * Mm;
        const long CyclesPerFixedStep = Atmega328P.ClockHz / 100;

        // 17 sonar rays: the centre, then rings at 3.5 and 7 degrees (docs/07 §5.1).
        static readonly float[] RingAngles = { 3.5f, 7f };
        const float MaxRangeM = 4f;
        const float AcceptIncidenceDeg = 45f;

        Atmega328P? mcu;
        PinDuty? duty; // how much of each 10 ms step each pin was high: PWM, as the motors average it
        HcSr04? sonar;
        bool boardRunning;
        readonly Dictionary<string, (AvrPort port, int bit)?> pins = new Dictionary<string, (AvrPort, int)?>();
        Func<string, bool> pinHigh = null!;

        // Servos on their signal pins, and the LEDs the board, the driver and LED modules show
        readonly List<(Sg90Servo servo, ServoLink link)> servos = new List<(Sg90Servo, ServoLink)>();
        readonly Dictionary<string, bool> lightsShown = new Dictionary<string, bool>();
        string? unoId, driverId;
        float txUntil;
        readonly L298NModel bridge = new L298NModel { SupplyVolts = 6.0 };
        readonly DcMotorModel motor = DcMotorModel.TtGearMotor148();

        ArticulationBody chassis = null!;
        ArticulationBody? leftWheel;   // the motor whose wheel is on the left, and the one on the right (the winding models follow them)
        ArticulationBody? rightWheel;
        string leftMotorId = "", rightMotorId = "";
        Transform? sonarMount;
        Camera followCamera = null!;
        ArenaCamera arenaCamera = null!;

        readonly List<Vector3> rayDirections = new List<Vector3>();
        LineRenderer[] rayLines = Array.Empty<LineRenderer>();
        Transform? hitMarker;
        double distanceCm = double.NaN;

        // Telemetry
        double leftVolts = double.NaN, rightVolts = double.NaN, leftAmps, rightAmps;
        double emulatorMsAverage;
        readonly Stopwatch emulatorWatch = new Stopwatch();
        int fixedSteps, emulatedSteps;
        float fps;
        int frameCount;
        float frameTimer;
        readonly StringBuilder serialLine = new StringBuilder();
        readonly Queue<string> serialLines = new Queue<string>();

        public double EmulatorMsPerFixedStep => emulatorMsAverage;
        public RobotProject Project => project;
        public RobotCircuit Circuit => circuit;
        public string FirmwareName => firmwareFile;

        // Live values for the UI spike's panels.
        public event Action<string>? SerialLine;
        public double DistanceCm => distanceCm;
        public int SonarMeasurements => sonar?.Measurements ?? 0;
        public double SupplyVolts => bridge.SupplyVolts;
        public double LeftVolts => leftVolts;
        public double RightVolts => rightVolts;
        public double LeftAmps => leftAmps;
        public double RightAmps => rightAmps;
        public double LeftWheelSpeed => leftWheel != null ? leftWheel.jointVelocity[0] : 0;
        public double RightWheelSpeed => rightWheel != null ? rightWheel.jointVelocity[0] : 0;
        public bool BoardRunning => boardRunning;
        /// <summary>Seconds since the robot was put in the arena (100 physics steps per second).</summary>
        public double ArenaSeconds => fixedSteps * 0.01;

        /// <summary>A string-table key for the board's state (docs/10 §5).</summary>
        public string BoardStatusKey =>
            mcu == null ? "board.none"
            : project.BoardBurnt ? "board.burnt"
            : !circuit.BoardPowered ? "board.unpowered"
            : project.Battery.IsEmpty ? "board.batteryEmpty"
            : "board.running";

        /// <summary>One line of state for the benchmark log.</summary>
        public string Telemetry()
        {
            if (chassis == null) return "not started";
            var p = chassis.transform.position;
            var e = chassis.transform.rotation.eulerAngles;
            var inputs = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                string? pin = circuit.DriverInputs[i];
                inputs.Append(pin == null ? '-' : pinHigh(pin) ? '1' : '0');
            }
            string last = serialLines.Count > 0 ? string.Join("|", serialLines) : "-";
            string board = mcu == null ? "none" : boardRunning ? $"t={mcu.Seconds:F2}" : BoardStatusKey;
            return $"{board} pos=({p.x:F2},{p.y:F3},{p.z:F2}) rot=({e.x:F0},{e.y:F0},{e.z:F0}) " +
                   $"wheels=({LeftWheelSpeed:F1},{RightWheelSpeed:F1}) rad/s " +
                   $"volts=({Volts(leftVolts)},{Volts(rightVolts)}) amps=({leftAmps:F2},{rightAmps:F2}) " +
                   $"IN1-4={inputs} sonar={distanceCm:F1}cm n={SonarMeasurements} " +
                   $"battery={project.Battery.StateOfCharge * 100:F2}% motors=({project.LeftMotor.TemperatureC:F1},{project.RightMotor.TemperatureC:F1})C serial={last}";
        }

        public float Fps => fps;

        /// <summary>The whole arena from above (the benchmark's picture), shown at once; false goes back behind the robot.</summary>
        public bool TopView
        {
            get => arenaCamera != null && arenaCamera.View == ArenaView.Arena;
            set => arenaCamera?.Show(value ? ArenaView.Arena : ArenaView.Follow, now: true);
        }

        /// <summary>The camera's viewpoint (the buttons over the arena's view).</summary>
        public ArenaView View
        {
            get => arenaCamera != null ? arenaCamera.View : ArenaView.Follow;
            set => arenaCamera?.Show(value);
        }

        /// <summary>Goes to a viewpoint, at once with <paramref name="now"/> (the benchmark's pictures).</summary>
        public void ShowView(ArenaView view, bool now) => arenaCamera?.Show(view, now);

        /// <summary>True where a panel, not the 3D view, is under the mouse (set by the UI over the arena).</summary>
        public Func<Vector2, bool>? OverUi { get; set; }
        public Atmega328P? Mcu => mcu;
        public Vector3 RobotPosition => chassis != null ? chassis.transform.position : Vector3.zero;

        void Awake()
        {
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.defaultContactOffset = 0.002f;
            Physics.defaultMaxAngularSpeed = 200f;
            Physics.sleepThreshold = 0.001f;
        }

        void Start()
        {
            SpikeReport.Init();
            GarageState.Load(SpikeReport.Active);
            project = GarageState.Current;
            pinHigh = pin => boardRunning && PinHigh(pin); // an unpowered board drives nothing
            BodyLook.Init(chassisMaterial, acrylicMaterial);
            CoreEngine.Spike.Parts.PartLooks.Init(chassisMaterial, partMaterial);
            BuildArena();
            BuildRobot();
            BuildSonar();
            BuildCamera();
            PowerOn();
            StartEmulator();
        }

        // ------------------------------------------------------------------ world

        void BuildArena()
        {
            var floorMat = new PhysicsMaterial("Laminate") { staticFriction = 0.9f, dynamicFriction = 0.8f };
            var laminate = new Material(floorMaterial);
            laminate.SetTexture("_BaseMap", FloorTexture());
            laminate.SetTextureScale("_BaseMap", new Vector2(10, 10)); // 30 cm tiles on the 3 m floor
            laminate.SetFloat("_Smoothness", 0.45f);
            var floor = Box("Floor", new Vector3(0, -0.01f, 0), new Vector3(3f, 0.02f, 3f), laminate);
            floor.GetComponent<Collider>().material = floorMat;

            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float offset = side % 2 == 0 ? 1.5f : -1.5f;
                var pos = alongX ? new Vector3(0, 0.05f, offset) : new Vector3(offset, 0.05f, 0);
                var size = alongX ? new Vector3(3.04f, 0.1f, 0.02f) : new Vector3(0.02f, 0.1f, 3.04f);
                Box("Wall", pos, size, wallMaterial);
            }

            var random = new System.Random(7);
            int placed = 0;
            while (placed < 9)
            {
                var pos = new Vector3((float)(random.NextDouble() * 2.4 - 1.2), 0, (float)(random.NextDouble() * 2.4 - 1.2));
                if (pos.magnitude < 0.5f) continue; // keep the start area clear
                float w = 0.1f + (float)random.NextDouble() * 0.15f;
                float d = 0.1f + (float)random.NextDouble() * 0.15f;
                var box = Box("Obstacle", new Vector3(pos.x, 0.075f, pos.z), new Vector3(w, 0.15f, d), obstacleMaterial);
                box.transform.rotation = Quaternion.Euler(0, (float)random.NextDouble() * 90f, 0);
                // Cardboard boxes: packing tape across the lid and down the two ends.
                var tape = Box("Tape", box.transform.position + new Vector3(0, 0.0755f, 0), new Vector3(w + 0.002f, 0.001f, 0.048f), tapeMaterial ??= Tape());
                tape.transform.rotation = box.transform.rotation;
                Destroy(tape.GetComponent<Collider>());
                foreach (float end in new[] { -1f, 1f })
                {
                    var side = Box("Tape", box.transform.position + box.transform.right * end * (w / 2 + 0.0005f) + new Vector3(0, 0.04f, 0),
                                   new Vector3(0.001f, 0.07f, 0.048f), tapeMaterial);
                    side.transform.rotation = box.transform.rotation;
                    Destroy(side.GetComponent<Collider>());
                }
                placed++;
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Material? tapeMaterial;

        Material Tape()
        {
            var material = new Material(obstacleMaterial);
            material.SetColor("_BaseColor", new Color(0.78f, 0.62f, 0.38f));
            material.SetFloat("_Smoothness", 0.7f);
            return material;
        }

        /// <summary>One 30 cm laminate tile with faint grain and a dark seam, made in code (no texture files).</summary>
        static Texture2D FloorTexture()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "LaminateTile",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float grain = 0.965f + 0.035f * Mathf.PerlinNoise(0.37f, y * 0.21f);
                for (int x = 0; x < size; x++)
                {
                    float v = x < 2 || y < 2 ? 0.74f : grain * (0.985f + 0.015f * Mathf.PerlinNoise(x * 0.05f, y * 0.9f));
                    pixels[y * size + x] = new Color(0.95f * v, 0.92f * v, 0.86f * v);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        GameObject Box(string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // ------------------------------------------------------------------ robot

        /// <summary>The physics robot in the chassis frame of <see cref="DesignGeometry"/> (origin 5 cm above the floor).</summary>
        void BuildRobot()
        {
            var design = project.Design;
            var body = design.Body;
            circuit = CircuitAnalysis.Analyse(design);

            // The frame's origin is on the floor under the robot; it stands where its lowest point is (a wheel, the
            // caster, or anything hanging lower), half a millimetre up so it settles rather than starting inside.
            var root = new GameObject("Robot");
            root.transform.position = new Vector3(0, (0.5f - DesignGeometry.LowestPoint(design)) * Mm, 0);
            var plastic = new PhysicsMaterial("Plastic") { staticFriction = 0.4f, dynamicFriction = 0.35f };

            // Every solid piece of the body collides as its convex hull (holes are left out), and so does an
            // imported mesh that is not closed.
            var bodyMeshes = BodyBuilder.Build(body, project.ImportFolder);
            var shapes = new List<Mesh>(bodyMeshes.Loose);
            foreach (var solid in bodyMeshes.Solids) shapes.Add(solid.Mesh);
            foreach (var mesh in shapes)
            {
                var shape = new GameObject("ShapeCollider");
                shape.transform.SetParent(root.transform, false);
                var hull = shape.AddComponent<MeshCollider>();
                hull.sharedMesh = mesh;
                hull.convex = true;
                hull.material = plastic;
            }

            // Parts collide as their boxes where they were put; the caster as its 20 mm ball, which barely rubs.
            var casterMaterial = new PhysicsMaterial("Caster")
            {
                staticFriction = 0.02f,
                dynamicFriction = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                if (def.Kind == PartKind.Caster)
                {
                    var c = DesignGeometry.CasterBall(part);
                    var ball = root.AddComponent<SphereCollider>();
                    ball.center = new Vector3(c.x, c.y, c.z) * Mm;
                    ball.radius = DesignGeometry.CasterBallRadius * Mm;
                    ball.material = casterMaterial;
                    continue;
                }
                var holder = new GameObject("PartCollider " + part.Id);
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = new Vector3(part.X, part.Y, part.Z) * Mm;
                holder.transform.localRotation = Quaternion.Euler(part.RotX, part.Rotation, part.RotZ);
                var box = holder.AddComponent<BoxCollider>();
                box.center = new Vector3(def.BoxCentre.x, def.BoxCentre.y, def.BoxCentre.z) * Mm;
                box.size = new Vector3(def.SizeX, def.SizeY, def.SizeZ) * Mm;
                box.material = plastic;
            }

            // Mass and centre of mass from the parts (docs/09 masses) and the body's exact volumes from Manifold,
            // each of its own material; the wheels are bodies of their own.
            chassis = root.AddComponent<ArticulationBody>();
            double partsGrams = design.MassKg() * 1000 - DesignGeometry.BodyMassG(body);
            chassis.mass = Mathf.Max(0.02f, (float)((partsGrams + bodyMeshes.MassG) / 1000) - design.Count(PartCatalog.TtMotor) * WheelMass);
            var com = DesignGeometry.CentreOfMass(design, wheels: false);
            chassis.automaticCenterOfMass = false;
            chassis.centerOfMass = new Vector3(com.x, com.y, com.z) * Mm;
            chassis.linearDamping = 0f;
            chassis.angularDamping = 0.05f;

            // A wheel on every motor's shaft, turning about the shaft; with one motor the robot can only turn.
            // The motor with its wheel on the left drives the left winding model, the other the right one.
            var wheels = new Dictionary<string, Transform>();
            foreach (var part in design.Parts)
            {
                if (PartCatalog.Get(part.Part)?.Kind != PartKind.Motor) continue;
                var w = DesignGeometry.WheelCentre(part);
                var wheel = BuildWheel(root.transform, new Vector3(w.x, w.y, w.z) * Mm, Quaternion.Euler(part.RotX, part.Rotation, part.RotZ), part.Id);
                wheels[part.Id] = wheel.transform;
                // Two motors on one side share the two winding models in turn.
                string side = DesignGeometry.SideOf(part);
                if ((side == "left" && leftWheel == null) || (side == "right" && rightWheel != null && leftWheel == null))
                {
                    leftWheel = wheel;
                    leftMotorId = part.Id;
                }
                else if (rightWheel == null)
                {
                    rightWheel = wheel;
                    rightMotorId = part.Id;
                }
            }

            var sensor = design.Parts.Find(p => p.Part == PartCatalog.HcSr04);
            if (sensor != null)
            {
                var face = DesignGeometry.SonarFace(sensor);
                var aim = DesignGeometry.SonarAim(sensor);
                sonarMount = new GameObject("SonarMount").transform;
                sonarMount.SetParent(root.transform, false);
                sonarMount.localPosition = new Vector3(face.x, face.y, face.z) * Mm; // the transducers' front faces
                sonarMount.localRotation = Quaternion.LookRotation(new Vector3(aim.x, aim.y, aim.z), Vector3.up);
            }

            // The same model as on the Garage turntable, with each wheel on its turning wheel body.
            visuals = RobotVisuals.Build(root.transform, wheels, project, chassisMaterial, prebuiltBody: bodyMeshes);
        }

        /// <summary>A wheel body on a motor's shaft: its joint turns about its own x axis, which is the shaft.</summary>
        ArticulationBody BuildWheel(Transform parent, Vector3 position, Quaternion motorTurn, string motorId)
        {
            var wheel = new GameObject("Wheel " + motorId);
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = position;
            wheel.transform.localRotation = motorTurn;

            var collider = wheel.AddComponent<SphereCollider>();
            collider.radius = WheelRadius;
            collider.material = new PhysicsMaterial("Rubber") { staticFriction = 0.9f, dynamicFriction = 0.8f };

            var body = wheel.AddComponent<ArticulationBody>();
            body.jointType = ArticulationJointType.RevoluteJoint;
            body.anchorRotation = Quaternion.identity; // revolute joints turn about the anchor's X axis
            body.mass = WheelMass;
            body.automaticInertiaTensor = false;
            float axle = 0.5f * WheelMass * WheelRadius * WheelRadius + (float)motor.ReflectedInertiaKgM2;
            body.inertiaTensor = new Vector3(axle, 1e-5f, 1e-5f);
            body.inertiaTensorRotation = Quaternion.identity;
            body.jointFriction = 0f;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.maxAngularVelocity = 200f;
            return body;
        }

        /// <summary>
        /// Switching on: wiring faults that destroy parts do it now (docs/06 §7, F7 and F26), and the damage
        /// stays until the part is replaced in Check &amp; repair.
        /// </summary>
        void PowerOn()
        {
            if (project.Battery.IsEmpty) return;
            if (circuit.BoardDamaged && !project.BoardBurnt)
            {
                project.BoardBurnt = true;
                Debug.Log("Spike: the Uno was destroyed by the battery on its 5V pin (F7)");
            }
            if (circuit.SonarDamaged && !project.SonarBurnt)
            {
                project.SonarBurnt = true;
                Debug.Log("Spike: the HC-SR04 was destroyed by the battery on its VCC (F26)");
            }
        }

        // ------------------------------------------------------------------ sonar

        void BuildSonar()
        {
            if (sonarMount == null) return;
            rayDirections.Add(Vector3.forward);
            foreach (float angle in RingAngles)
            {
                for (int k = 0; k < 8; k++)
                {
                    float phi = k * 45f * Mathf.Deg2Rad;
                    var axis = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0);
                    rayDirections.Add(Quaternion.AngleAxis(angle, axis) * Vector3.forward);
                }
            }

            rayLines = new LineRenderer[rayDirections.Count];
            for (int i = 0; i < rayLines.Length; i++)
            {
                var line = new GameObject("SonarRay" + i).AddComponent<LineRenderer>();
                line.sharedMaterial = rayMaterial;
                line.widthMultiplier = 0.0025f;
                line.positionCount = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rayLines[i] = line;
            }

            hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            hitMarker.name = "SonarHit";
            Destroy(hitMarker.GetComponent<Collider>());
            hitMarker.localScale = Vector3.one * 0.02f;
            hitMarker.GetComponent<Renderer>().sharedMaterial = sensorMaterial;
        }

        /// <summary>Nearest accepted hit of the 17-ray cone, in cm; NaN when nothing is in range or there is no sensor.</summary>
        double CastSonar(bool updateVisuals)
        {
            if (sonarMount == null) return double.NaN;
            bool sounding = sonar != null && boardRunning; // rays are drawn while the sensor is being used
            double best = double.NaN;
            Vector3 bestPoint = Vector3.zero;
            var origin = sonarMount.position;
            for (int i = 0; i < rayDirections.Count; i++)
            {
                var dir = sonarMount.TransformDirection(rayDirections[i]);
                bool accepted = false;
                float length = MaxRangeM;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, MaxRangeM))
                {
                    length = hit.distance;
                    float incidence = Vector3.Angle(-dir, hit.normal);
                    accepted = incidence <= AcceptIncidenceDeg;
                    if (accepted && (double.IsNaN(best) || hit.distance * 100 < best))
                    {
                        best = hit.distance * 100;
                        bestPoint = hit.point;
                    }
                }
                if (updateVisuals)
                {
                    // From the robot's own eye the beams start at the camera and only clutter the view: the
                    // hit marker shows where they land.
                    rayLines[i].enabled = sounding && (arenaCamera == null || arenaCamera.View != ArenaView.Eye);
                    rayLines[i].SetPosition(0, origin);
                    rayLines[i].SetPosition(1, origin + dir * length);
                    var color = accepted ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    rayLines[i].startColor = color;
                    rayLines[i].endColor = color;
                }
            }
            if (updateVisuals && hitMarker != null)
            {
                hitMarker.gameObject.SetActive(sounding && !double.IsNaN(best));
                hitMarker.position = bestPoint;
            }
            return best;
        }

        // ------------------------------------------------------------------ emulator and circuit

        void StartEmulator()
        {
            if (!project.Electronics) return; // no board: the robot is only a body on wheels
            mcu = new Atmega328P();
            string path = project.FirmwareFullPath;
            firmwareFile = Path.GetFileName(path);
            mcu.LoadHex(File.ReadAllText(path));
            mcu.Cpu.Diagnostic += message => Debug.LogWarning("Emulator: " + message);
            duty = new PinDuty(mcu);

            // The HC-SR04 on whatever pins its TRIG and ECHO wires reach; an unpowered sensor never answers.
            bool sensorWorks = circuit.SonarPowered && !project.SonarBurnt;
            var trig = circuit.Trig == null ? null : Pin(circuit.Trig);
            var echo = circuit.Echo == null ? null : Pin(circuit.Echo);
            if (sonarMount != null && sensorWorks && trig != null && echo != null)
                sonar = new HcSr04(mcu.Cpu, Atmega328P.ClockHz, trig.Value.port, trig.Value.bit, echo.Value.port, echo.Value.bit, () => distanceCm);

            // Servos on whatever pins their signal leads reach.
            foreach (var link in circuit.Servos)
            {
                var signal = link.Pin == null ? null : Pin(link.Pin);
                if (signal != null) servos.Add((new Sg90Servo(signal.Value.port, signal.Value.bit, Atmega328P.ClockHz), link));
            }
            foreach (var part in project.Design.Parts)
            {
                var kind = PartCatalog.Get(part.Part)?.Kind;
                if (kind == PartKind.Board) unoId ??= part.Id;
                if (kind == PartKind.MotorDriver) driverId ??= part.Id;
            }

            mcu.Usart0.ByteTransmitted += (value, start, end) =>
            {
                txUntil = Time.time + 0.04f; // the Uno's TX LED flickers with every byte
                if (value == '\n')
                {
                    string line = serialLine.ToString().TrimEnd('\r');
                    serialLines.Enqueue(line);
                    while (serialLines.Count > 6) serialLines.Dequeue();
                    serialLine.Clear();
                    SerialLine?.Invoke(line);
                }
                else if (serialLine.Length < 80)
                {
                    serialLine.Append((char)value);
                }
            };
            Debug.Log($"Spike: loaded {firmwareFile} ({mcu.ProgramSize} bytes); wiring: " +
                      (circuit.Warnings.Count == 0 ? "no findings" : string.Join(", ", circuit.Warnings)));
        }

        /// <summary>The port and bit of an Uno pin name, or null for pins that are not port I/O.</summary>
        (AvrPort port, int bit)? Pin(string name)
        {
            if (mcu == null) return null;
            if (!pins.TryGetValue(name, out var found))
            {
                var io = UnoPins.PortOf(name);
                found = io == null ? null : (io.Value.port == 'B' ? mcu.PortB : io.Value.port == 'C' ? mcu.PortC : mcu.PortD, io.Value.bit);
                pins[name] = found;
            }
            return found;
        }

        bool PinHigh(string name)
        {
            var pin = Pin(name);
            return pin != null && pin.Value.port.GetDrive(pin.Value.bit) == PinDrive.High;
        }

        void FixedUpdate()
        {
            if (chassis == null) return;
            fixedSteps++;

            // 1. Inputs from physics: the sonar distance the HC-SR04 will report if triggered in this slice.
            distanceCm = CastSonar(false);

            // 2. Run the MCU for 10 ms (160 000 cycles) while it has power and is not burnt out.
            boardRunning = mcu != null && circuit.BoardPowered && !project.BoardBurnt && !project.Battery.IsEmpty;
            if (boardRunning)
            {
                emulatorWatch.Restart();
                duty!.Begin(mcu!.Cpu.Cycles);
                mcu.RunCycles(CyclesPerFixedStep);
                emulatorWatch.Stop();
                emulatedSteps++;
                double ms = emulatorWatch.Elapsed.TotalMilliseconds;
                emulatorMsAverage = emulatedSteps == 1 ? ms : emulatorMsAverage * 0.98 + ms * 0.02;
            }

            // 3. Outputs: pins -> the L298N inputs the wires reach -> motor lead voltages -> wheel torque.
            //    The 4×AA pack feeds the bridge (docs/06 §4.1); a burnt winding is an open circuit (F18). Each input
            //    counts by the share of the step it was high, so analogWrite on ENA/ENB sets the speed.
            bridge.SupplyVolts = project.Battery.TerminalVolts(batteryAmps);
            long stepEnd = mcu?.Cpu.Cycles ?? 0;
            System.Func<string, double> pinDuty = pin => boardRunning ? duty!.Duty(pin, stepEnd) : 0; // an unpowered board drives nothing
            leftVolts = leftWheel == null || project.LeftMotor.Burnt ? double.NaN : DriveMap.MotorVolts(circuit, leftMotorId, pinDuty, bridge);
            rightVolts = rightWheel == null || project.RightMotor.Burnt ? double.NaN : DriveMap.MotorVolts(circuit, rightMotorId, pinDuty, bridge);
            leftAmps = DriveWheel(leftWheel, leftVolts);
            rightAmps = DriveWheel(rightWheel, rightVolts);

            // 4. Servos turn toward the angle their last pulse asked for.
            foreach (var (servo, link) in servos) servo.Step(CyclesPerFixedStep / Atmega328P.ClockHz, link.Powered && !project.Battery.IsEmpty);

            // 5. Heat and charge: winding temperatures (docs/06 §5.10) and the battery drain.
            double dt = Time.fixedDeltaTime;
            project.LeftMotor.Update(Math.Abs(leftAmps), motor.ResistanceOhm, dt);
            project.RightMotor.Update(Math.Abs(rightAmps), motor.ResistanceOhm, dt);
            batteryAmps = ElectronicsAmps() + Drawn(leftVolts, leftAmps) + Drawn(rightVolts, rightAmps);
            project.Battery.Drain(batteryAmps, dt);
        }

        /// <summary>Uno ≈ 50 mA while running, L298N logic ≈ 10 mA, HC-SR04 ≈ 15 mA.</summary>
        double ElectronicsAmps() =>
            (boardRunning ? 0.05 : 0) + (circuit.DriverPowered ? 0.01 : 0) + (circuit.SonarPowered && !project.SonarBurnt ? 0.015 : 0);

        /// <summary>Current taken from the battery by one driven channel; braking and coasting take none.</summary>
        static double Drawn(double volts, double amps) =>
            double.IsNaN(volts) || volts == 0 || Math.Sign(volts) != Math.Sign(amps) ? 0 : Math.Abs(amps);

        /// <summary>
        /// Torque of one motor on its wheel, with the reaction on the chassis. The motor model works in the
        /// motor's own frame: a positive voltage turns the shaft about its own x, which is the wheel joint's axis,
        /// so a motor turned round (the kit's right one) turns its wheel the other way by itself. Returns the
        /// winding current.
        /// </summary>
        double DriveWheel(ArticulationBody? wheel, double volts)
        {
            if (wheel == null) return 0;
            double torque = motor.OutputTorque(volts, wheel.jointVelocity[0], out double amps);
            var axis = wheel.transform.right * (float)torque;
            wheel.AddTorque(axis);
            chassis.AddTorque(-axis);
            return amps;
        }

        // ------------------------------------------------------------------ camera and HUD

        /// <summary>
        /// The LEDs as the running board drives them: ON while the Uno runs, L on D13, TX while bytes go out, the
        /// L298N's PWR while it has its supply, each LED module on its pin; and each servo's horn at its angle.
        /// </summary>
        void UpdateLightsAndHorns()
        {
            if (visuals == null) return;
            ShowLight(unoId, "ON", boardRunning);
            ShowLight(unoId, "L", boardRunning && PinHigh("D13"));
            ShowLight(unoId, "TX", boardRunning && Time.time < txUntil);
            ShowLight(driverId, "PWR", circuit.DriverPowered && !project.Battery.IsEmpty);
            foreach (var link in circuit.Leds)
                ShowLight(link.PartId, "LED", boardRunning && link.Live && link.Pin != null && PinHigh(link.Pin));
            foreach (var (servo, link) in servos)
                if (visuals.Horns.TryGetValue(link.PartId, out var horn))
                    horn.localRotation = Quaternion.Euler(0, 90f - (float)servo.AngleDegrees, 0);
        }

        void ShowLight(string? partId, string name, bool on)
        {
            if (partId == null) return;
            string key = partId + "/" + name;
            if (lightsShown.TryGetValue(key, out bool shown) && shown == on) return;
            lightsShown[key] = on;
            visuals!.SetLight(partId, name, on);
        }

        /// <summary>The arena's camera and its six viewpoints (<see cref="ArenaCamera"/>), starting behind the robot.</summary>
        void BuildCamera()
        {
            followCamera = new GameObject("Camera").AddComponent<Camera>();
            followCamera.tag = "MainCamera";
            followCamera.nearClipPlane = 0.01f;
            followCamera.fieldOfView = 55f;
            followCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            arenaCamera = followCamera.gameObject.AddComponent<ArenaCamera>();
            arenaCamera.Target = chassis != null ? chassis.transform : null;
            arenaCamera.Eye = sonarMount;
            arenaCamera.OverUi = point => OverUi?.Invoke(point) ?? false;
        }

        void LateUpdate()
        {
            if (chassis == null) return;
            CastSonar(true);
            UpdateLightsAndHorns();

            frameCount++;
            frameTimer += Time.unscaledDeltaTime;
            if (frameTimer >= 0.5f)
            {
                fps = frameCount / frameTimer;
                frameCount = 0;
                frameTimer = 0;
            }
        }

        /// <summary>The IMGUI overlay; hidden while the UI Toolkit panels are shown.</summary>
        public bool ShowHud { get; set; } = true;

        void OnGUI()
        {
            if (chassis == null || !ShowHud) return;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
            var text = new StringBuilder();
            text.AppendLine("<b>CoreEngine Phase 0 spike</b>  (C or 1-6: camera)");
            text.AppendLine($"FPS {fps:F0}   emulator {emulatorMsAverage:F2} ms per 10 ms step ({10.0 / Math.Max(emulatorMsAverage, 1e-6):F1}x real time)");
            text.AppendLine(mcu == null
                ? "No board on this robot"
                : $"Board: {UI.SpikeStrings.Get(BoardStatusKey)}; emulated {mcu.Seconds:F2} s, {mcu.Cpu.InterruptsServiced:N0} interrupts, firmware {firmwareFile}");
            text.AppendLine(sonarMount == null
                ? "No HC-SR04"
                : $"HC-SR04: {(double.IsNaN(distanceCm) ? "no echo" : distanceCm.ToString("F1") + " cm")}, {SonarMeasurements} measurements");
            text.AppendLine($"Left motor {Volts(leftVolts)} {leftAmps:F2} A   Right motor {Volts(rightVolts)} {rightAmps:F2} A");
            text.AppendLine("Serial (115200 baud):");
            foreach (string line in serialLines) text.AppendLine("  " + line);
            GUI.Box(new Rect(10, 10, 470, 210), text.ToString(), style);
        }

        static string Volts(double v) => double.IsNaN(v) ? "open" : $"{v:+0.0;-0.0;0.0} V";
    }
}
