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
        HcSr04? sonar;
        bool boardRunning;
        readonly Dictionary<string, (AvrPort port, int bit)?> pins = new Dictionary<string, (AvrPort, int)?>();
        Func<string, bool> pinHigh = null!;
        readonly L298NModel bridge = new L298NModel { SupplyVolts = 6.0 };
        readonly DcMotorModel motor = DcMotorModel.TtGearMotor148();

        ArticulationBody chassis = null!;
        ArticulationBody? leftWheel;
        ArticulationBody? rightWheel;
        Transform? sonarMount;
        Camera followCamera = null!;
        bool topView;

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
        public bool TopView { get => topView; set => topView = value; }
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

            var root = new GameObject("Robot");
            root.transform.position = new Vector3(0, 0.05f, 0);

            // The plates and walls as one collider: a box, or a convex disc for a round body.
            float bottom = DesignGeometry.BottomPlateBottom(body);
            float top = DesignGeometry.DeckTop(body) + (body.Shape == BodyShape.Round ? 0 : body.WallHeightMm);
            var plates = new GameObject("ChassisCollider");
            plates.transform.SetParent(root.transform, false);
            plates.transform.localPosition = new Vector3(0, (top + bottom) / 2 * Mm, 0);
            var plastic = new PhysicsMaterial("Plastic") { staticFriction = 0.4f, dynamicFriction = 0.35f };
            if (body.Shape == BodyShape.Round)
            {
                plates.transform.localScale = new Vector3(body.WidthMm * Mm, (top - bottom) * Mm / 2, body.WidthMm * Mm);
                var disc = plates.AddComponent<MeshCollider>();
                disc.sharedMesh = RobotVisuals.CylinderMesh; // 1 unit across, 2 units tall
                disc.convex = true;
                disc.material = plastic;
            }
            else
            {
                var box = plates.AddComponent<BoxCollider>();
                box.size = new Vector3(body.WidthMm, top - bottom, body.EffectiveLength) * Mm;
                box.material = plastic;
            }

            // The Body Studio's solid shapes collide too: a convex hull around each (holes are left out).
            var bodyMeshes = BodyBuilder.Build(body, project.ImportFolder);
            var shapes = new List<Mesh>(bodyMeshes.Loose);
            foreach (var feature in bodyMeshes.Features) if (!feature.hole) shapes.Add(feature.mesh);
            foreach (var mesh in shapes)
            {
                var shape = new GameObject("ShapeCollider");
                shape.transform.SetParent(root.transform, false);
                var hull = shape.AddComponent<MeshCollider>();
                hull.sharedMesh = mesh;
                hull.convex = true;
                hull.material = plastic;
            }

            if (design.Count(PartCatalog.Caster) > 0)
            {
                var c = DesignGeometry.CasterCentre(body);
                var caster = root.AddComponent<SphereCollider>();
                caster.center = new Vector3(c.x, c.y, c.z) * Mm;
                caster.radius = 0.01f; // the 20 mm ball touches the floor
                caster.material = new PhysicsMaterial("Caster")
                {
                    staticFriction = 0.02f,
                    dynamicFriction = 0.02f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            // Mass and centre of mass from the parts (docs/09 masses) and the body's exact volume from Manifold;
            // the wheels are bodies of their own.
            chassis = root.AddComponent<ArticulationBody>();
            double bodyGrams = bodyMeshes.VolumeMm3 / 1000.0 * BodyDesign.DensityGPerCm3(body.Material);
            double partsGrams = design.MassKg() * 1000 - DesignGeometry.BodyMassG(body);
            chassis.mass = Mathf.Max(0.02f, (float)((partsGrams + bodyGrams) / 1000) - design.Count(PartCatalog.TtMotor) * WheelMass);
            var com = DesignGeometry.CentreOfMass(design, wheels: false);
            chassis.automaticCenterOfMass = false;
            chassis.centerOfMass = new Vector3(com.x, com.y, com.z) * Mm;
            chassis.linearDamping = 0f;
            chassis.angularDamping = 0.05f;

            // A wheel on every motor that was placed; with one motor the robot can only turn.
            foreach (var part in design.Parts)
            {
                if (PartCatalog.Get(part.Part)?.Kind != PartKind.Motor) continue;
                var w = DesignGeometry.WheelCentre(body, part.Slot);
                var wheel = BuildWheel(root.transform, new Vector3(w.x, w.y, w.z) * Mm, part.Slot);
                if (part.Slot == "right") rightWheel = wheel;
                else leftWheel = wheel;
            }

            var sensor = design.Parts.Find(p => p.Part == PartCatalog.HcSr04);
            if (sensor != null)
            {
                var s = DesignGeometry.Place(design, sensor);
                sonarMount = new GameObject("SonarMount").transform;
                sonarMount.SetParent(root.transform, false);
                sonarMount.localPosition = new Vector3(s.x, s.y, s.z + 13) * Mm; // the transducers' front faces
            }

            // The same model as on the Garage turntable, with the wheel parts on the turning wheel bodies.
            visuals = RobotVisuals.Build(root.transform, leftWheel?.transform, rightWheel?.transform, project, chassisMaterial, prebuiltBody: bodyMeshes);
        }

        ArticulationBody BuildWheel(Transform parent, Vector3 position, string slot)
        {
            var wheel = new GameObject(slot == "right" ? "RightWheel" : "LeftWheel");
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = position;

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
                    rayLines[i].enabled = sounding;
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

            // The HC-SR04 on whatever pins its TRIG and ECHO wires reach; an unpowered sensor never answers.
            bool sensorWorks = circuit.SonarPowered && !project.SonarBurnt;
            var trig = circuit.Trig == null ? null : Pin(circuit.Trig);
            var echo = circuit.Echo == null ? null : Pin(circuit.Echo);
            if (sonarMount != null && sensorWorks && trig != null && echo != null)
                sonar = new HcSr04(mcu.Cpu, Atmega328P.ClockHz, trig.Value.port, trig.Value.bit, echo.Value.port, echo.Value.bit, () => distanceCm);

            mcu.Usart0.ByteTransmitted += (value, start, end) =>
            {
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
                mcu!.RunCycles(CyclesPerFixedStep);
                emulatorWatch.Stop();
                emulatedSteps++;
                double ms = emulatorWatch.Elapsed.TotalMilliseconds;
                emulatorMsAverage = emulatedSteps == 1 ? ms : emulatorMsAverage * 0.98 + ms * 0.02;
            }

            // 3. Outputs: pins -> the L298N inputs the wires reach -> motor lead voltages -> wheel torque.
            //    The 4×AA pack feeds the bridge (docs/06 §4.1); a burnt winding is an open circuit (F18).
            bridge.SupplyVolts = project.Battery.TerminalVolts(batteryAmps);
            leftVolts = leftWheel == null || project.LeftMotor.Burnt ? double.NaN : DriveMap.MotorVolts(circuit, "left", pinHigh, bridge);
            rightVolts = rightWheel == null || project.RightMotor.Burnt ? double.NaN : DriveMap.MotorVolts(circuit, "right", pinHigh, bridge);
            leftAmps = DriveWheel(leftWheel, "left", leftVolts);
            rightAmps = DriveWheel(rightWheel, "right", rightVolts);

            // 4. Heat and charge: winding temperatures (docs/06 §5.10) and the battery drain.
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
        /// motor's own frame; the mirrored right motor turns its wheel the other way (<see cref="DriveMap.MountSign"/>).
        /// Returns the winding current.
        /// </summary>
        double DriveWheel(ArticulationBody? wheel, string slot, double volts)
        {
            if (wheel == null) return 0;
            int sign = DriveMap.MountSign(slot);
            double torque = motor.OutputTorque(volts, wheel.jointVelocity[0] * sign, out double amps) * sign;
            var axis = chassis.transform.right * (float)torque;
            wheel.AddTorque(axis);
            chassis.AddTorque(-axis);
            return amps;
        }

        // ------------------------------------------------------------------ camera and HUD

        void BuildCamera()
        {
            followCamera = new GameObject("Camera").AddComponent<Camera>();
            followCamera.tag = "MainCamera";
            followCamera.nearClipPlane = 0.01f;
            followCamera.fieldOfView = 55f;
            followCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.C) && !UI.CodeEditor.HasTypingFocus) topView = !topView;
            if (chassis == null) return;
            CastSonar(true);

            var target = chassis.transform;
            if (topView)
            {
                followCamera.transform.SetPositionAndRotation(new Vector3(0, 3.2f, -0.6f), Quaternion.Euler(80f, 0, 0));
            }
            else
            {
                var flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
                var desired = target.position - flatForward * 0.45f + Vector3.up * 0.28f;
                followCamera.transform.position = Vector3.Lerp(followCamera.transform.position, desired, 0.1f);
                followCamera.transform.LookAt(target.position + flatForward * 0.15f);
            }

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
            text.AppendLine("<b>CoreEngine Phase 0 spike</b>  (C: camera)");
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
