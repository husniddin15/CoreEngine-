using CoreEngine.Sim.Avr;

namespace CoreEngine.Sim.Tests.Avr;

public class TimerTests
{
    static TestMachine IdleMachine() => new(Asm.Rjmp(-1)); // the CPU spins while the timer runs

    [Fact]
    public void NormalModeCountsWithThePrescaler()
    {
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR0B, 3); // clk/64
        m.Mcu.RunCycles(64 * 100);
        Assert.Equal(100, m.Cpu.ReadData(Atmega328P.TCNT0));
        Assert.Equal(0, m.Cpu.ReadData(Atmega328P.TIFR0) & AvrTimer8.FlagOverflow);
        m.Mcu.RunCycles(64 * 156);
        Assert.Equal(0, m.Cpu.ReadData(Atmega328P.TCNT0));
        Assert.Equal(AvrTimer8.FlagOverflow, m.Cpu.ReadData(Atmega328P.TIFR0) & AvrTimer8.FlagOverflow);
    }

    [Fact]
    public void OverflowInterruptFiresEvery256TicksAtPrescalerOne()
    {
        var program = new ushort[34];
        program[0] = Asm.Sei;
        program[1] = Asm.Rjmp(-1);
        program[32] = Asm.Inc(24);
        program[33] = Asm.Reti;
        var m = new TestMachine(program);
        m.Cpu.WriteData(Atmega328P.TIMSK0, 1);
        m.Cpu.WriteData(Atmega328P.TCCR0B, 1);
        m.Mcu.RunCycles(256 * 10 + 100);
        Assert.Equal(10, m.R(24));
        Assert.Equal(10, m.Cpu.InterruptsServiced);
    }

    [Fact]
    public void CtcModeSetsCompareFlagEveryOcrPlusOneTicks()
    {
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR0A, 0x02); // WGM01: CTC
        m.Cpu.WriteData(Atmega328P.OCR0A, 99);
        m.Cpu.WriteData(Atmega328P.TCCR0B, 2);    // clk/8: one match every 800 cycles
        m.Mcu.RunCycles(750);
        Assert.Equal(0, m.Cpu.ReadData(Atmega328P.TIFR0) & AvrTimer8.FlagCompareA);
        m.Mcu.RunCycles(100);
        const int aAndOverflow = AvrTimer8.FlagCompareA | AvrTimer8.FlagOverflow;
        Assert.Equal(AvrTimer8.FlagCompareA, m.Cpu.ReadData(Atmega328P.TIFR0) & aAndOverflow); // no TOV in CTC
        // OCR0B is 0, so compare match B fires each time the counter clears to 0, as on the chip.
        Assert.Equal(AvrTimer8.FlagCompareB, m.Cpu.ReadData(Atmega328P.TIFR0) & AvrTimer8.FlagCompareB);

        m.Cpu.WriteData(Atmega328P.TIFR0, AvrTimer8.FlagCompareA); // writing one clears only that flag
        Assert.Equal(AvrTimer8.FlagCompareB, m.Cpu.ReadData(Atmega328P.TIFR0) & 0x07);
        m.Mcu.RunCycles(800);
        Assert.Equal(AvrTimer8.FlagCompareA, m.Cpu.ReadData(Atmega328P.TIFR0) & aAndOverflow);
    }

    [Fact]
    public void PhaseCorrectModeCountsUpAndDown()
    {
        var m = IdleMachine();
        m.Cpu.WriteData(Atmega328P.TCCR0A, 0x01); // WGM00: phase correct, TOP = 0xFF
        long start = m.Cpu.Cycles;
        m.Cpu.WriteData(Atmega328P.TCCR0B, 1);
        m.Mcu.RunCycles(300 - (m.Cpu.Cycles - start));
        Assert.Equal(510 - 300, m.Cpu.ReadData(Atmega328P.TCNT0)); // counting down after TOP
        Assert.Equal(0, m.Cpu.ReadData(Atmega328P.TIFR0) & AvrTimer8.FlagOverflow);
        m.Mcu.RunCycles(220);
        Assert.Equal(AvrTimer8.FlagOverflow, m.Cpu.ReadData(Atmega328P.TIFR0) & AvrTimer8.FlagOverflow); // TOV at BOTTOM
    }
}

