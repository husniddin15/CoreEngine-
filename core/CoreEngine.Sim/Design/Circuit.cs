using System;
using System.Collections.Generic;
using CoreEngine.Sim.Components;

namespace CoreEngine.Sim.Design
{
    /// <summary>Which pins are joined by wires (and inside parts): union-find over "part/pin" keys.</summary>
    public sealed class Netlist
    {
        readonly Dictionary<string, string> parent = new Dictionary<string, string>();

        static string Key(string part, string pin) => part + "/" + pin;

        public static Netlist Build(RobotDesign design)
        {
            var net = new Netlist();
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                foreach (var pin in def.Pins) net.Register(Key(part.Id, pin.Id));
                foreach (var (a, b) in PartCatalog.InternalConnections(part.Part)) net.Union(Key(part.Id, a), Key(part.Id, b));
            }
            foreach (var wire in design.Wires)
            {
                string a = Key(wire.FromPart, wire.FromPin), b = Key(wire.ToPart, wire.ToPin);
                if (net.parent.ContainsKey(a) && net.parent.ContainsKey(b)) net.Union(a, b);
            }
            return net;
        }

        public bool Connected(string partA, string pinA, string partB, string pinB)
        {
            string a = Key(partA, pinA), b = Key(partB, pinB);
            return parent.ContainsKey(a) && parent.ContainsKey(b) && Find(a) == Find(b);
        }

        /// <summary>Every pin on the same net as the given pin, including itself.</summary>
        public List<(string part, string pin)> Members(string part, string pin)
        {
            var result = new List<(string, string)>();
            string key = Key(part, pin);
            if (!parent.ContainsKey(key)) return result;
            string root = Find(key);
            foreach (string other in new List<string>(parent.Keys))
            {
                if (Find(other) != root) continue;
                int slash = other.IndexOf('/');
                result.Add((other.Substring(0, slash), other.Substring(slash + 1)));
            }
            return result;
        }

        void Register(string key)
        {
            if (!parent.ContainsKey(key)) parent[key] = key;
        }

        string Find(string key)
        {
            string root = key;
            while (parent[root] != root) root = parent[root];
            while (parent[key] != root)
            {
                string next = parent[key];
                parent[key] = root;
                key = next;
            }
            return root;
        }

