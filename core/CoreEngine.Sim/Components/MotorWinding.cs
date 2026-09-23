using System;

namespace CoreEngine.Sim.Components
{
    /// <summary>
    /// Winding temperature of a DC motor (docs/06-electrical-simulation-spec.md §5.10):
    /// dT/dt = (i²·R·θ − (T − T_amb)) / τ with θ = 40 °C/W and τ = 60 s [VERIFY]. Above 120 °C the
    /// winding burns open (failure F18, §6) and the motor stops for good until it is replaced.
    /// With these parameters a TT motor stalled at 1.05 A burns in about 50 s.
    /// </summary>
    public sealed class MotorWinding
    {
        public const double AmbientC = 20;
        public const double FailureC = 120;
        public const double ThermalResistanceCPerW = 40;
        public const double TimeConstantSeconds = 60;

        public double TemperatureC { get; private set; } = AmbientC;

        /// <summary>Highest temperature since the motor was fitted.</summary>
        public double PeakC { get; private set; } = AmbientC;

        /// <summary>True once the winding has burnt open (F18).</summary>
        public bool Burnt { get; private set; }

        /// <summary>Advances the temperature by <paramref name="seconds"/> with a steady current.</summary>
        public void Update(double currentAmps, double resistanceOhm, double seconds)
        {
            if (seconds <= 0) return;
            double power = Burnt ? 0 : currentAmps * currentAmps * resistanceOhm;
            double steady = AmbientC + power * ThermalResistanceCPerW;
            // Exact solution for a constant current over the step, so any step size is stable.
            TemperatureC = steady + (TemperatureC - steady) * Math.Exp(-seconds / TimeConstantSeconds);
            PeakC = Math.Max(PeakC, TemperatureC);
            if (TemperatureC > FailureC) Burnt = true;
        }

        /// <summary>A new motor, as when the player presses Replace.</summary>
        public void Replace()
        {
            TemperatureC = AmbientC;
            PeakC = AmbientC;
            Burnt = false;
        }

        /// <summary>Restores a saved state.</summary>
        public void Restore(double temperatureC, double peakC, bool burnt)
        {
            TemperatureC = temperatureC;
            PeakC = Math.Max(peakC, temperatureC);
            Burnt = burnt;
        }
    }
}
