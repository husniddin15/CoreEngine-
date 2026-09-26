using System;
using CoreEngine.Sim.Avr;

namespace CoreEngine.Sim.Components
{
    /// <summary>
    /// HC-SR04 ultrasonic ranger wired to two MCU pins (docs/07-physics-world-sensors-spec.md §5.1).
    /// A trigger pulse of at least 10 µs starts a measurement; after the burst delay the echo pin goes
    /// high for the round-trip time 2·d / v, with v = 331.3 + 0.606·T m/s, or 38 ms when nothing
    /// is in range. The echo edges are scheduled at exact CPU cycles, so pulseIn() measures them as on
    /// real hardware. Triggers during a measurement are ignored.
    /// Phase 0 spike version: the distance comes from a callback (the host's raycast); the 17-ray
    /// cone, surface acceptance, noise and cross-talk come with the Phase 1 sensor model.
    /// </summary>
    public sealed class HcSr04
    {
        public const double MinTriggerMicros = 10;
        public const double EchoDelayMicros = 460;      // 8-cycle 40 kHz burst plus module latency [VERIFY on the fidelity bench]
        public const double TimeoutMicros = 38000;
        public const double MinRangeCm = 2;
        public const double MaxRangeCm = 400;

        readonly AvrCpu cpu;
        readonly double clockHz;
        readonly int triggerBit;
        readonly AvrPort echoPort;
        readonly int echoBit;
        readonly Func<double> distanceCm;
        readonly int eventId;

        long triggerRiseCycle = -1;
        long echoEndCycle;
        int phase; // 0 idle, 1 waiting for the echo to start, 2 echo high
        bool live = true;

        /// <param name="distanceCm">Distance to the nearest target in cm, or NaN when nothing is in range.</param>
        public HcSr04(AvrCpu cpu, double clockHz, AvrPort triggerPort, int triggerBit, AvrPort echoPort, int echoBit, Func<double> distanceCm)
        {
            this.cpu = cpu;
            this.clockHz = clockHz;
            this.triggerBit = triggerBit;
            this.echoPort = echoPort;
            this.echoBit = echoBit;
            this.distanceCm = distanceCm;
            eventId = cpu.RegisterEventSource(OnEvent);
            echoPort.SetInputLevel(echoBit, false);
            triggerPort.PinDriveChanged += OnTriggerChanged;
        }

        /// <summary>Air temperature for the speed of sound.</summary>
        public double AirTemperatureC { get; set; } = 20;

        /// <summary>Distance used by the most recent measurement (cm, NaN when out of range).</summary>
        public double LastDistanceCm { get; private set; } = double.NaN;

        public int Measurements { get; private set; }

        public bool Busy => phase != 0;

        /// <summary>
        /// False once the module has lost its supply or its TRIG or ECHO wire (a wire pulled out in the arena): it
        /// ignores triggers and drops an echo it was giving, so pulseIn() waits in vain, as on a desk.
        /// </summary>
        public bool Live
        {
            get => live;
            set
            {
                live = value;
                if (live || phase == 0) return;
                echoPort.SetInputLevel(echoBit, false);
                phase = 0;
            }
        }

        /// <summary>Echo pulse length in microseconds for a distance in cm (NaN or beyond 4 m: timeout).</summary>
        public double EchoMicros(double cm)
        {
            if (double.IsNaN(cm) || cm > MaxRangeCm) return TimeoutMicros;
            double speed = 331.3 + 0.606 * AirTemperatureC;
            return 2 * (Math.Max(cm, MinRangeCm) / 100.0) / speed * 1e6;
        }

        void OnTriggerChanged(AvrPort port, int bit, PinDrive drive, long cycle)
        {
            if (bit != triggerBit) return;
            if (!live)
            {
                triggerRiseCycle = -1;
                return;
            }
            if (drive == PinDrive.High)
            {
                triggerRiseCycle = cycle;
                return;
            }
            if (triggerRiseCycle < 0) return;
            double widthMicros = (cycle - triggerRiseCycle) / clockHz * 1e6;
            triggerRiseCycle = -1;
            if (widthMicros < MinTriggerMicros || phase != 0) return;

            double cm = distanceCm();
            LastDistanceCm = cm;
            Measurements++;
            long echoStart = cycle + MicrosToCycles(EchoDelayMicros);
            echoEndCycle = echoStart + MicrosToCycles(EchoMicros(cm));
            phase = 1;
            cpu.ScheduleEvent(eventId, echoStart);
        }

        void OnEvent(long cycle)
        {
            if (!live) return; // cut off mid-measurement: Live already let the echo pin fall
            if (phase == 1)
            {
                echoPort.SetInputLevel(echoBit, true);
                phase = 2;
                cpu.ScheduleEvent(eventId, echoEndCycle);
            }
            else if (phase == 2)
            {
                echoPort.SetInputLevel(echoBit, false);
                phase = 0;
            }
        }

        long MicrosToCycles(double micros) => (long)Math.Round(micros * clockHz / 1e6);
    }
}
