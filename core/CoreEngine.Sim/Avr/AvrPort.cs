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
    /// Writing a one to PINx toggles the matching PORTx bit.
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

        public PinDrive GetDrive(int bit) => DriveOf(ddr, port, bit);

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
            Update(0, 0);
        }

        byte ReadPins()
        {
            byte outputs = (byte)(ddr & port);
            byte undrivenInputs = (byte)(~ddr & ~externallyDriven & port); // pull-ups read high
            byte drivenInputs = (byte)(~ddr & externallyDriven & externalLevels);
            return (byte)(outputs | undrivenInputs | drivenInputs);
        }

        void Toggle(byte bits)
        {
            if (bits != 0) Update(ddr, (byte)(port ^ bits));
        }

        void Update(byte newDdr, byte newPort)
        {
            byte oldDdr = ddr, oldPort = port;
            ddr = newDdr;
            port = newPort;
            var data = cpu.DataSpace;
            data[ddrAddress] = ddr;
            data[portAddress] = port;

            var handler = PinDriveChanged;
            if (handler == null) return;
            for (int bit = 0; bit < 8; bit++)
            {
                PinDrive before = DriveOf(oldDdr, oldPort, bit);
                PinDrive after = DriveOf(ddr, port, bit);
                if (before != after) handler(this, bit, after, cpu.Cycles);
            }
        }

        static PinDrive DriveOf(byte ddr, byte port, int bit)
        {
            bool output = (ddr & (1 << bit)) != 0;
            bool high = (port & (1 << bit)) != 0;
            if (output) return high ? PinDrive.High : PinDrive.Low;
            return high ? PinDrive.PullUp : PinDrive.HighZ;
        }
    }
}