public class UsartTests
{
    [Fact]
    public void FramesTakeTenBitTimesAndTheSecondByteWaitsInTheBuffer()
    {
        var m = new TestMachine(Asm.Rjmp(-1));
        var cpu = m.Cpu;
        cpu.WriteData(Atmega328P.UBRR0L, 207);
        cpu.WriteData(Atmega328P.UCSR0A, 0x02);  // U2X: 16 MHz / (8 * 208) = 9615 baud
        cpu.WriteData(Atmega328P.UCSR0B, 0x08);  // TXEN
        var sent = new List<(byte value, long end)>();
        m.Mcu.Usart0.ByteTransmitted += (value, _, end) => sent.Add((value, end));

        long start = cpu.Cycles;
        cpu.WriteData(Atmega328P.UDR0, (byte)'A');
        Assert.Equal(0x20, cpu.ReadData(Atmega328P.UCSR0A) & 0x20); // shifter took it: UDRE stays set
        cpu.WriteData(Atmega328P.UDR0, (byte)'B');
        Assert.Equal(0x00, cpu.ReadData(Atmega328P.UCSR0A) & 0x20); // buffer full: UDRE clear

        m.Mcu.RunCycles(40_000);
        const long frame = 10 * 8 * 208;
        Assert.Equal(2, sent.Count);
        Assert.Equal(((byte)'A', start + frame), sent[0]);
        Assert.Equal(((byte)'B', start + 2 * frame), sent[1]);
        Assert.Equal(0x60, cpu.ReadData(Atmega328P.UCSR0A) & 0x60); // TXC and UDRE set when idle
    }

    [Fact]
    public void ReceivedBytesSetRxcUntilRead()
    {
        var m = new TestMachine(Asm.Rjmp(-1));
        var cpu = m.Cpu;
        cpu.WriteData(Atmega328P.UBRR0L, 16);
        cpu.WriteData(Atmega328P.UCSR0A, 0x02);
        cpu.WriteData(Atmega328P.UCSR0B, 0x10);  // RXEN
        m.Mcu.Usart0.Receive((byte)'x');
        Assert.Equal(0, cpu.ReadData(Atmega328P.UCSR0A) & 0x80);
        m.Mcu.RunCycles(10 * 8 * 17 + 10);
        Assert.Equal(0x80, cpu.ReadData(Atmega328P.UCSR0A) & 0x80);
        Assert.Equal((byte)'x', cpu.ReadData(Atmega328P.UDR0));
        Assert.Equal(0, cpu.ReadData(Atmega328P.UCSR0A) & 0x80);
    }
}

public class PortTests
{
    [Fact]
    public void DriveChangesAreReportedWithTheirCycle()
    {
        var m = new TestMachine(Asm.Rjmp(-1));
        var events = new List<(int bit, PinDrive drive)>();
        m.Mcu.PortB.PinDriveChanged += (_, bit, drive, _) => events.Add((bit, drive));

        m.Cpu.WriteData(0x24, 0x20); // DDRB5
        m.Cpu.WriteData(0x25, 0x21); // PORTB5 high, pull-up on PB0 (one write, reported in bit order)
        Assert.Equal(new[] { (5, PinDrive.Low), (0, PinDrive.PullUp), (5, PinDrive.High) }, events);
    }

    [Fact]
    public void InputsReadPullUpsAndExternalLevels()
    {
        var m = new TestMachine(Asm.Rjmp(-1));
        m.Cpu.WriteData(0x25, 0x01);                 // pull-up on PB0
        Assert.Equal(0x01, m.Cpu.ReadData(0x23) & 0x01);
        m.Mcu.PortB.SetInputLevel(0, false);         // a button pulls it low
        Assert.Equal(0x00, m.Cpu.ReadData(0x23) & 0x01);
        m.Mcu.PortB.SetInputLevel(1, true);
        Assert.Equal(0x02, m.Cpu.ReadData(0x23) & 0x02);
    }
}
