using System.Collections.Generic;

namespace CoreEngine.Sim.Avr
{
    /// <summary>
    /// How much of a stretch of emulated time each pin spent driving high: a PWM signal's duty, for the parts
    /// that respond to its average (a motor driver's enable or inputs, an LED's brightness). It follows the
    /// ports' <see cref="AvrPort.PinDriveChanged"/> events, which carry the exact cycle of each edge, so it is
    /// right whatever the PWM frequency and however the window falls on the waveform.
    /// </summary>
    public sealed class PinDuty
    {
        sealed class Pin
        {
            public bool High;
            public long Since;       // when it last changed (or the window started)
            public long HighCycles;  // high time in the window before Since
        }

        readonly Dictionary<(char port, int bit), Pin> pins = new Dictionary<(char, int), Pin>();
        long windowStart;

        public PinDuty(Atmega328P mcu)
        {
            foreach (var port in new[] { mcu.PortB, mcu.PortC, mcu.PortD })
            {
                for (int bit = 0; bit < 8; bit++)
                    pins[(port.Name, bit)] = new Pin { High = port.GetDrive(bit) == PinDrive.High, Since = mcu.Cpu.Cycles };
                port.PinDriveChanged += OnChange;
            }
            windowStart = mcu.Cpu.Cycles;
        }

        void OnChange(AvrPort port, int bit, PinDrive drive, long cycle)
        {
            var pin = pins[(port.Name, bit)];
            if (pin.High && cycle > pin.Since) pin.HighCycles += cycle - pin.Since;
            pin.High = drive == PinDrive.High;
            pin.Since = cycle;
        }

        /// <summary>Starts a new window at <paramref name="cycle"/> (the start of an emulation step).</summary>
        public void Begin(long cycle)
        {
            windowStart = cycle;
            foreach (var pin in pins.Values)
            {
                pin.HighCycles = 0;
                pin.Since = cycle;
            }
        }

        /// <summary>
        /// The share of the window from <see cref="Begin"/> to <paramref name="end"/> that an Uno pin ("D5", "A0")
        /// drove high, 0 to 1; its level now when the window is empty; 0 for a name that is not a port pin.
        /// </summary>
        public double Duty(string name, long end)
        {
            var io = Design.UnoPins.PortOf(name);
            if (io == null || !pins.TryGetValue(io.Value, out var pin)) return 0;
            long length = end - windowStart;
            if (length <= 0) return pin.High ? 1 : 0;
            long high = pin.HighCycles + (pin.High && end > pin.Since ? end - pin.Since : 0);
            return System.Math.Min(1.0, (double)high / length);
        }
    }
}
