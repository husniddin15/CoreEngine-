using System;

namespace CoreEngine.Sim.Components
{
    /// <summary>
    /// L298N dual H-bridge module, logic level only (docs/06-electrical-simulation-spec.md §5.11):
    /// truth table and a fixed bridge drop. Phase 0 spike version; the Phase 1 model adds the
    /// current-dependent drop, heating, the 78M05 regulator and PWM on ENA/ENB.
    /// </summary>
    public sealed class L298NModel
    {
        /// <summary>Motor supply on the +12V terminal, in volts.</summary>
        public double SupplyVolts { get; set; } = 6.0;

        /// <summary>Total source + sink saturation drop; 1.8 V is the ST datasheet typical at 1 A.</summary>
        public double BridgeDropVolts { get; set; } = 1.8;

        /// <summary>
        /// Voltage across one channel's motor terminals. ENA low leaves the outputs open (NaN, the motor
        /// coasts); equal inputs brake (0 V across a shorted motor); otherwise ±(supply - drop).
        /// </summary>
        public double ChannelVolts(bool enable, bool in1, bool in2)
        {
            if (!enable) return double.NaN;
            if (in1 == in2) return 0.0;
            double volts = Math.Max(0.0, SupplyVolts - BridgeDropVolts);
            return in1 ? volts : -volts;
        }

        /// <summary>
        /// The average voltage across a channel whose inputs switch (PWM), from the share of the time each is
        /// high: driving forward while enable and IN1 are high and IN2 low, backward the other way round, and 0 V
        /// otherwise (brake, or coasting while enable is low). That is (supply − drop) · enable · (IN1 − IN2): with
        /// ENA at analogWrite 180 and IN1 high, IN2 low, 180/255 of the full voltage, as a motor on a real L298N
        /// averages it. NaN when enable is never high (the motor coasts).
        /// </summary>
        public double AverageChannelVolts(double enable, double in1, double in2)
        {
            if (enable <= 0) return double.NaN;
            return Math.Max(0.0, SupplyVolts - BridgeDropVolts) * enable * (in1 - in2);
        }
    }
}
