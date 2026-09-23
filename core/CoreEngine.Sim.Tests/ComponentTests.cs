using System.Text;
using CoreEngine.Sim.Avr;
using CoreEngine.Sim.Components;

namespace CoreEngine.Sim.Tests;

public class DcMotorModelTests
{
    readonly DcMotorModel tt = DcMotorModel.TtGearMotor148();

    [Fact]
    public void StallTorqueMatchesTheDatasheet()
    {
        double torque = tt.OutputTorque(6.0, 0.0, out double current);
        Assert.Equal(1.5, current, 3);                  // 6 V / 4 Ω, as measured by Adafruit
        Assert.InRange(torque / 0.0980665, 0.70, 0.85); // datasheet: 0.8 kg·cm at 6 V
    }

    [Fact]
    public void NoLoadSpeedIsAbout250RpmAt6Volts()
    {
        double at250Rpm = 250 * 2 * Math.PI / 60;
        Assert.True(tt.OutputTorque(6.0, at250Rpm * 0.97, out _) > 0);
        Assert.True(tt.OutputTorque(6.0, at250Rpm * 1.03, out _) < 0);
    }

    [Fact]
    public void OpenCircuitOnlyLeavesFriction()
    {
        double torque = tt.OutputTorque(double.NaN, 10.0, out double current);
        Assert.Equal(0, current);
        Assert.True(torque < 0);                       // friction opposes the motion
        Assert.Equal(0, tt.OutputTorque(double.NaN, 0.0, out _));
    }

    [Fact]
    public void ShortedMotorBrakes()
    {
        double torque = tt.OutputTorque(0.0, 20.0, out double current);
        Assert.True(current < 0);                      // back-EMF drives current the other way
        Assert.True(torque < 0);
    }
}

public class L298NModelTests
{
    [Theory]
    [InlineData(true, true, false, 4.2)]
    [InlineData(true, false, true, -4.2)]
    [InlineData(true, false, false, 0.0)]    // brake
    [InlineData(true, true, true, 0.0)]      // brake
    public void TruthTableWithTheTypicalDrop(bool enable, bool in1, bool in2, double volts)
    {
        var bridge = new L298NModel { SupplyVolts = 6.0 };
        Assert.Equal(volts, bridge.ChannelVolts(enable, in1, in2), 6);
    }

    [Fact]
    public void DisabledChannelCoasts()
    {
        Assert.True(double.IsNaN(new L298NModel().ChannelVolts(false, true, false)));
    }
}

public class HcSr04Tests
{
    [Fact]
    public void EchoTimeIs58MicrosecondsPerCentimetreAt20Celsius()
    {
        var mcu = new Atmega328P();
        var sensor = new HcSr04(mcu.Cpu, Atmega328P.ClockHz, mcu.PortB, 1, mcu.PortB, 2, () => 100);
        Assert.Equal(5823.8, sensor.EchoMicros(100), 1);
        Assert.Equal(HcSr04.TimeoutMicros, sensor.EchoMicros(double.NaN));
        Assert.Equal(HcSr04.TimeoutMicros, sensor.EchoMicros(450));
    }
}

public class ObstacleAvoiderGoldenTests
{
    static (Atmega328P mcu, List<string> lines) Start(Func<double> distanceCm, out HcSr04 sensor)
    {
        var mcu = new Atmega328P();
        mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "Hex", "ObstacleAvoider.hex")));
        mcu.Cpu.Diagnostic += message => throw new Xunit.Sdk.XunitException("Emulator diagnostic: " + message);
        sensor = new HcSr04(mcu.Cpu, Atmega328P.ClockHz, mcu.PortB, 1, mcu.PortB, 2, distanceCm); // TRIG = D9, ECHO = D10
        var lines = new List<string>();
        var current = new StringBuilder();
        mcu.Usart0.ByteTransmitted += (value, _, _) =>
        {
            if (value == '\n') { lines.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append((char)value);
        };
        return (mcu, lines);
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(57.3)]
    [InlineData(12.0)]
    [InlineData(250.0)]
    public void PulseInMeasuresTheEchoLikeOnAnUno(double cm)
    {
        var (mcu, lines) = Start(() => cm, out var sensor);
        mcu.RunSeconds(1.5);

        // The sketch prints duration / 58, with duration measured by pulseIn's cycle-counting loop.
        // pulseIn leaves interrupts on, so the Timer0 overflow ISR (about 100 cycles every 1.024 ms)
        // steals time from the loop and a real Uno reads about 0.6 % short: allow up to 1.5 %.
        double ideal = sensor.EchoMicros(cm) / 58;
        Assert.NotEmpty(lines);
        foreach (string line in lines) Assert.InRange(long.Parse(line), (long)(ideal * 0.985) - 1, (long)ideal + 1);
    }

    [Fact]
    public void NothingInRangePrints999()
    {
        var (mcu, lines) = Start(() => double.NaN, out _);
        mcu.RunSeconds(0.5);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal("999", line));
    }

    [Fact]
    public void FarObstacleDrivesForward()
    {
        var (mcu, _) = Start(() => 100, out _);
        mcu.RunSeconds(0.1);
        AssertMotors(mcu, in1: true, in2: false, in3: true, in4: false);
    }

    [Fact]
    public void NearObstacleBacksOffThenTurns()
    {
        var (mcu, _) = Start(() => 12, out _);
        mcu.RunSeconds(0.1);
        AssertMotors(mcu, in1: false, in2: true, in3: false, in4: true);   // backing off
        mcu.RunSeconds(0.3);
        AssertMotors(mcu, in1: true, in2: false, in3: false, in4: true);   // turning right in place
    }

    [Fact]
    public void UnchangedDistanceWhileDrivingMeansStuck()
    {
        // Pushing against a box beside the narrow beam: the sensor keeps reading the wall behind it.
        // Each reading takes about 63.5 ms, so the 16th reading (about 0.96 s) starts the escape.
        var (mcu, _) = Start(() => 51, out _);
        mcu.RunSeconds(0.8);
        AssertMotors(mcu, in1: true, in2: false, in3: true, in4: false);   // still driving forward
        mcu.RunSeconds(0.3);
        AssertMotors(mcu, in1: false, in2: true, in3: false, in4: true);   // backing off
        mcu.RunSeconds(0.4);
        AssertMotors(mcu, in1: false, in2: true, in3: true, in4: false);   // turning left in place
    }

    [Fact]
    public void ApproachingAnObstacleIsNotMistakenForStuck()
    {
        Atmega328P? machine = null;
        var (mcu, _) = Start(() => 150 - 50 * machine!.Seconds, out _);   // closing in at 0.5 m/s
        machine = mcu;
        for (int i = 0; i < 18; i++)
        {
            mcu.RunSeconds(0.1);
            AssertMotors(mcu, in1: true, in2: false, in3: true, in4: false);
        }
    }

    static void AssertMotors(Atmega328P mcu, bool in1, bool in2, bool in3, bool in4)
    {
        Assert.Equal(in1 ? PinDrive.High : PinDrive.Low, mcu.PortD.GetDrive(5)); // D5
        Assert.Equal(in2 ? PinDrive.High : PinDrive.Low, mcu.PortD.GetDrive(6)); // D6
        Assert.Equal(in3 ? PinDrive.High : PinDrive.Low, mcu.PortD.GetDrive(7)); // D7
        Assert.Equal(in4 ? PinDrive.High : PinDrive.Low, mcu.PortB.GetDrive(0)); // D8
    }
}
