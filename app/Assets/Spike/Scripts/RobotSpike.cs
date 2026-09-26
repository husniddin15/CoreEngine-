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
using UnityEngine.Rendering;
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

        const float Mm = RobotPhysics.Mm;
        const long CyclesPerFixedStep = Atmega328P.ClockHz / 100;

        // 17 sonar rays: the centre, then rings at 3.5 and 7 degrees (docs/07 §5.1).
        static readonly float[] RingAngles = { 3.5f, 7f };
        const float MaxRangeM = 4f;
        const float AcceptIncidenceDeg = 45f;

        Atmega328P? mcu;
        PinDuty? duty; // how much of each 10 ms step each pin was high: PWM, as the motors average it
        HcSr04? sonar;
        string? sonarTrig, sonarEcho; // the Uno pins its TRIG and ECHO wires reached when it was switched on

        // Wires to pieces that fell off hang between the bodies and pull out when stretched (WireTethers).
        WireTethers? tethers;
        int reportedPulls;
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

        /// <summary>A wire came off its pin: the two bodies it joined moved farther apart than it is long.</summary>
        public event Action<WireInstance>? WirePulledOut;

        /// <summary>Wires hanging between the robot and pieces that fell off, or null when everything holds together.</summary>
        public WireTethers? Tethers => tethers;
        /// <summary>What the HC-SR04 sees, or NaN when nothing is in range or it has lost its supply or a signal wire.</summary>
        public double DistanceCm => sonar == null || sonar.Live ? distanceCm : double.NaN;
        public int SonarMeasurements => sonar?.Measurements ?? 0;
        /// <summary>The L298N's supply: the battery's voltage while it is wired to the driver, else nothing.</summary>
        public double SupplyVolts => circuit != null && circuit.DriverPowered ? bridge.SupplyVolts : 0;
        public double LeftVolts => leftVolts;
        public double RightVolts => rightVolts;
        public double LeftAmps => leftAmps;
        public double RightAmps => rightAmps;
        public double LeftWheelSpeed => leftWheel != null ? leftWheel.jointVelocity[0] : 0;
        public double RightWheelSpeed => rightWheel != null ? rightWheel.jointVelocity[0] : 0;
        public bool BoardRunning => boardRunning;

        /// <summary>The motors whose wheels the left and right winding models drive ("" when there is none).</summary>
        public string LeftMotorId => leftMotorId;
        public string RightMotorId => rightMotorId;
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

        /// <summary>How the robot rests on the floor: on its wheels and caster, or tipped onto something that drags.</summary>
        public RobotStance Stance { get; private set; } = RobotStance.Steady;

        /// <summary>Which way the robot faces, in degrees about the vertical (the benchmark's turning test).</summary>
        public float RobotHeading => chassis != null ? chassis.transform.eulerAngles.y : 0;

        /// <summary>How far the robot leans from level, in degrees.</summary>
        public float RobotLean => chassis != null ? Vector3.Angle(chassis.transform.up, Vector3.up) : 0;

        void Awake() => RobotPhysics.ConfigureWorld();

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
            ArenaBuilder.ObstacleField(null, floorMaterial, wallMaterial, obstacleMaterial);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            ClearAir();
        }

        /// <summary>
        /// A clear day, sharp to the horizon (the owner, 2026-09-25: the arena looked foggy, "we need clear game like
        /// counter strike 2"): the default sky's haze thinned to a clean blue, a wide pale floor round the arena out to
        /// the horizon instead of the empty grey band, and no fog or darkened corners.
        /// </summary>
        void ClearAir()
        {
            RenderSettings.fog = false;
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "ClearSky" };
                sky.SetFloat("_SunSize", 0.03f);
                sky.SetFloat("_AtmosphereThickness", 0.55f);
                sky.SetColor("_SkyTint", new Color(0.42f, 0.6f, 0.9f));
                sky.SetColor("_GroundColor", new Color(0.8f, 0.8f, 0.78f));
                sky.SetFloat("_Exposure", 1.15f);
                RenderSettings.skybox = sky;
                DynamicGI.UpdateEnvironment();
            }
            // Shadows a clean neutral grey: lit by the blue sky itself they came out a deep blue.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.68f, 0.72f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.64f, 0.65f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.44f, 0.43f, 0.42f);
            var ground = new Material(floorMaterial) { name = "Ground" };
            ground.SetColor("_BaseColor", new Color(0.74f, 0.75f, 0.76f));
            ground.SetFloat("_Smoothness", 0.2f);
            ArenaBuilder.Box(null, "Ground", new Vector3(0, -0.03f, 0), new Vector3(80f, 0.02f, 80f), ground);
            var volume = FindAnyObjectByType<Volume>();
            if (volume != null && volume.profile.TryGet(out Vignette vignette)) vignette.active = false;
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
            Stance = RobotStance.Of(design);
            var bodyMeshes = BodyBuilder.Build(body, project.ImportFolder);
            chassis = RobotPhysics.Build(root, design, bodyMeshes, motor.ReflectedInertiaKgM2, out var wheelBodies, out var loose);

            // The motor with its wheel on the left drives the left winding model, the other the right one; with one
            // motor the robot can only turn.
            var wheels = new Dictionary<string, Transform>();
            foreach (var (part, wheel) in wheelBodies)
            {
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
                // On whatever holds it: the robot, or a loose piece it fell off with, still wired and measuring.
                var holder = loose.Find(l => l.Pieces.Contains(sensor.Id));
                sonarMount.SetParent(holder != null ? holder.Body.transform : root.transform, false);
                sonarMount.localPosition = new Vector3(face.x, face.y, face.z) * Mm; // the transducers' front faces
                sonarMount.localRotation = Quaternion.LookRotation(new Vector3(aim.x, aim.y, aim.z), Vector3.up);
            }

            // The same model as on the Garage turntable, with each wheel on its turning wheel body.
            visuals = RobotVisuals.Build(root.transform, wheels, project, chassisMaterial, prebuiltBody: bodyMeshes);
            foreach (var piece in loose) visuals.MovePieces(piece.Pieces, piece.Body.transform);
            Loose = loose;

            // A wire to a piece that fell off holds nothing, but it carries current while it reaches.
            if (loose.Count > 0)
                tethers = WireTethers.Build(design, visuals, partId => loose.Find(l => l.Pieces.Contains(partId))?.Body.transform ?? root.transform);
        }

        /// <summary>The groups of pieces that fall off because nothing attaches them to the robot.</summary>
        public IReadOnlyList<RobotPhysics.LooseBody> Loose { get; private set; } = Array.Empty<RobotPhysics.LooseBody>();

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
            bool sounding = sonar != null && sonar.Live && boardRunning; // rays are drawn while the sensor is being used
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
            {
                sonar = new HcSr04(mcu.Cpu, Atmega328P.ClockHz, trig.Value.port, trig.Value.bit, echo.Value.port, echo.Value.bit, () => distanceCm);
                sonarTrig = circuit.Trig;
                sonarEcho = circuit.Echo;
            }

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

        /// <summary>
        /// Works the circuit out again without the wires that pulled out (<see cref="WireTethers"/>): a motor, the
        /// board or the driver may lose its supply, and the sensor and the servos stop when theirs goes.
        /// </summary>
        void Rewire()
        {
            var wired = project.Design.Clone();
            var gone = new List<int>(tethers!.PulledOut);
            gone.Sort();
            for (int k = gone.Count - 1; k >= 0; k--) wired.Wires.RemoveAt(gone[k]);
            circuit = CircuitAnalysis.Analyse(wired);

            if (sonar != null)
                sonar.Live = circuit.SonarPowered && circuit.Trig == sonarTrig && circuit.Echo == sonarEcho;
            for (int i = 0; i < servos.Count; i++)
            {
                var (servo, link) = servos[i];
                var now = circuit.Servos.Find(s => s.PartId == link.PartId);
                servos[i] = (servo, new ServoLink(link.PartId, link.Pin, now != null && now.Powered && now.Pin == link.Pin));
            }

            for (; reportedPulls < tethers.PulledOut.Count; reportedPulls++)
            {
                var wire = project.Design.Wires[tethers.PulledOut[reportedPulls]];
                Debug.Log($"Spike: at {ArenaSeconds:F2} s the wire {wire.FromPart}.{wire.FromPin} - {wire.ToPart}.{wire.ToPin} pulled out; " +
                          "wiring now: " + (circuit.Warnings.Count == 0 ? "no findings" : string.Join(", ", circuit.Warnings)));
                WirePulledOut?.Invoke(wire);
            }
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

            // 0. A wire stretched between two bodies that moved apart comes off its pin; the rest is the circuit.
            if (tethers != null && tethers.Pull()) Rewire();

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
            tethers?.Draw();

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
