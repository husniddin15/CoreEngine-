using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CoreEngine.Sim.Avr;
using CoreEngine.Sim.Components;
using CoreEngine.Spike.Garage;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CoreEngine.Spike
{
    /// <summary>
    /// Phase 0.4 spike (docs/11-roadmap.md §3): a two-wheel robot built from PhysX articulation
    /// bodies, driven by a real compiled Arduino sketch running on the ATmega328P emulator, with an
    /// HC-SR04 ultrasonic cone made of raycasts. Throwaway prototype: it proves the architecture of
    /// docs/04 §4 (100 Hz physics, emulator slices inside the fixed step); it is not the Phase 1 design.
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

        // The robot from the Garage (ADR-0009): its look, firmware, battery and motor windings.
        RobotProject project = null!;
        RobotVisuals? visuals;
        double batteryAmps;
        const double ElectronicsAmps = 0.075; // Uno ≈ 50 mA, L298N logic ≈ 10 mA, HC-SR04 ≈ 15 mA

        const float WheelRadius = 0.0325f;           // 65 mm TT-motor wheel
        const float TrackHalfWidth = 0.095f;
        const float ChassisMass = 0.9f;              // chassis, Uno, L298N, 4xAA, sensor
        const float WheelMass = 0.03f;
        const long CyclesPerFixedStep = Atmega328P.ClockHz / 100;

        // 17 sonar rays: the centre, then rings at 3.5 and 7 degrees (docs/07 §5.1).
        static readonly float[] RingAngles = { 3.5f, 7f };
        const float MaxRangeM = 4f;
        const float AcceptIncidenceDeg = 45f;

        Atmega328P mcu = null!;
        HcSr04 sonar = null!;
        readonly L298NModel bridge = new L298NModel { SupplyVolts = 6.0 };
        readonly DcMotorModel motor = DcMotorModel.TtGearMotor148();

        ArticulationBody chassis = null!;
        ArticulationBody leftWheel = null!;
        ArticulationBody rightWheel = null!;
        Transform sonarMount = null!;
        Camera followCamera = null!;
        bool topView;

        readonly List<Vector3> rayDirections = new List<Vector3>();
        LineRenderer[] rayLines = null!;
        Transform hitMarker = null!;
        double distanceCm = double.NaN;

        // Telemetry
        double leftVolts, rightVolts, leftAmps, rightAmps;
        double emulatorMsAverage;
        readonly Stopwatch emulatorWatch = new Stopwatch();
        int fixedSteps;
        float fps;
        int frameCount;
        float frameTimer;
        readonly StringBuilder serialLine = new StringBuilder();
        readonly Queue<string> serialLines = new Queue<string>();

        public double EmulatorMsPerFixedStep => emulatorMsAverage;
        public RobotProject Project => project;
        public string FirmwareName => firmwareFile;

        // Live values for the UI spike's panels.
        public event Action<string>? SerialLine;
        public double DistanceCm => distanceCm;
        public int SonarMeasurements => sonar != null ? sonar.Measurements : 0;
        public double SupplyVolts => bridge.SupplyVolts;
        public double LeftVolts => leftVolts;
        public double RightVolts => rightVolts;
        public double LeftAmps => leftAmps;
        public double RightAmps => rightAmps;
        public double LeftWheelSpeed => leftWheel != null ? leftWheel.jointVelocity[0] : 0;
        public double RightWheelSpeed => rightWheel != null ? rightWheel.jointVelocity[0] : 0;

        /// <summary>One line of state for the benchmark log.</summary>
        public string Telemetry()
        {
            if (chassis == null || mcu == null) return "not started";
            var p = chassis.transform.position;
            var e = chassis.transform.rotation.eulerAngles;
            string pins = $"{Bit(mcu.PortD, 5)}{Bit(mcu.PortD, 6)}{Bit(mcu.PortD, 7)}{Bit(mcu.PortB, 0)}";
            string last = serialLines.Count > 0 ? string.Join("|", serialLines) : "-";
            return $"t={mcu.Seconds:F2} pos=({p.x:F2},{p.y:F3},{p.z:F2}) rot=({e.x:F0},{e.y:F0},{e.z:F0}) " +
                   $"wheels=({leftWheel.jointVelocity[0]:F1},{rightWheel.jointVelocity[0]:F1}) rad/s " +
                   $"volts=({Volts(leftVolts)},{Volts(rightVolts)}) amps=({leftAmps:F2},{rightAmps:F2}) " +
                   $"pins IN1-4={pins} sonar={distanceCm:F1}cm n={sonar.Measurements} " +
                   $"battery={project.Battery.StateOfCharge * 100:F2}% motors=({project.LeftMotor.TemperatureC:F1},{project.RightMotor.TemperatureC:F1})C serial={last}";
        }

        static char Bit(AvrPort port, int bit) => port.GetDrive(bit) == PinDrive.High ? '1' : '0';
        public float Fps => fps;
        public bool TopView { get => topView; set => topView = value; }
        public Atmega328P Mcu => mcu;
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
            if (!project.Electronics) project = GarageState.Robots.Find(r => r.Electronics) ?? new RobotProject();
            BuildArena();
            BuildRobot();
            BuildSonar();
            BuildCamera();
            StartEmulator();
        }

        // ------------------------------------------------------------------ world

        void BuildArena()
        {
            var floorMat = new PhysicsMaterial("Laminate") { staticFriction = 0.9f, dynamicFriction = 0.8f };
            var floor = Box("Floor", new Vector3(0, -0.01f, 0), new Vector3(3f, 0.02f, 3f), floorMaterial);
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
                placed++;
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
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

        void BuildRobot()
        {
            var root = new GameObject("Robot");
            root.transform.position = new Vector3(0, 0.05f, 0);

            var chassisCollider = new GameObject("ChassisCollider").AddComponent<BoxCollider>();
            chassisCollider.transform.SetParent(root.transform, false);
            chassisCollider.size = new Vector3(0.12f, 0.03f, 0.16f);
            chassisCollider.material = new PhysicsMaterial("Plastic") { staticFriction = 0.4f, dynamicFriction = 0.35f };

            var caster = root.AddComponent<SphereCollider>();
            caster.center = new Vector3(0, -0.04f, 0.065f);
            caster.radius = 0.01f;
            caster.material = new PhysicsMaterial("Caster")
            {
                staticFriction = 0.02f,
                dynamicFriction = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };

            chassis = root.AddComponent<ArticulationBody>();
            chassis.mass = ChassisMass;
            chassis.linearDamping = 0f;
            chassis.angularDamping = 0.05f;

            leftWheel = BuildWheel(root.transform, -TrackHalfWidth);
            rightWheel = BuildWheel(root.transform, TrackHalfWidth);

            sonarMount = new GameObject("SonarMount").transform;
            sonarMount.SetParent(root.transform, false);
            sonarMount.localPosition = new Vector3(0, 0.03f, 0.085f);

            // The same model as on the Garage turntable, with the wheel parts on the turning wheel bodies.
            visuals = RobotVisuals.Build(root.transform, leftWheel.transform, rightWheel.transform, project, chassisMaterial);
        }

        ArticulationBody BuildWheel(Transform parent, float x)
        {
            var wheel = new GameObject(x < 0 ? "LeftWheel" : "RightWheel");
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = new Vector3(x, WheelRadius - 0.05f, -0.03f);

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

        // ------------------------------------------------------------------ sonar

        void BuildSonar()
        {
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

        /// <summary>Nearest accepted hit of the 17-ray cone, in cm; NaN when nothing is in range.</summary>
        double CastSonar(bool updateVisuals)
        {
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
                    rayLines[i].SetPosition(0, origin);
                    rayLines[i].SetPosition(1, origin + dir * length);
                    var color = accepted ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    rayLines[i].startColor = color;
                    rayLines[i].endColor = color;
                }
            }
            if (updateVisuals)
            {
                hitMarker.gameObject.SetActive(!double.IsNaN(best));
                hitMarker.position = bestPoint;
            }
            return best;
        }

        // ------------------------------------------------------------------ emulator

        void StartEmulator()
        {
            mcu = new Atmega328P();
            string path = project.FirmwareFullPath;
            firmwareFile = Path.GetFileName(path);
            mcu.LoadHex(File.ReadAllText(path));
            mcu.Cpu.Diagnostic += message => Debug.LogWarning("Emulator: " + message);

            // TRIG = D9 (PB1), ECHO = D10 (PB2), as wired in ObstacleAvoider.ino
            sonar = new HcSr04(mcu.Cpu, Atmega328P.ClockHz, mcu.PortB, 1, mcu.PortB, 2, () => distanceCm);

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
            Debug.Log($"Spike: loaded {firmwareFile} ({mcu.ProgramSize} bytes)");
        }

        void FixedUpdate()
        {
            if (mcu == null) return;

            // 1. Inputs from physics: the sonar distance the HC-SR04 will report if triggered in this slice.
            distanceCm = CastSonar(false);
            double leftSpeed = leftWheel.jointVelocity[0];
            double rightSpeed = rightWheel.jointVelocity[0];

            // 2. Run the MCU for 10 ms (160 000 cycles) of emulated time.
            emulatorWatch.Restart();
            mcu.RunCycles(CyclesPerFixedStep);
            emulatorWatch.Stop();
            fixedSteps++;
            double ms = emulatorWatch.Elapsed.TotalMilliseconds;
            emulatorMsAverage = fixedSteps == 1 ? ms : emulatorMsAverage * 0.98 + ms * 0.02;

            // 3. Outputs: L298N inputs D5-D8 -> motor voltages -> wheel torque, with the reaction on the chassis.
            bool in1 = mcu.PortD.GetDrive(5) == PinDrive.High;
            bool in2 = mcu.PortD.GetDrive(6) == PinDrive.High;
            bool in3 = mcu.PortD.GetDrive(7) == PinDrive.High;
            bool in4 = mcu.PortB.GetDrive(0) == PinDrive.High;
            // The 4×AA pack feeds the bridge (docs/06 §4.1); a burnt winding is an open circuit (F18).
            bridge.SupplyVolts = project.Battery.TerminalVolts(batteryAmps);
            leftVolts = project.LeftMotor.Burnt ? double.NaN : bridge.ChannelVolts(true, in1, in2);
            rightVolts = project.RightMotor.Burnt ? double.NaN : bridge.ChannelVolts(true, in3, in4);
            ApplyMotor(leftWheel, motor.OutputTorque(leftVolts, leftSpeed, out leftAmps));
            ApplyMotor(rightWheel, motor.OutputTorque(rightVolts, rightSpeed, out rightAmps));

            // 4. Heat and charge: winding temperatures (docs/06 §5.10) and the battery drain.
            double dt = Time.fixedDeltaTime;
            project.LeftMotor.Update(Math.Abs(leftAmps), motor.ResistanceOhm, dt);
            project.RightMotor.Update(Math.Abs(rightAmps), motor.ResistanceOhm, dt);
            batteryAmps = ElectronicsAmps + Drawn(leftVolts, leftAmps) + Drawn(rightVolts, rightAmps);
            project.Battery.Drain(batteryAmps, dt);
        }

        /// <summary>Current taken from the battery by one driven channel; braking and coasting take none.</summary>
        static double Drawn(double volts, double amps) =>
            double.IsNaN(volts) || volts == 0 || Math.Sign(volts) != Math.Sign(amps) ? 0 : Math.Abs(amps);

        void ApplyMotor(ArticulationBody wheel, double torque)
        {
            var axis = chassis.transform.right * (float)torque;
            wheel.AddTorque(axis);
            chassis.AddTorque(-axis);
        }

        // ------------------------------------------------------------------ camera and HUD

        void BuildCamera()
        {
            followCamera = new GameObject("Camera").AddComponent<Camera>();
            followCamera.tag = "MainCamera";
            followCamera.nearClipPlane = 0.01f;
            followCamera.fieldOfView = 55f;
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
            if (mcu == null || !ShowHud) return;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
            var text = new StringBuilder();
            text.AppendLine("<b>CoreEngine Phase 0 spike</b>  (C: camera)");
            text.AppendLine($"FPS {fps:F0}   emulator {emulatorMsAverage:F2} ms per 10 ms step ({10.0 / Math.Max(emulatorMsAverage, 1e-6):F1}x real time)");
            text.AppendLine($"Emulated {mcu.Seconds:F2} s, {mcu.Cpu.InterruptsServiced:N0} interrupts, firmware {firmwareFile}");
            text.AppendLine($"HC-SR04: {(double.IsNaN(distanceCm) ? "no echo" : distanceCm.ToString("F1") + " cm")}, {sonar.Measurements} measurements");
            text.AppendLine($"Left motor {Volts(leftVolts)} {leftAmps:F2} A   Right motor {Volts(rightVolts)} {rightAmps:F2} A");
            text.AppendLine("Serial (115200 baud):");
            foreach (string line in serialLines) text.AppendLine("  " + line);
            GUI.Box(new Rect(10, 10, 470, 210), text.ToString(), style);
        }

        static string Volts(double v) => double.IsNaN(v) ? "open" : $"{v:+0.0;-0.0;0.0} V";
    }
}
