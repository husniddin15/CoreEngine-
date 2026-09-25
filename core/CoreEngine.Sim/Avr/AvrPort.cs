using System;

namespace CoreEngine.Sim.Avr
{
    /// <summary>What an MCU pin does electrically (docs/05-arduino-emulation-spec.md §4).</summary>
    public enum PinDrive : byte
    {
        /// <summary>Input without pull-up: high impedance.</summary>
        HighZ,
        /// <summary>Input with the internal pull-up enabled.</summary>
        PullUp,
        /// <summary>Output driving low.</summary>
        Low,
        /// <summary>Output driving high.</summary>
        High,
    }

    /// <summary>Signature of <see cref="AvrPort.PinDriveChanged"/>.</summary>
    public delegate void PinDriveChangedHandler(AvrPort port, int bit, PinDrive drive, long cycle);

    /// <summary>
    /// A GPIO port with PINx, DDRx and PORTx registers at consecutive data addresses.
    /// Writing a one to PINx toggles the matching PORTx bit. A peripheral can take over an output pin, as a
    /// timer's output-compare unit does for PWM (<see cref="SetAlternateOutput"/>): while it holds the pin, an
    /// output drives the peripheral's level instead of the PORTx bit.
    /// </summary>
    public sealed class AvrPort
    {
        readonly AvrCpu cpu;
        readonly int pinAddress;
        readonly int ddrAddress;
        readonly int portAddress;

        byte ddr;
        byte port;
        byte externalLevels;
        byte externallyDriven;
        byte alternate;       // pins a peripheral drives
        byte alternateLevels; // and the levels it drives them to

        public AvrPort(AvrCpu cpu, char name, int pinAddress)
        {
            this.cpu = cpu;
            Name = name;
            this.pinAddress = pinAddress;
            ddrAddress = pinAddress + 1;
            portAddress = pinAddress + 2;

            cpu.SetReadHook(pinAddress, _ => ReadPins());
            cpu.SetWriteHook(pinAddress, (_, value, mask) => Toggle((byte)(value & mask)));
            cpu.SetReadHook(ddrAddress, _ => ddr);
            cpu.SetWriteHook(ddrAddress, (_, value, _) => Update(value, port));
            cpu.SetReadHook(portAddress, _ => port);
            cpu.SetWriteHook(portAddress, (_, value, _) => Update(ddr, value));
        }

        /// <summary>Port letter: 'B', 'C' or 'D'.</summary>
        public char Name { get; }

        public byte Ddr => ddr;

        public byte PortRegister => port;

        /// <summary>Raised whenever a pin's drive state changes.</summary>
        public event PinDriveChangedHandler? PinDriveChanged;

        public PinDrive GetDrive(int bit) => DriveOf(ddr, port, alternate, alternateLevels, bit);

        /// <summary>
        /// A peripheral takes over a pin's output with <paramref name="level"/>, or gives it back with null. It shows
        /// only while the pin is an output, as on the chip (a timer's OCnx needs its DDR bit set).
        /// </summary>
        /// <param name="cycle">When it happened, for <see cref="PinDriveChanged"/>: a timer's compare match, which the
        /// CPU may reach a few cycles later.</param>
        public void SetAlternateOutput(int bit, bool? level, long cycle)
        {
            byte m = (byte)(1 << bit);
            byte newAlternate = level == null ? (byte)(alternate & ~m) : (byte)(alternate | m);
            byte newLevels = level == true ? (byte)(alternateLevels | m) : (byte)(alternateLevels & ~m);
            if (newAlternate == alternate && newLevels == alternateLevels) return;
            Apply(ddr, port, newAlternate, newLevels, cycle);
        }

        /// <summary>
        /// Sets the level an external circuit applies to an input pin, or null when nothing drives it.
        /// Until the electrical model exists, an undriven input reads its pull-up (1) or 0.
        /// </summary>
        public void SetInputLevel(int bit, bool? level)
        {
            byte m = (byte)(1 << bit);
            if (level == null)
            {
                externallyDriven &= (byte)~m;
                externalLevels &= (byte)~m;
            }
            else
            {
                externallyDriven |= m;
                if (level.Value) externalLevels |= m;
                else externalLevels &= (byte)~m;
            }
        }

        public void Reset()
        {
            Apply(0, 0, 0, 0, cpu.Cycles);
        }

        byte ReadPins()
        {
            byte outputs = (byte)(ddr & ((port & ~alternate) | (alternateLevels & alternate)));
            byte undrivenInputs = (byte)(~ddr & ~externallyDriven & port); // pull-ups read high
            byte drivenInputs = (byte)(~ddr & externallyDriven & externalLevels);
            return (byte)(outputs | undrivenInputs | drivenInputs);
        }

        void Toggle(byte bits)
        {
            if (bits != 0) Update(ddr, (byte)(port ^ bits));
        }

        void Update(byte newDdr, byte newPort) => Apply(newDdr, newPort, alternate, alternateLevels, cpu.Cycles);

        void Apply(byte newDdr, byte newPort, byte newAlternate, byte newLevels, long cycle)
        {
            byte oldDdr = ddr, oldPort = port, oldAlternate = alternate, oldLevels = alternateLevels;
            ddr = newDdr;
            port = newPort;
            alternate = newAlternate;
            alternateLevels = newLevels;
            var data = cpu.DataSpace;
            data[ddrAddress] = ddr;
            data[portAddress] = port;

            var handler = PinDriveChanged;
            if (handler == null) return;
            for (int bit = 0; bit < 8; bit++)
            {
                PinDrive before = DriveOf(oldDdr, oldPort, oldAlternate, oldLevels, bit);
                PinDrive after = DriveOf(ddr, port, alternate, alternateLevels, bit);
                if (before != after) handler(this, bit, after, cycle);
            }
        }

        static PinDrive DriveOf(byte ddr, byte port, byte alternate, byte levels, int bit)
        {
            int m = 1 << bit;
            bool output = (ddr & m) != 0;
            if (output) return ((alternate & m) != 0 ? (levels & m) != 0 : (port & m) != 0) ? PinDrive.High : PinDrive.Low;
            return (port & m) != 0 ? PinDrive.PullUp : PinDrive.HighZ;
        }
    }
}