        void Union(string a, string b)
        {
            string ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }
    }

    /// <summary>A motor's connection to an L298N channel. Polarity +1: M+ on OUT1 (or OUT3).</summary>
    public sealed class MotorLink
    {
        public MotorLink(string motorId, string slot, int channel, int polarity)
        {
            MotorId = motorId;
            Slot = slot;
            Channel = channel;
            Polarity = polarity;
        }

        public string MotorId { get; }
        public string Slot { get; }
        public int Channel { get; }
        public int Polarity { get; }
    }

    /// <summary>A finding of the wiring check (docs/03 §6.3). Code and arguments are localised by the UI.</summary>
    public sealed class CircuitWarning
    {
        public CircuitWarning(string code, bool info, params string[] args)
        {
            Code = code;
            Info = info;
            Args = args;
        }

        public string Code { get; }
        public bool Info { get; }
        public string[] Args { get; }
        public override string ToString() => Args.Length == 0 ? Code : Code + "(" + string.Join(", ", Args) + ")";
    }

    /// <summary>
    /// What the wires make of the parts, for the digital-level simulation of the Phase 1 slice: which parts
    /// have power, which Uno pin drives which input, where the sensor's pins go, and how the motors are
    /// connected. The analogue solver of docs/06 replaces the power rules later; the pin mapping stays.
    /// </summary>
    public sealed class RobotCircuit
    {
        public static readonly string[] DriverInputNames = { "IN1", "IN2", "IN3", "IN4", "ENA", "ENB" };

        public bool BoardPowered { get; internal set; }
        public bool BoardDamaged { get; internal set; }
        public bool DriverPowered { get; internal set; }
        public bool SonarPowered { get; internal set; }
        public bool SonarDamaged { get; internal set; }

        /// <summary>Uno pin (for example "D9") on each input's net, or null when floating. ENA/ENB null means the jumper enables the channel.</summary>
        public string?[] DriverInputs { get; } = new string?[6];
        public string? Trig { get; internal set; }
        public string? Echo { get; internal set; }
        public List<MotorLink> Motors { get; } = new List<MotorLink>();
        public List<CircuitWarning> Warnings { get; } = new List<CircuitWarning>();

        /// <summary>The motor on that side ("left" or "right"), or with that part id.</summary>
        public MotorLink? Motor(string slotOrId)
        {
            foreach (var m in Motors) if (m.MotorId == slotOrId) return m;
            foreach (var m in Motors) if (m.Slot == slotOrId) return m;
            return null;
        }

        public bool HasProblems
        {
            get
            {
                foreach (var w in Warnings) if (!w.Info) return true;
                return false;
            }
        }
    }

    public static class CircuitAnalysis
    {
        public static RobotCircuit Analyse(RobotDesign design)
        {
            var circuit = new RobotCircuit();
            var net = Netlist.Build(design);
            PartInstance? First(PartKind kind)
            {
                foreach (var part in design.Parts)
                    if (PartCatalog.Get(part.Part)?.Kind == kind) return part;
                return null;
            }
            var battery = First(PartKind.Battery);
            var uno = First(PartKind.Board);
            var driver = First(PartKind.MotorDriver);
            var sonar = First(PartKind.Ultrasonic);
            void Warn(string code, params string[] args) => circuit.Warnings.Add(new CircuitWarning(code, false, args));
            void Note(string code, params string[] args) => circuit.Warnings.Add(new CircuitWarning(code, true, args));
            bool Conn(PartInstance? a, string pa, PartInstance? b, string pb) => a != null && b != null && net.Connected(a.Id, pa, b.Id, pb);

            bool shorted = battery != null && Conn(battery, "+", battery, "-");
            if (battery == null) Warn("noBattery");
            if (shorted) Warn("shortCircuit");
            bool BatteryPlus(PartInstance? p, string pin) => !shorted && Conn(p, pin, battery, "+");
            bool Ground(PartInstance? p, string pin) => !shorted && Conn(p, pin, battery, "-");

            // L298N: motor supply on +12V and GND; its 78M05 feeds the +5V terminal (5V-EN jumper fitted).
            circuit.DriverPowered = driver != null && BatteryPlus(driver, "+12V") && Ground(driver, "GND");
            if (driver != null && !circuit.DriverPowered) Warn("driverUnpowered");
            bool DriverFive(PartInstance? p, string pin) => circuit.DriverPowered && Conn(p, pin, driver, "+5V");

            // The Uno runs from VIN or its 5V pin, with its GND on the battery's minus.
            if (uno == null)
            {
                Warn("noBoard");
            }
            else
            {
                bool grounded = Ground(uno, "GND.1");
                bool vin = BatteryPlus(uno, "VIN");
                bool fromDriver = DriverFive(uno, "5V");
                if (BatteryPlus(uno, "5V"))
                {
                    circuit.BoardDamaged = true; // F7: about 6 V on the 5V pin, absolute maximum 5.5 V
                    Warn("board5vOvervoltage");
                }
                circuit.BoardPowered = grounded && !circuit.BoardDamaged && (vin || fromDriver);
                if (!circuit.BoardPowered && !circuit.BoardDamaged) Warn(!grounded && (vin || fromDriver) ? "noGround" : "boardUnpowered");
                if (circuit.BoardPowered && fromDriver) Note("driver5vLow");
            }

            bool FiveVolt(PartInstance? p, string pin) => (circuit.BoardPowered && Conn(p, pin, uno, "5V")) || DriverFive(p, pin);
            bool LogicGround(PartInstance? p, string pin) => Ground(p, pin) || (circuit.BoardPowered && Conn(p, pin, uno, "GND.1"));

            string? UnoPin(PartInstance? p, string pin)
            {
                if (p == null || uno == null) return null;
                string? found = null;
                foreach (var (part, other) in net.Members(p.Id, pin))
                {
                    if (part != uno.Id || !UnoPins.IsIo(other)) continue;
                    if (found != null && found != other) Warn("pinsJoined", found, other);
                    found ??= other;
                }
                return found;
            }

            if (sonar != null)
            {
                if (BatteryPlus(sonar, "VCC"))
                {
                    circuit.SonarDamaged = true; // F26: more than 5.5 V on VCC
                    Warn("sonarOvervoltage");
                }
                circuit.SonarPowered = !circuit.SonarDamaged && FiveVolt(sonar, "VCC") && LogicGround(sonar, "GND");
                if (!circuit.SonarPowered && !circuit.SonarDamaged) Warn("sonarUnpowered");
                circuit.Trig = UnoPin(sonar, "TRIG");
                circuit.Echo = UnoPin(sonar, "ECHO");
                if (circuit.Trig == null) Warn("sonarPin", "TRIG");
                if (circuit.Echo == null) Warn("sonarPin", "ECHO");
            }

            if (driver != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    string name = RobotCircuit.DriverInputNames[i];
                    circuit.DriverInputs[i] = UnoPin(driver, name);
                    if (i < 4 && circuit.DriverInputs[i] == null) Warn("inputFloating", name);
                }
            }

            foreach (var motor in design.Parts)
            {
                if (PartCatalog.Get(motor.Part)?.Kind != PartKind.Motor) continue;
                string side = DesignGeometry.SideOf(motor);
                MotorLink? link = null;
                if (driver != null)
                {
                    (string plus, string minus, int channel, int polarity)[] options =
                    {
                        ("OUT1", "OUT2", 0, 1), ("OUT2", "OUT1", 0, -1), ("OUT3", "OUT4", 1, 1), ("OUT4", "OUT3", 1, -1),
                    };
                    foreach (var o in options)
                        if (Conn(motor, "M+", driver, o.plus) && Conn(motor, "M-", driver, o.minus))
                            link = new MotorLink(motor.Id, side, o.channel, o.polarity);
                }
                if (link != null)
                {
                    circuit.Motors.Add(link);
                    continue;
                }
                string? gpio = UnoPin(motor, "M+") ?? UnoPin(motor, "M-");
                if (gpio != null) Warn("motorOnGpio", side, gpio);
                else Warn("motorNotConnected", side);
            }
            return circuit;
        }
    }

    /// <summary>
    /// From driver inputs to wheel drive in the arena (docs/06 §5.11): the L298N channel a motor is on, the
    /// channel's enable (the ENA/ENB jumper, unless a wire takes the pin to the Uno) and the lead polarity. Which
    /// way the wheel then rolls follows from how the motor was mounted (<see cref="DesignGeometry.ForwardSign"/>).
    /// </summary>
    public static class DriveMap
    {
        /// <summary>Voltage across a motor's M+ and M− leads; NaN when they are open (not on the driver, no supply, channel disabled).</summary>
        /// <param name="slotOrId">The motor's part id, or its side ("left", "right").</param>
        /// <param name="pinHigh">Whether an Uno pin (for example "D5") is driven high.</param>
        public static double MotorVolts(RobotCircuit circuit, string slotOrId, Func<string, bool> pinHigh, L298NModel bridge)
        {
            var link = circuit.Motor(slotOrId);
            if (link == null || !circuit.DriverPowered) return double.NaN;
            bool High(string? pin) => pin != null && pinHigh(pin); // a floating input reads low
            int a = 2 * link.Channel;
            string? enable = circuit.DriverInputs[4 + link.Channel];
            double volts = bridge.ChannelVolts(enable == null || High(enable), High(circuit.DriverInputs[a]), High(circuit.DriverInputs[a + 1]));
            return volts == 0 ? 0 : volts * link.Polarity;
        }
    }
}
