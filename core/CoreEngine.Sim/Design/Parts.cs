using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    /// <summary>What a pin carries; used for colours in the wiring view and for the wiring check.</summary>
    public enum PinKind { Signal, Power, Ground, Motor }

    public enum PartKind { Board, MotorDriver, Ultrasonic, Motor, Battery, Caster }

    /// <summary>
    /// Where a part goes (docs/03 §5.2): anywhere on the top deck, or in one of the chassis' fixed places
    /// (motor mounts, the sensor bracket at the front, the caster mount, the space between the decks).
    /// </summary>
    public enum MountKind { Deck, Motor, Front, Caster, Lower }

    /// <summary>A pin, terminal or lead end, in millimetres in the part's own frame (y up, +z to the part's front).</summary>
    public sealed class PinDef
    {
        public PinDef(string id, string label, PinKind kind, float x, float y, float z)
        {
            Id = id;
            Label = label;
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
        }

        public string Id { get; }
        public string Label { get; }
        public PinKind Kind { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    /// <summary>A catalogue part (docs/09): real name, size, mass and pins. The frame's origin is the
    /// centre of the part's footprint on its mounting surface.</summary>
    public sealed class PartDef
    {
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
        }

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

        public static readonly IReadOnlyList<PartDef> All = new[]
        {
            UnoDef(),
            new PartDef(L298N, "L298N motor driver", PartKind.MotorDriver, MountKind.Deck, 43, 27, 43, 26, 2,
                new PinDef("ENA", "ENA · enable A (jumper fitted)", PinKind.Signal, -6.35f, 11, 19),
                new PinDef("IN1", "IN1 · input 1", PinKind.Signal, -3.81f, 11, 19),
                new PinDef("IN2", "IN2 · input 2", PinKind.Signal, -1.27f, 11, 19),
                new PinDef("IN3", "IN3 · input 3", PinKind.Signal, 1.27f, 11, 19),
                new PinDef("IN4", "IN4 · input 4", PinKind.Signal, 3.81f, 11, 19),
                new PinDef("ENB", "ENB · enable B (jumper fitted)", PinKind.Signal, 6.35f, 11, 19),
                new PinDef("+12V", "+12V · motor supply (5–35 V)", PinKind.Power, -5, 10, -17),
                new PinDef("GND", "GND", PinKind.Ground, 0, 10, -17),
                new PinDef("+5V", "+5V · 78M05 output (5V-EN jumper fitted)", PinKind.Power, 5, 10, -17),
                new PinDef("OUT1", "OUT1 · motor A", PinKind.Motor, -18, 10, 5),
                new PinDef("OUT2", "OUT2 · motor A", PinKind.Motor, -18, 10, -2),
                new PinDef("OUT3", "OUT3 · motor B", PinKind.Motor, 18, 10, -2),
                new PinDef("OUT4", "OUT4 · motor B", PinKind.Motor, 18, 10, 5)),
            new PartDef(HcSr04, "HC-SR04 ultrasonic sensor", PartKind.Ultrasonic, MountKind.Front, 45, 20, 15, 8.5f, 1,
                new PinDef("VCC", "VCC · 5 V", PinKind.Power, -3.81f, -9, -2),
                new PinDef("TRIG", "TRIG · trigger input", PinKind.Signal, -1.27f, -9, -2),
                new PinDef("ECHO", "ECHO · echo output", PinKind.Signal, 1.27f, -9, -2),
                new PinDef("GND", "GND", PinKind.Ground, 3.81f, -9, -2)),
            new PartDef(TtMotor, "TT gear motor 1:48", PartKind.Motor, MountKind.Motor, 19, 22, 70, 30.6f, 2,
                new PinDef("M+", "M+ · red lead", PinKind.Motor, 0, 4, 34),
                new PinDef("M-", "M− · black lead", PinKind.Motor, 0, -4, 34)),
            new PartDef(Battery4AA, "Battery holder 4×AA", PartKind.Battery, MountKind.Lower, 58, 15, 62, 20, 1,
                new PinDef("+", "+ · red lead (≈ 6 V)", PinKind.Power, 12, 8, -31),
                new PinDef("-", "− · black lead", PinKind.Ground, -12, 8, -31)),
            new PartDef(Caster, "Ball caster 20 mm", PartKind.Caster, MountKind.Caster, 30, 25, 30, 15, 1),
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
