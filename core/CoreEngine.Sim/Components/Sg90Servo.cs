using System;
using CoreEngine.Sim.Avr;

namespace CoreEngine.Sim.Components
{
    /// <summary>
    /// SG90 micro servo on one MCU pin (docs/09 §6). It times every high pulse on its signal pin to the CPU
    /// cycle and turns its horn toward the angle the pulse asks for: 544 µs is 0° and 2400 µs is 180°, the range
    /// Arduino's Servo library writes, so <c>servo.write(90)</c> centres it. Pulses shorter than 400 µs or longer
    /// than 2600 µs are not servo pulses and are ignored. The horn moves at the rated 0.1 s per 60° (4.8 V) while
    /// the servo has power, and holds where it is without it.
    /// </summary>
    public sealed class Sg90Servo
    {
        public const double ZeroMicros = 544, FullMicros = 2400;
        public const double DegreesPerSecond = 600;

        readonly double clockHz;
        readonly int bit;
        long riseCycle = -1;

        public Sg90Servo(AvrPort port, int bit, double clockHz)
        {
            this.bit = bit;
            this.clockHz = clockHz;
            port.PinDriveChanged += OnPinChanged;
        }

        /// <summary>The angle the last pulse asked for, in degrees from 0 to 180.</summary>
        public double TargetDegrees { get; private set; } = 90;

        /// <summary>Where the horn is, in degrees from 0 to 180.</summary>
        public double AngleDegrees { get; private set; } = 90;

        public double LastPulseMicros { get; private set; } = double.NaN;

        public int Pulses { get; private set; }

        void OnPinChanged(AvrPort port, int changed, PinDrive drive, long cycle)
        {
            if (changed != bit) return;
            if (drive == PinDrive.High)
            {
                riseCycle = cycle;
                return;
            }
            if (riseCycle < 0) return;
            double micros = (cycle - riseCycle) * 1e6 / clockHz;
            riseCycle = -1;
            if (micros < 400 || micros > 2600) return;
            LastPulseMicros = micros;
            Pulses++;
            TargetDegrees = Math.Max(0, Math.Min(180, (micros - ZeroMicros) / (FullMicros - ZeroMicros) * 180));
        }

        /// <summary>Turns the horn toward its target for <paramref name="seconds"/> of emulated time.</summary>
        public void Step(double seconds, bool powered)
        {
            if (!powered || seconds <= 0) return;
            double most = DegreesPerSecond * seconds;
            AngleDegrees += Math.Max(-most, Math.Min(most, TargetDegrees - AngleDegrees));
        }
    }
}
