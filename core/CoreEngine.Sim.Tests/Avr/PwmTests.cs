using CoreEngine.Sim.Avr;
using CoreEngine.Sim.Components;
using CoreEngine.Sim.Design;

namespace CoreEngine.Sim.Tests.Avr;

/// <summary>
/// The timers' output-compare pins (docs/05 §4): what analogWrite, tone and servo pulses put on the Uno's
/// PWM pins. Registers are set as the Arduino core sets them; the pins are measured from their edges.
/// </summary>
public class PwmTests
{
    static TestMachine IdleMachine() => new(Asm.Rjmp(-1));

    /// <summary>
    /// Lets the timer settle (as on the chip, a PWM pin starts its pattern at the first BOTTOM), then runs the
    /// machine and returns a pin's duty over the run and its rising edges.
    /// </summary>
    static (double duty, int rises) Measure(TestMachine m, string pin, long cycles, long settle = 64_000)
    {
        m.Mcu.RunCycles(settle);
        var meter = new PinDuty(m.Mcu);
        var io = UnoPins.PortOf(pin)!.Value;
        var port = io.port == 'B' ? m.Mcu.PortB : io.port == 'C' ? m.Mcu.PortC : m.Mcu.PortD;
        int rises = 0;
        port.PinDriveChanged += (_, bit, drive, _) => { if (bit == io.bit && drive == PinDrive.High) rises++; };
        long start = m.Cpu.Cycles;
        meter.Begin(start);
        m.Mcu.RunCycles(cycles);
        return (meter.Duty(pin, m.Cpu.Cycles), rises);
    }

