using System;

namespace CoreEngine.Sim.Components
{
    /// <summary>
    /// Alkaline AA cells in series (docs/06-electrical-simulation-spec.md §4.1): open-circuit voltage and
    /// internal resistance as functions of the state of charge, and a Peukert-style capacity that falls
    /// with the drain current (≈ 2.8 Ah at 25 mA, ≈ 1.2 Ah at 500 mA). Empty is failure F19 (§6).
    /// Phase 0 version for the Garage prototype; the curves are [VERIFY] as in the spec.
    /// </summary>
    public sealed class BatteryPack
    {
        // Per-cell open-circuit voltage against state of charge: 1.60 V full, ≈ 1.25 V plateau, 0.90 V empty.
        static readonly double[] SocPoints = { 0.00, 0.05, 0.20, 0.50, 0.80, 0.95, 1.00 };
        static readonly double[] CellVolts = { 0.90, 1.05, 1.15, 1.25, 1.38, 1.50, 1.60 };

        const double ReferenceAmps = 0.025;
        const double ReferenceAh = 2.8;
        // Peukert-style exponent through (25 mA, 2.8 Ah) and (500 mA, 1.2 Ah): ln(2.8/1.2) / ln(20).
        static readonly double RateExponent = Math.Log(2.8 / 1.2) / Math.Log(0.5 / ReferenceAmps);

        public BatteryPack(int cells) => Cells = cells;

        public int Cells { get; }

        /// <summary>0 = empty, 1 = fresh.</summary>
        public double StateOfCharge { get; private set; } = 1.0;

        public bool IsEmpty => StateOfCharge <= 0;

        public double OpenCircuitVolts => Cells * CellOpenCircuit(StateOfCharge);

        /// <summary>Internal resistance of the whole pack: 0.15 Ω per fresh cell, rising steeply to 0.6 Ω when empty.</summary>
        public double InternalOhms => Cells * (0.15 + 0.45 * Math.Pow(1 - StateOfCharge, 4));

        /// <summary>Voltage at the pack's terminals while <paramref name="currentAmps"/> flows out of it.</summary>
        public double TerminalVolts(double currentAmps) =>
            IsEmpty ? 0 : Math.Max(0, OpenCircuitVolts - Math.Max(0, currentAmps) * InternalOhms);

        /// <summary>Usable capacity at a steady drain current: less charge comes out at higher currents.</summary>
        public static double CapacityAh(double currentAmps) =>
            currentAmps <= ReferenceAmps ? ReferenceAh : ReferenceAh * Math.Pow(ReferenceAmps / currentAmps, RateExponent);

        /// <summary>Removes the charge for <paramref name="currentAmps"/> flowing for <paramref name="seconds"/>.</summary>
        public void Drain(double currentAmps, double seconds)
        {
            if (currentAmps <= 0 || seconds <= 0 || IsEmpty) return;
            StateOfCharge = Math.Max(0, StateOfCharge - currentAmps * seconds / (3600 * CapacityAh(currentAmps)));
        }

        /// <summary>Fresh cells, as when the player presses Replace batteries.</summary>
        public void Replace() => StateOfCharge = 1.0;

        /// <summary>Sets the charge, for example when a saved robot is loaded.</summary>
        public void Restore(double stateOfCharge) => StateOfCharge = Math.Max(0, Math.Min(1, stateOfCharge));

        static double CellOpenCircuit(double soc)
        {
            if (soc <= SocPoints[0]) return CellVolts[0];
            for (int i = 1; i < SocPoints.Length; i++)
            {
                if (soc <= SocPoints[i])
                {
                    double t = (soc - SocPoints[i - 1]) / (SocPoints[i] - SocPoints[i - 1]);
                    return CellVolts[i - 1] + t * (CellVolts[i] - CellVolts[i - 1]);
                }
            }
            return CellVolts[CellVolts.Length - 1];
        }
    }
}
