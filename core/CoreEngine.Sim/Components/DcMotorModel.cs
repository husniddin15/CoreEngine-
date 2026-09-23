using System;

namespace CoreEngine.Sim.Components
{
    /// <summary>
    /// Brushed DC gear motor (docs/07-physics-world-sensors-spec.md §4.1), steady-state electrical
    /// model: i = (V - Ke·ω_m) / R, T_m = Kt·i - T_friction, T_out = η·N·T_m, with Kt = Ke in SI units.
    /// Inductance is left out because τ = L/R ≈ 0.25 ms is far shorter than the 10 ms physics step.
    /// Phase 0 spike version; Phase 1 integrates the full model at 1 kHz with the electrical solver.
    /// </summary>
    public sealed class DcMotorModel
    {
        public DcMotorModel(double resistanceOhm, double keVoltSecondsPerRad, double gearRatio,
            double gearEfficiency, double frictionTorqueNm, double rotorInertiaKgM2)
        {
            ResistanceOhm = resistanceOhm;
            Ke = keVoltSecondsPerRad;
            GearRatio = gearRatio;
            GearEfficiency = gearEfficiency;
            FrictionTorqueNm = frictionTorqueNm;
            RotorInertiaKgM2 = rotorInertiaKgM2;
        }

        /// <summary>
        /// TT gear motor 1:48 ("yellow motor") with the datasheet-fitted parameters of docs/07 §4.1:
        /// R 4.0 Ω, Ke 4.26 mV·s/rad, η 0.26, friction from the 0.16 A no-load current.
        /// </summary>
        public static DcMotorModel TtGearMotor148() => new DcMotorModel(4.0, 4.26e-3, 48, 0.26, 4.26e-3 * 0.16, 2e-7);

        public double ResistanceOhm { get; }
        public double Ke { get; }
        public double GearRatio { get; }
        public double GearEfficiency { get; }
        public double FrictionTorqueNm { get; }
        public double RotorInertiaKgM2 { get; }

        /// <summary>Rotor inertia seen at the output shaft (N² · J_rotor).</summary>
        public double ReflectedInertiaKgM2 => RotorInertiaKgM2 * GearRatio * GearRatio;

        /// <summary>
        /// Output-shaft torque in N·m for a terminal voltage (NaN = open circuit, the motor coasts)
        /// and the output-shaft speed in rad/s. <paramref name="currentAmps"/> is the winding current.
        /// </summary>
        public double OutputTorque(double terminalVolts, double outputRadPerSec, out double currentAmps)
        {
            double motorSpeed = outputRadPerSec * GearRatio;
            double current = double.IsNaN(terminalVolts) ? 0 : (terminalVolts - Ke * motorSpeed) / ResistanceOhm;
            double torque = Ke * current;

            // Coulomb friction opposes motion; at standstill it holds up to its own value.
            if (Math.Abs(motorSpeed) > 1e-3) torque -= FrictionTorqueNm * Math.Sign(motorSpeed);
            else if (Math.Abs(torque) <= FrictionTorqueNm) torque = 0;
            else torque -= FrictionTorqueNm * Math.Sign(torque);

            currentAmps = current;
            return torque * GearRatio * GearEfficiency;
        }
    }
}