    [Fact]
    public void FastPwmOnTimer0DrivesD5AsAnalogWriteDoes()
    {
        // init(): fast PWM, clk/64; analogWrite(5, 180): DDR, COM0B1, OCR0B.
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR0A, 0x03);
        m.Cpu.WriteData(Atmega328P.TCCR0B, 0x03);
        m.Cpu.WriteData(Atmega328P.PIND + 1, 1 << 5);
        m.Cpu.WriteData(Atmega328P.TCCR0A, 0x03 | 0x20);
        m.Cpu.WriteData(Atmega328P.OCR0B, 180);
        var (duty, rises) = Measure(m, "D5", 10 * 64 * 256); // ten periods: 16 MHz / 64 / 256 = 976.6 Hz
        Assert.InRange(duty, 180 / 256.0 - 0.005, 180 / 256.0 + 0.005);
        Assert.Equal(10, rises);
    }

    [Fact]
    public void PhaseCorrectPwmOnTimer2DrivesD3()
    {
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR2A, 0x01 | 0x20); // phase correct, COM2B1
        m.Cpu.WriteData(Atmega328P.TCCR2B, 0x04);        // clk/64
        m.Cpu.WriteData(Atmega328P.PIND + 1, 1 << 3);
        m.Cpu.WriteData(Atmega328P.OCR2B, 64);
        var (duty, rises) = Measure(m, "D3", 10 * 64 * 510); // ten periods: 16 MHz / 64 / 510 = 490 Hz
        Assert.InRange(duty, 64 / 255.0 - 0.005, 64 / 255.0 + 0.005);
        Assert.Equal(10, rises);
    }

    [Fact]
    public void Timer1PwmDrivesD10AsAnalogWriteDoes()
    {
        // init(): phase-correct 8-bit PWM (WGM10), clk/64; analogWrite(10, 180): COM1B1, OCR1B high then low byte.
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR1B, 0x03);
        m.Cpu.WriteData(Atmega328P.TCCR1A, 0x01);
        m.Cpu.WriteData(Atmega328P.PINB + 1, 1 << 2);
        m.Cpu.WriteData(Atmega328P.TCCR1A, 0x01 | 0x20);
        m.Cpu.WriteData(Atmega328P.OCR1BH, 0);
        m.Cpu.WriteData(Atmega328P.OCR1BL, 180);
        var (duty, rises) = Measure(m, "D10", 10 * 64 * 510);
        Assert.InRange(duty, 180 / 255.0 - 0.005, 180 / 255.0 + 0.005);
        Assert.Equal(10, rises);
    }

    [Fact]
    public void Timer1MakesServoPulsesWithIcr1AsTop()
    {
        // Fast PWM with ICR1 as TOP (mode 14), clk/8: a 20 ms frame, and OCR1A 3000 gives a 1.5 ms pulse on D9.
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.PINB + 1, 1 << 1);
        m.Cpu.WriteData(Atmega328P.ICR1H, 39999 >> 8);
        m.Cpu.WriteData(Atmega328P.ICR1L, 39999 & 0xFF);
        m.Cpu.WriteData(Atmega328P.OCR1AH, 3000 >> 8);
        m.Cpu.WriteData(Atmega328P.OCR1AL, 3000 & 0xFF);
        m.Cpu.WriteData(Atmega328P.TCCR1A, 0x80 | 0x02);  // COM1A1, WGM11
        m.Cpu.WriteData(Atmega328P.TCCR1B, 0x18 | 0x02);  // WGM13, WGM12, clk/8
        var (duty, rises) = Measure(m, "D9", 1_600_000, settle: 400_000); // 100 ms: five frames
        Assert.InRange(duty, 0.075 - 0.002, 0.075 + 0.002);
        Assert.Equal(5, rises);
    }

    [Fact]
    public void CtcToggleMakesASquareWave()
    {
        // tone()-style: CTC with OCR1A as TOP, COM1A0 toggles D9 at every match: 8000 cycles per half wave, 1 kHz.
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.PINB + 1, 1 << 1);
        m.Cpu.WriteData(Atmega328P.OCR1AH, 999 >> 8);
        m.Cpu.WriteData(Atmega328P.OCR1AL, 999 & 0xFF);
        m.Cpu.WriteData(Atmega328P.TCCR1A, 0x40);        // COM1A0: toggle
        m.Cpu.WriteData(Atmega328P.TCCR1B, 0x08 | 0x02); // WGM12 (CTC), clk/8
        var (duty, rises) = Measure(m, "D9", 160_000);   // ten periods of 1 ms
        Assert.InRange(duty, 0.495, 0.505);
        Assert.Equal(10, rises);
    }

    [Fact]
    public void ExtremeValuesHoldThePinAndDisconnectingGivesItBack()
    {
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR0A, 0x03 | 0x20);
        m.Cpu.WriteData(Atmega328P.TCCR0B, 0x03);
        m.Cpu.WriteData(Atmega328P.PIND + 1, 1 << 5);
        m.Cpu.WriteData(Atmega328P.OCR0B, 255);
        m.Mcu.RunCycles(20_000);
        Assert.Equal(1.0, Measure(m, "D5", 80_000).duty, 3);  // OCR at TOP: high all the time
        m.Cpu.WriteData(Atmega328P.OCR0B, 0);
        m.Mcu.RunCycles(20_000);
        Assert.Equal(0.0, Measure(m, "D5", 80_000).duty, 3);  // 0: low
        // digitalWrite(5, HIGH) turns the PWM off (COM0B1 cleared) and the PORT bit drives the pin.
        m.Cpu.WriteData(Atmega328P.TCCR0A, 0x03);
        m.Cpu.WriteData(Atmega328P.PIND + 2, 1 << 5);
        Assert.Equal(PinDrive.High, m.Mcu.PortD.GetDrive(5));
        Assert.Equal(1.0, Measure(m, "D5", 80_000).duty, 3);
    }

    [Fact]
    public void Timer1ReadsAndWritesSixteenBitsThroughTemp()
    {
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCNT1H, 0x12);
        m.Cpu.WriteData(Atmega328P.TCNT1L, 0x34); // the high byte waits in TEMP until the low byte is written
        Assert.Equal(0x1234, m.Mcu.Timer1.CurrentCount);
        m.Cpu.WriteData(Atmega328P.TCCR1B, 0x01); // clk/1
        m.Mcu.RunCycles(0x100);
        int low = m.Cpu.ReadData(Atmega328P.TCNT1L); // latches the high byte
        int high = m.Cpu.ReadData(Atmega328P.TCNT1H);
        int value = (high << 8) | low;
        Assert.InRange(value, 0x1234 + 0x100, 0x1234 + 0x110);
    }

    [Fact]
    public void Timer1CompareInterruptFiresAsServoHExpects()
    {
        // Normal mode, clk/8, OCR1A = 500 with OCIE1A: one interrupt per 65 536 ticks (the counter wraps).
        var program = new ushort[48];
        program[0] = Asm.Sei;
        program[1] = Asm.Rjmp(-1);
        program[22] = Asm.Inc(24); // vector 11 (TIMER1_COMPA) at word 22
        program[23] = Asm.Reti;
        var m = new TestMachine(program);
        m.Cpu.WriteData(Atmega328P.OCR1AH, 500 >> 8);
        m.Cpu.WriteData(Atmega328P.OCR1AL, 500 & 0xFF);
        m.Cpu.WriteData(Atmega328P.TIMSK1, 0x02);
        m.Cpu.WriteData(Atmega328P.TCCR1B, 0x02);
        m.Mcu.RunCycles(8L * 65536 * 3 + 8 * 600);
        Assert.Equal(4, m.R(24)); // at ticks 500, 66 036, 131 572 and 197 108
    }

    [Fact]
    public void TheOwnersSketchDrivesBothEnablePinsAt180()
    {
        // analogWrite(ENA = 5, 180) runs on Timer0 and analogWrite(ENB = 10, 180) on Timer1.
        var mcu = new Atmega328P();
        mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "Hex", "PwmMotors.hex")));
        mcu.Cpu.Diagnostic += message => throw new Xunit.Sdk.XunitException("Emulator diagnostic: " + message);
        mcu.RunSeconds(0.1);
        var meter = new PinDuty(mcu);
        meter.Begin(mcu.Cpu.Cycles);
        mcu.RunSeconds(0.05);
        long end = mcu.Cpu.Cycles;
        Assert.InRange(meter.Duty("D5", end), 180 / 256.0 - 0.02, 180 / 256.0 + 0.02);
        Assert.InRange(meter.Duty("D10", end), 180 / 255.0 - 0.02, 180 / 255.0 + 0.02);
        Assert.Equal(1.0, meter.Duty("D6", end), 3); // IN1 high
        Assert.Equal(0.0, meter.Duty("D7", end), 3); // IN2 low
    }

    /// <summary>
    /// The owner's first own robot (2026-09-25), as saved: the preset with the L298N's +5V on the Uno's VIN.
    /// </summary>
    static RobotDesign OwnersFirstRobot()
    {
        var d = DesignPresets.PwmTwoWheeler();
        d.Wires.RemoveAll(w => w.ToPart == "uno1" && w.ToPin == "5V");
        d.AddWire("driver1", "+5V", "uno1", "VIN", "red");
        return d;
    }

    [Fact]
    public void TheOwnersFirstRobotRunsOnceItsUnoIsOnFiveVolts()
    {
        var d = OwnersFirstRobot();
        var before = CircuitAnalysis.Analyse(d);
        Assert.False(before.BoardPowered);
        Assert.Contains(before.Warnings, w => w.Code == "fiveVoltOnVin");

        // The fix: the red wire from VIN to the Uno's 5V pin.
        d.Wires.RemoveAll(w => w.ToPart == "uno1" && w.ToPin == "VIN");
        d.AddWire("driver1", "+5V", "uno1", "5V", "red");
        var circuit = CircuitAnalysis.Analyse(d);
        Assert.True(circuit.BoardPowered, string.Join(", ", circuit.Warnings));
        Assert.False(circuit.HasProblems, string.Join(", ", circuit.Warnings));

        // Its sketch, as compiled for it (the same 1010 bytes): both motors get 180/255 of the bridge's voltage.
        var mcu = new Atmega328P();
        mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "Hex", "PwmMotors.hex")));
        Assert.Equal(1010, mcu.ProgramSize);
        mcu.RunSeconds(0.1);
        var meter = new PinDuty(mcu);
        meter.Begin(mcu.Cpu.Cycles);
        mcu.RunSeconds(0.05);
        long end = mcu.Cpu.Cycles;
        var bridge = new L298NModel { SupplyVolts = 6.0 };
        double full = 6.0 - bridge.BridgeDropVolts;
        double left = DriveMap.MotorVolts(circuit, "motor1", pin => meter.Duty(pin, end), bridge);
        double right = DriveMap.MotorVolts(circuit, "motor2", pin => meter.Duty(pin, end), bridge);
        Assert.InRange(Math.Abs(left), 0.68 * full, 0.73 * full);
        Assert.InRange(Math.Abs(right), 0.68 * full, 0.73 * full);

        // Both wheels turn the same way, so it drives straight: toward -z, wheels first, because each motor's
        // leads are on the L298N the other way round from the kit's (IN1 and IN3 high are the kit's forward).
        int leftDrive = Math.Sign(left) * DesignGeometry.ForwardSign(d.Find("motor1")!);
        int rightDrive = Math.Sign(right) * DesignGeometry.ForwardSign(d.Find("motor2")!);
        Assert.Equal(-1, leftDrive);
        Assert.Equal(-1, rightDrive);
    }

    [Fact]
    public void TheOwnersTurnRightDrivesItsWheelsOppositeWays()
    {
        // The owner's third robot (2026-09-25) running its turnRight(), held (SpinInPlace.ino).
        var d = DesignPresets.NoCasterTwoWheeler();
        var circuit = CircuitAnalysis.Analyse(d);
        Assert.True(circuit.BoardPowered, string.Join(", ", circuit.Warnings));
        Assert.False(circuit.HasProblems, string.Join(", ", circuit.Warnings));

        var mcu = new Atmega328P();
        mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "Hex", "SpinInPlace.hex")));
        mcu.RunSeconds(0.1);
        var meter = new PinDuty(mcu);
        meter.Begin(mcu.Cpu.Cycles);
        mcu.RunSeconds(0.05);
        long end = mcu.Cpu.Cycles;
        var bridge = new L298NModel { SupplyVolts = 4.9 }; // four AA cells under the motors' load, as in the arena
        double full = 4.9 - bridge.BridgeDropVolts;
        double left = DriveMap.MotorVolts(circuit, "motor1", pin => meter.Duty(pin, end), bridge);
        double right = DriveMap.MotorVolts(circuit, "motor2", pin => meter.Duty(pin, end), bridge);
        Assert.InRange(Math.Abs(left), 0.96 * full, full); // analogWrite 250 of 255
        Assert.InRange(Math.Abs(right), 0.96 * full, full);

        // Its left wheel rolls forward and its right one back: it turns right on the spot, as its comments say.
        Assert.Equal("left", DesignGeometry.SideOf(d.Find("motor1")!));
        Assert.Equal(1, Math.Sign(left) * DesignGeometry.ForwardSign(d.Find("motor1")!));
        Assert.Equal(-1, Math.Sign(right) * DesignGeometry.ForwardSign(d.Find("motor2")!));
    }

    [Fact]
    public void AMotorOnAPwmEnableGetsTheAverageVoltage()
    {
        var design = DesignPresets.ObstacleAvoiderKit();
        design.AddWire("uno1", "D3", "driver1", "ENA", "white"); // ENA on a PWM pin: the jumper comes off
        var circuit = CircuitAnalysis.Analyse(design);
        var bridge = new L298NModel { SupplyVolts = 6.0 };
        double full = 6.0 - bridge.BridgeDropVolts;
        double Duty(string pin) => pin switch { "D3" => 0.5, "D5" => 1.0, _ => 0.0 }; // ENA half, IN1 (D5) high
        Assert.Equal(0.5 * full, DriveMap.MotorVolts(circuit, "left", Duty, bridge), 6);
        Assert.True(double.IsNaN(DriveMap.MotorVolts(circuit, "left", pin => pin == "D5" ? 1.0 : 0.0, bridge))); // enable never high: it coasts
        Assert.Equal(0.0, DriveMap.MotorVolts(circuit, "right", Duty, bridge), 6); // ENB on its jumper, IN3 and IN4 low: it brakes
    }
}
