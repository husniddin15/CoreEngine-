using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    /// <summary>What a pin carries; used for colours in the wiring view and for the wiring check.</summary>
    public enum PinKind { Signal, Power, Ground, Motor }

    public enum PartKind { Board, MotorDriver, Ultrasonic, Motor, Battery, Caster, Servo, Led }

    /// <summary>
    /// Where a part usually goes. Since 2026-09-24 every part is placed and turned freely by the player; the
    /// kind only says where a new one appears and where old saves had it (docs/03 §5.2): on a deck, under the
    /// body as a motor or caster, upright at the front as the sensor, or lower down as the battery holder.
    /// </summary>
    public enum MountKind { Deck, Motor, Front, Caster, Lower }

    /// <summary>
    /// How a wire meets a pin: a jumper's male end in a female header (the Uno), a jumper's female end on a
    /// male header pin (L298N, HC-SR04), a stripped end in a screw terminal, or the part's own lead.
    /// </summary>
    public enum PinStyle { Header, Pin, Terminal, Lead }

    /// <summary>
    /// A pin, terminal or lead end, in millimetres in the part's own frame (y up, +z to the part's front), and
    /// the direction a wire leaves it: up for header pins, sideways out of a screw terminal, along a lead.
    /// </summary>
    public sealed class PinDef
    {
        public PinDef(string id, string label, PinKind kind, float x, float y, float z,
            PinStyle style = PinStyle.Header, float exitX = 0, float exitY = 1, float exitZ = 0)
        {
            Id = id;
            Label = label;
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
            Style = style;
            ExitX = exitX;
            ExitY = exitY;
            ExitZ = exitZ;
        }

        public string Id { get; }
        public string Label { get; }
        public PinKind Kind { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public PinStyle Style { get; }
        public float ExitX { get; }
        public float ExitY { get; }
        public float ExitZ { get; }

        /// <summary>
        /// Wires the pin takes: one jumper per header pin and one place per lead end; a screw terminal
        /// clamps two stripped wires.
        /// </summary>
        public int Capacity => Style == PinStyle.Terminal ? 2 : 1;

        /// <summary>The printed name: the label before its first " · " ("D9", "+12V", "GND").</summary>
        public string ShortLabel
        {
            get
            {
                int dot = Label.IndexOf(" · ", StringComparison.Ordinal);
                return dot > 0 ? Label.Substring(0, dot) : Label;
            }
        }
    }

    /// <summary>A solid block of a part: a box in the part's frame (mm), for what wires must go round.</summary>
    public readonly struct PartBlock
    {
        public PartBlock((float x, float y, float z) centre, (float x, float y, float z) size)
        {
            Centre = centre;
            Size = size;
        }

        public (float x, float y, float z) Centre { get; }
        public (float x, float y, float z) Size { get; }
    }

    /// <summary>
    /// A catalogue part (docs/09): real name, size, mass and pins. Real parts cannot be resized, only placed and
    /// turned. Sizes are the part's bounding box in its own frame, centred on <see cref="BoxCentre"/>; for
    /// most parts the frame's origin is the middle of the face that stands on a surface.
    /// </summary>
    public sealed class PartDef
    {
        PartBlock[]? blocks;
        public PartDef(string id, string name, PartKind kind, MountKind mount, float sizeX, float sizeY, float sizeZ, float massG, int maxCount, params PinDef[] pins)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Mount = mount;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            MassG = massG;
            MaxCount = maxCount;
            Pins = pins;
            BoxCentre = (0, sizeY / 2, 0);
        }

        /// <summary>The middle of the bounding box in the part's frame (mm).</summary>
        public (float x, float y, float z) BoxCentre { get; private set; }

        /// <summary>The point of the part that touches the surface it is mounted on (mm, part frame).</summary>
        public (float x, float y, float z) MountPoint { get; private set; }

        /// <summary>
        /// The way the mounting face looks, in the part's frame: down for a board standing on a plate, up for a
        /// motor or caster hanging under one. Placing a part on a face turns this against the face's normal.
        /// </summary>
        public (float x, float y, float z) MountNormal { get; private set; } = (0, -1, 0);

        /// <summary>For parts whose frame is not the middle of their bottom face (catalogue set-up only).</summary>
        internal PartDef Frame((float x, float y, float z) boxCentre, (float x, float y, float z) mountPoint, (float x, float y, float z) mountNormal)
        {
            BoxCentre = boxCentre;
            MountPoint = mountPoint;
            MountNormal = mountNormal;
            return this;
        }

        /// <summary>For a part whose bounding box would keep wires away from its pins (catalogue set-up only).</summary>
        internal PartDef Blocks(params PartBlock[] solids)
        {
            blocks = solids;
            return this;
        }

        /// <summary>
        /// The part's solid blocks, which jumper wires go round (<see cref="WireRouter"/>): its bounding box, or for
        /// a part with a tall piece beside its pins (the L298N's heatsink next to its header) the pieces themselves.
        /// </summary>
        public IReadOnlyList<PartBlock> Solids => blocks ??= new[] { new PartBlock(BoxCentre, (SizeX, SizeY, SizeZ)) };

        public string Id { get; }
        public string Name { get; }
        public PartKind Kind { get; }
        public MountKind Mount { get; }
        public float SizeX { get; }
        public float SizeY { get; }
        public float SizeZ { get; }
        public float MassG { get; }
        public int MaxCount { get; }
        public IReadOnlyList<PinDef> Pins { get; }

        public PinDef? Pin(string id)
        {
            foreach (var pin in Pins) if (pin.Id == id) return pin;
            return null;
        }
    }

    /// <summary>
    /// The parts of the obstacle-avoider class of robots (the Phase 1 slice, docs/11 §4). Dimensions,
    /// masses and pin names follow docs/09; pin positions are approximate header and terminal places [VERIFY].
    /// </summary>
    public static class PartCatalog
    {
        public const string Uno = "board-uno-r3";
        public const string L298N = "drv-l298n-module";
        public const string HcSr04 = "sens-hc-sr04";
        public const string TtMotor = "motor-tt-1-48";
        public const string Battery4AA = "bat-4aa-holder";
        public const string Caster = "caster-ball-20";
        public const string Servo = "servo-sg90";
        public const string Led = "led-module-5mm";

        public static readonly IReadOnlyList<PartDef> All = new[]
        {
            UnoDef(),
            // The common red module: the heatsink at the back (+z) with a motor terminal either side of it, the
            // power terminal at the front left and the logic header at the front right, its ENA and ENB jumpers
            // to the pins behind them.
            new PartDef(L298N, "L298N motor driver", PartKind.MotorDriver, MountKind.Deck, 43, 27, 43, 26, 2,
                new PinDef("ENA", "ENA · enable A (jumper fitted)", PinKind.Signal, 3.5f, 11, -14.5f, PinStyle.Pin),
                new PinDef("IN1", "IN1 · input 1", PinKind.Signal, 6.04f, 11, -14.5f, PinStyle.Pin),
                new PinDef("IN2", "IN2 · input 2", PinKind.Signal, 8.58f, 11, -14.5f, PinStyle.Pin),
                new PinDef("IN3", "IN3 · input 3", PinKind.Signal, 11.12f, 11, -14.5f, PinStyle.Pin),
                new PinDef("IN4", "IN4 · input 4", PinKind.Signal, 13.66f, 11, -14.5f, PinStyle.Pin),
                new PinDef("ENB", "ENB · enable B (jumper fitted)", PinKind.Signal, 16.2f, 11, -14.5f, PinStyle.Pin),
                new PinDef("+12V", "+12V · motor supply (5–35 V)", PinKind.Power, -12, 6.5f, -21.5f, PinStyle.Terminal, 0, 0, -1),
                new PinDef("GND", "GND", PinKind.Ground, -7, 6.5f, -21.5f, PinStyle.Terminal, 0, 0, -1),
                new PinDef("+5V", "+5V · 78M05 output (5V-EN jumper fitted)", PinKind.Power, -2, 6.5f, -21.5f, PinStyle.Terminal, 0, 0, -1),
                new PinDef("OUT1", "OUT1 · motor A", PinKind.Motor, -21.5f, 6.5f, 14, PinStyle.Terminal, -1, 0, 0),
                new PinDef("OUT2", "OUT2 · motor A", PinKind.Motor, -21.5f, 6.5f, 9, PinStyle.Terminal, -1, 0, 0),
                new PinDef("OUT3", "OUT3 · motor B", PinKind.Motor, 21.5f, 6.5f, 9, PinStyle.Terminal, 1, 0, 0),
                new PinDef("OUT4", "OUT4 · motor B", PinKind.Motor, 21.5f, 6.5f, 14, PinStyle.Terminal, 1, 0, 0))
                .Blocks(
                    new PartBlock((0, 2.8f, 0), (43, 2.4f, 43)),              // the board and its low parts
                    new PartBlock((0, 14.7f, 10), (23, 23, 19)),              // the heatsink with the chip against it
                    new PartBlock((-7, 8.2f, -17.7f), (15, 10, 7.6f)),        // the power terminal
                    new PartBlock((-17.7f, 8.2f, 11.5f), (7.6f, 10, 10)),     // the motor terminals
                    new PartBlock((17.7f, 8.2f, 11.5f), (7.6f, 10, 10)),
                    new PartBlock((9.85f, 7.1f, -13.2f), (15.3f, 7.8f, 5.2f)), // the logic header with its jumpers
                    new PartBlock((-16, 9, -3), (8, 11.5f, 8)),               // the two 220 µF capacitors
                    new PartBlock((16, 9, -3), (8, 11.5f, 8))),
            // The sensor's frame is the middle of its board; it stands on its bracket, transducers toward +z. Seen
            // from the front the pins read VCC, TRIG, ECHO, GND from left to right, as printed on the real board.
            new PartDef(HcSr04, "HC-SR04 ultrasonic sensor", PartKind.Ultrasonic, MountKind.Front, 45, 26, 22, 8.5f, 1,
                new PinDef("VCC", "VCC · 5 V", PinKind.Power, 3.81f, -9, -3.5f, PinStyle.Pin, 0, 0, -1),
                new PinDef("TRIG", "TRIG · trigger input", PinKind.Signal, 1.27f, -9, -3.5f, PinStyle.Pin, 0, 0, -1),
                new PinDef("ECHO", "ECHO · echo output", PinKind.Signal, -1.27f, -9, -3.5f, PinStyle.Pin, 0, 0, -1),
                new PinDef("GND", "GND", PinKind.Ground, -3.81f, -9, -3.5f, PinStyle.Pin, 0, 0, -1))
                .Frame((0, -3, 1.9f), (0, -16, -4), (0, -1, 0)),
            // The motor's frame is the middle of its gearbox; the shaft runs along x, 8.5 mm above it, with the
            // wheel on the −x side; the can and the leads point to +z. It hangs under a plate by its top face.
            new PartDef(TtMotor, "TT gear motor 1:48", PartKind.Motor, MountKind.Motor, 19, 22, 64, 30.6f, 2,
                new PinDef("M+", "M+ · red lead", PinKind.Motor, 0, 4, 45.5f, PinStyle.Lead, 0, 0, 1),
                new PinDef("M-", "M− · black lead", PinKind.Motor, 0, -4, 45.5f, PinStyle.Lead, 0, 0, 1))
                .Frame((0, 0, 13.5f), (0, 11, 0), (0, 1, 0)),
            new PartDef(Battery4AA, "Battery holder 4×AA", PartKind.Battery, MountKind.Lower, 58, 15, 62, 20, 1,
                new PinDef("+", "+ · red lead (≈ 6 V)", PinKind.Power, 12, 5, -31.5f, PinStyle.Lead, 0, 0, -1),
                new PinDef("-", "− · black lead", PinKind.Ground, -12, 5, -31.5f, PinStyle.Lead, 0, 0, -1))
                .Frame((0, 0, 0), (0, -7.5f, 0), (0, -1, 0)),
            // The caster's frame is the middle of its 20 mm ball; the holder's flange screws under a plate.
            new PartDef(Caster, "Ball caster 20 mm", PartKind.Caster, MountKind.Caster, 22, 35, 22, 15, 1)
                .Frame((0, 7.5f, 0), (0, 25, 0), (0, 1, 0)),
            // The SG90 stands on its base, its output shaft toward +x with the horn on top; its lead leaves the -x
            // end and doubles back along its side to the 3-way socket, whose mouth faces +x: brown GND, red V+,
            // orange signal, as on the real lead.
            new PartDef(Servo, "SG90 micro servo", PartKind.Servo, MountKind.Deck, 30.6f, 30.4f, 20.9f, 9, 2,
                new PinDef("GND", "GND · brown lead", PinKind.Ground, 2.2f, 1.3f, -8.46f, PinStyle.Header, 1, 0, 0),
                new PinDef("V+", "V+ · red lead (4.8–6 V)", PinKind.Power, 2.2f, 1.3f, -11f, PinStyle.Header, 1, 0, 0),
                new PinDef("SIG", "SIG · orange lead (servo pulses)", PinKind.Signal, 2.2f, 1.3f, -13.54f, PinStyle.Header, 1, 0, 0))
                .Frame((0.8f, 15.2f, -4.35f), (0, 0, 0), (0, -1, 0)),
            // A 5 mm red LED on a small board with its 220 Ω resistor: S lights it through the resistor, - is ground.
            new PartDef(Led, "LED module 5 mm (red)", PartKind.Led, MountKind.Deck, 20, 14.3f, 14, 2, 4,
                new PinDef("S", "S · signal (lights it when high)", PinKind.Signal, 7.46f, 11, -1.27f, PinStyle.Pin),
                new PinDef("GND", "− · ground", PinKind.Ground, 7.46f, 11, 1.27f, PinStyle.Pin)),
        };

        static PartDef UnoDef()
        {
            // Headers of the Uno R3 (docs/09 §2.1), in the board's top view with the USB jack at -x:
            // digital pins along +z, power and analog pins along -z. Origin at the board centre.
            var pins = new List<PinDef>();
            const float top = 11f;        // female header top, 11 mm above the mounting face
            const float digitalZ = 24.1f;  // 50.8 mm from the lower edge
            const float powerZ = -24.2f;   // 2.5 mm from the lower edge
            float X(float fromLeft) => fromLeft - 34.3f;
            pins.Add(new PinDef("AREF", "AREF", PinKind.Signal, X(23.0f), top, digitalZ));
            pins.Add(new PinDef("GND.3", "GND", PinKind.Ground, X(25.5f), top, digitalZ));
            string[] high = { "D13", "D12", "D11", "D10", "D9", "D8" };
            string[] highLabels = { "D13 · SCK · L LED", "D12 · MISO", "D11 · PWM · MOSI", "D10 · PWM · SS", "D9 · PWM · OC1A", "D8 · ICP1" };
            for (int i = 0; i < high.Length; i++) pins.Add(new PinDef(high[i], highLabels[i], PinKind.Signal, X(28.1f + 2.54f * i), top, digitalZ));
            string[] low = { "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0" };
            string[] lowLabels = { "D7", "D6 · PWM · OC0A", "D5 · PWM · OC0B", "D4", "D3 · PWM · INT1", "D2 · INT0", "D1 · TX", "D0 · RX" };
            for (int i = 0; i < low.Length; i++) pins.Add(new PinDef(low[i], lowLabels[i], PinKind.Signal, X(44.9f + 2.54f * i), top, digitalZ));
            string[] power = { "RESET", "3V3", "5V", "GND.1", "GND.2", "VIN" };
            string[] powerLabels = { "RESET", "3.3V · 50 mA", "5V", "GND", "GND", "VIN · 7–12 V" };
            PinKind[] powerKinds = { PinKind.Signal, PinKind.Power, PinKind.Power, PinKind.Ground, PinKind.Ground, PinKind.Power };
            for (int i = 0; i < power.Length; i++) pins.Add(new PinDef(power[i], powerLabels[i], powerKinds[i], X(30.5f + 2.54f * i), top, powerZ));
            for (int i = 0; i < 6; i++) pins.Add(new PinDef("A" + i, $"A{i} · analog / D{14 + i}", PinKind.Signal, X(50.8f + 2.54f * i), top, powerZ));
            return new PartDef(Uno, "Arduino Uno R3", PartKind.Board, MountKind.Deck, 68.6f, 15, 53.4f, 25, 1, pins.ToArray());
        }

        public static PartDef? Get(string id)
        {
            foreach (var part in All) if (part.Id == id) return part;
            return null;
        }

        /// <summary>Pins joined inside a part: the Uno's three GND pins are one net.</summary>
        public static IEnumerable<(string a, string b)> InternalConnections(string partId)
        {
            if (partId == Uno)
            {
                yield return ("GND.1", "GND.2");
                yield return ("GND.1", "GND.3");
            }
        }
    }

    /// <summary>Arduino Uno pin names to ATmega328P ports (docs/09 §2.1).</summary>
    public static class UnoPins
    {
        /// <summary>'B', 'C' or 'D' and the bit, or null for pins that are not port I/O.</summary>
        public static (char port, int bit)? PortOf(string pin)
        {
            if (pin.Length >= 2 && pin[0] == 'D' && int.TryParse(pin.Substring(1), out int d))
            {
                if (d >= 0 && d <= 7) return ('D', d);
                if (d >= 8 && d <= 13) return ('B', d - 8);
            }
            if (pin.Length == 2 && pin[0] == 'A' && pin[1] >= '0' && pin[1] <= '5') return ('C', pin[1] - '0');
            return null;
        }

        public static bool IsIo(string pin) => PortOf(pin) != null;
    }
}
