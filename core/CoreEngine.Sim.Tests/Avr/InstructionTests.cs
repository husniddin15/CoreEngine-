using static CoreEngine.Sim.Tests.Avr.TestMachine;

namespace CoreEngine.Sim.Tests.Avr;

/// <summary>
/// Instruction results, status flags and cycle counts against the AVR Instruction Set Manual
/// (docs/05-arduino-emulation-spec.md §10, item 1). Expected SREG values are worked out by hand.
/// </summary>
public class ArithmeticTests
{
    [Theory]
    [InlineData(0x7F, 0x01, 0x80, FlagH | FlagV | FlagN)]            // signed overflow
    [InlineData(0xFF, 0x01, 0x00, FlagH | FlagZ | FlagC)]            // unsigned carry
    [InlineData(0x10, 0x20, 0x30, 0)]
    [InlineData(0x80, 0x80, 0x00, FlagV | FlagS | FlagZ | FlagC)]    // -128 + -128
    public void Add(int a, int b, int result, int sreg)
    {
        var m = new TestMachine(Asm.Ldi(16, a), Asm.Ldi(17, b), Asm.Add(16, 17));
        m.Steps(2);
        Assert.Equal(1, m.Step());
        Assert.Equal(result, m.R(16));
        Assert.Equal(sreg, m.Sreg);
    }

    [Fact]
    public void AdcAddsTheCarry()
    {
        var m = new TestMachine(Asm.Sec, Asm.Ldi(16, 0x0F), Asm.Ldi(17, 0x00), Asm.Adc(16, 17));
        m.Steps(4);
        Assert.Equal(0x10, m.R(16));
        Assert.Equal(FlagH, m.Sreg);
    }

    [Theory]
    [InlineData(0x00, 0x01, 0xFF, FlagH | FlagS | FlagN | FlagC)]    // borrow
    [InlineData(0x80, 0x01, 0x7F, FlagH | FlagS | FlagV)]            // signed overflow
    [InlineData(0x05, 0x05, 0x00, FlagZ)]
    [InlineData(0x10, 0x01, 0x0F, FlagH)]                            // half borrow only
    public void Sub(int a, int b, int result, int sreg)
    {
        var m = new TestMachine(Asm.Ldi(16, a), Asm.Ldi(17, b), Asm.Sub(16, 17));
        m.Steps(3);
        Assert.Equal(result, m.R(16));
        Assert.Equal(sreg, m.Sreg);
    }

    [Fact]
    public void CpcKeepsZeroFlagOnlyWhenTheLowBytesWereEqual()
    {
        // 0x0100 == 0x0100: CP/CPC leave Z set.
        var equal = new TestMachine(Asm.Ldi(16, 0x00), Asm.Ldi(17, 0x01), Asm.Ldi(18, 0x00), Asm.Ldi(19, 0x01),
            Asm.Cp(16, 18), Asm.Cpc(17, 19));
        equal.Steps(6);
        Assert.Equal(FlagZ, equal.Sreg);

        // 0x0001 vs 0x0000: the high bytes are equal, but Z must stay clear from the low-byte compare.
        var different = new TestMachine(Asm.Ldi(16, 0x01), Asm.Ldi(17, 0x00), Asm.Ldi(18, 0x00), Asm.Ldi(19, 0x00),
            Asm.Cp(16, 18), Asm.Cpc(17, 19));
        different.Steps(6);
        Assert.Equal(0, different.Sreg & FlagZ);
    }

    [Fact]
    public void SubiSbciSubtractSixteenBits()
    {
        var m = new TestMachine(Asm.Ldi(24, 0x00), Asm.Ldi(25, 0x01), Asm.Subi(24, 1), Asm.Sbci(25, 0));
        m.Steps(4);
        Assert.Equal(0x00FF, m.Word(24));
        Assert.Equal(0, m.Sreg & (FlagZ | FlagC));
    }

    [Theory]
    [InlineData("inc", 0x7F, true, 0x80, FlagV | FlagN | FlagC)]    // INC keeps C
    [InlineData("dec", 0x80, false, 0x7F, FlagV | FlagS)]
    [InlineData("neg", 0x80, false, 0x80, FlagV | FlagN | FlagC)]
    [InlineData("neg", 0x00, false, 0x00, FlagZ)]
    [InlineData("neg", 0x01, false, 0xFF, FlagH | FlagS | FlagN | FlagC)]
    [InlineData("com", 0x0F, false, 0xF0, FlagS | FlagN | FlagC)]
    [InlineData("asr", 0x81, false, 0xC0, FlagS | FlagN | FlagC)]
    [InlineData("lsr", 0x01, false, 0x00, FlagS | FlagV | FlagZ | FlagC)]
    [InlineData("ror", 0x02, true, 0x81, FlagV | FlagN)]
    [InlineData("swap", 0x12, false, 0x21, 0)]
    public void SingleOperand(string op, int input, bool carryIn, int result, int sreg)
    {
        ushort instruction = op switch
        {
            "inc" => Asm.Inc(16),
            "dec" => Asm.Dec(16),
            "neg" => Asm.Neg(16),
            "com" => Asm.Com(16),
            "asr" => Asm.Asr(16),
            "lsr" => Asm.Lsr(16),
            "ror" => Asm.Ror(16),
            _ => Asm.Swap(16),
        };
        var m = new TestMachine(carryIn ? Asm.Sec : Asm.Clc, Asm.Ldi(16, input), instruction);
        m.Steps(3);
        Assert.Equal(result, m.R(16));
        Assert.Equal(sreg, m.Sreg);
    }

    [Theory]
    [InlineData(true, 0xFFFF, 1, 0x0000, FlagZ | FlagC)]
    [InlineData(true, 0x7FFF, 1, 0x8000, FlagV | FlagN)]
    [InlineData(false, 0x0000, 1, 0xFFFF, FlagS | FlagN | FlagC)]
    [InlineData(false, 0x8000, 1, 0x7FFF, FlagS | FlagV)]
    public void AdiwSbiw(bool add, int value, int k, int result, int sreg)
    {
        var m = new TestMachine(Asm.Ldi(24, value & 0xFF), Asm.Ldi(25, value >> 8), add ? Asm.Adiw(24, k) : Asm.Sbiw(24, k));
        m.Steps(2);
        Assert.Equal(2, m.Step());
        Assert.Equal(result, m.Word(24));
        Assert.Equal(sreg, m.Sreg);
    }

    [Theory]
    [InlineData("mul", 0xFF, 0xFF, 0xFE01, FlagC)]
    [InlineData("muls", 0xFF, 0xFF, 0x0001, 0)]           // -1 * -1
    [InlineData("mulsu", 0xFF, 0xFF, 0xFF01, FlagC)]      // -1 * 255
    [InlineData("fmul", 0x80, 0x80, 0x8000, 0)]           // 1.0 * 1.0 (unsigned 1.7 format)
    [InlineData("fmuls", 0x80, 0x40, 0xC000, FlagC)]      // -1.0 * 0.5
    [InlineData("fmulsu", 0xC0, 0x80, 0xC000, FlagC)]     // -0.5 * 1.0
    [InlineData("mul", 0x00, 0x55, 0x0000, FlagZ)]
    public void Multiply(string op, int a, int b, int product, int sreg)
    {
        ushort instruction = op switch
        {
            "mul" => Asm.Mul(16, 17),
            "muls" => Asm.Muls(16, 17),
            "mulsu" => Asm.Mulsu(16, 17),
            "fmul" => Asm.Fmul(16, 17),
            "fmuls" => Asm.Fmuls(16, 17),
            _ => Asm.Fmulsu(16, 17),
        };
        var m = new TestMachine(Asm.Ldi(16, a), Asm.Ldi(17, b), instruction);
        m.Steps(2);
        Assert.Equal(2, m.Step());
        Assert.Equal(product, m.Word(0));
        Assert.Equal(sreg, m.Sreg);
    }

    [Fact]
    public void LogicClearsOverflowAndKeepsCarry()
    {
        var m = new TestMachine(Asm.Sec, Asm.Ldi(16, 0xF0), Asm.Andi(16, 0x80), Asm.Ori(16, 0x01), Asm.Ldi(17, 0x81), Asm.Eor(16, 17));
        m.Steps(3);
        Assert.Equal(0x80, m.R(16));
        Assert.Equal(FlagS | FlagN | FlagC, m.Sreg);
        m.Steps(3);
        Assert.Equal(0x00, m.R(16));
        Assert.Equal(FlagZ | FlagC, m.Sreg);
    }
}

public class ControlFlowTests
{
    const ushort Clz = 0x9498;

    [Fact]
    public void BranchTakesTwoCyclesWhenTakenAndOneOtherwise()
    {
        var taken = new TestMachine(Asm.Sez, Asm.Breq(2));
        taken.Step();
        Assert.Equal(2, taken.Step());
        Assert.Equal(4, taken.Cpu.PC);

        var notTaken = new TestMachine(Clz, Asm.Breq(2));
        notTaken.Step();
        Assert.Equal(1, notTaken.Step());
        Assert.Equal(2, notTaken.Cpu.PC);
    }

    [Fact]
    public void CpseSkipsATwoWordInstructionInThreeCycles()
    {
        var m = new TestMachine(Asm.Ldi(16, 5), Asm.Ldi(17, 5), Asm.Cpse(16, 17), Asm.Jmp(0x100), Asm.Nop);
        m.Steps(2);
        Assert.Equal(3, m.Step());
        Assert.Equal(5, m.Cpu.PC);
    }

    [Fact]
    public void CpseDoesNotSkipWhenDifferent()
    {
        var m = new TestMachine(Asm.Ldi(16, 5), Asm.Ldi(17, 6), Asm.Cpse(16, 17), Asm.Nop);
        m.Steps(2);
        Assert.Equal(1, m.Step());
        Assert.Equal(3, m.Cpu.PC);
    }

    [Fact]
    public void SbrsSkipsAOneWordInstructionInTwoCycles()
    {
        var m = new TestMachine(Asm.Ldi(16, 0x80), Asm.Sbrs(16, 7), Asm.Nop, Asm.Nop);
        m.Step();
        Assert.Equal(2, m.Step());
        Assert.Equal(3, m.Cpu.PC);
    }

    [Fact]
    public void RcallPushesTheReturnAddressLowByteFirst()
    {
        var m = new TestMachine(Asm.Rcall(2), Asm.Nop, Asm.Nop, Asm.Ret);
        int sp = m.Cpu.SP;
        Assert.Equal(3, m.Step());
        Assert.Equal(3, m.Cpu.PC);
        Assert.Equal(sp - 2, m.Cpu.SP);
        Assert.Equal(0x01, m.Cpu.DataSpace[sp]);     // low byte of return address 1
        Assert.Equal(0x00, m.Cpu.DataSpace[sp - 1]); // high byte
        Assert.Equal(4, m.Step());
        Assert.Equal(1, m.Cpu.PC);
        Assert.Equal(sp, m.Cpu.SP);
    }

    [Fact]
    public void CallAndRetTakeFourCyclesEach()
    {
        var m = new TestMachine(Asm.Call(4), Asm.Nop, Asm.Nop, Asm.Ret);
        Assert.Equal(4, m.Step());
        Assert.Equal(4, m.Cpu.PC);
        Assert.Equal(4, m.Step());
        Assert.Equal(2, m.Cpu.PC);
    }

    [Fact]
    public void RjmpBackwardsAndIndirectJumps()
    {
        var m = new TestMachine(Asm.Nop, Asm.Rjmp(-2));
        m.Step();
        Assert.Equal(2, m.Step());
        Assert.Equal(0, m.Cpu.PC);

        var indirect = new TestMachine(Asm.Ldi(30, 7), Asm.Ldi(31, 0), Asm.Icall);
        indirect.Steps(2);
        Assert.Equal(3, indirect.Step());
        Assert.Equal(7, indirect.Cpu.PC);
        Assert.Equal(0x03, indirect.Cpu.DataSpace[indirect.Cpu.SP + 2]); // return address 3
    }

    [Fact]
    public void PushAndPop()
    {
        var m = new TestMachine(Asm.Ldi(16, 0xAB), Asm.Push(16), Asm.Ldi(16, 0), Asm.Pop(17));
        int sp = m.Cpu.SP;
        m.Step();
        Assert.Equal(2, m.Step());
        Assert.Equal(sp - 1, m.Cpu.SP);
        m.Step();
        Assert.Equal(2, m.Step());
        Assert.Equal(0xAB, m.R(17));
        Assert.Equal(sp, m.Cpu.SP);
    }
}

public class DataTransferTests
{
    [Fact]
    public void PointerPostIncrementAndPreDecrement()
    {
        var m = new TestMachine(Asm.Ldi(26, 0x00), Asm.Ldi(27, 0x01), Asm.Ldi(16, 0x5A), Asm.StXInc(16), Asm.LdXDec(17));
        m.Steps(3);
        Assert.Equal(2, m.Step());
        Assert.Equal(0x5A, m.Cpu.DataSpace[0x100]);
        Assert.Equal(0x101, m.Word(26));
        Assert.Equal(2, m.Step());
        Assert.Equal(0x100, m.Word(26));
        Assert.Equal(0x5A, m.R(17));
    }

    [Fact]
    public void DisplacementAddressing()
    {
        var m = new TestMachine(Asm.Ldi(28, 0x00), Asm.Ldi(29, 0x02), Asm.Ldi(16, 0x77), Asm.Std(true, 63, 16), Asm.Ldd(17, true, 63));
        m.Steps(5);
        Assert.Equal(0x77, m.Cpu.DataSpace[0x23F]);
        Assert.Equal(0x77, m.R(17));
    }

    [Fact]
    public void LdsAndStsAreTwoWordsAndTwoCycles()
    {
        var m = new TestMachine(Asm.Ldi(16, 0x42), Asm.Sts(0x0300, 16), Asm.Lds(17, 0x0300));
        m.Step();
        Assert.Equal(2, m.Step());
        Assert.Equal(3, m.Cpu.PC);
        Assert.Equal(2, m.Step());
        Assert.Equal(5, m.Cpu.PC);
        Assert.Equal(0x42, m.R(17));
    }

    [Fact]
    public void LpmReadsProgramMemoryBytes()
    {
        var program = new ushort[11];
        program[0] = Asm.Ldi(30, 20); // byte address 20 = word 10
        program[1] = Asm.Ldi(31, 0);
        program[2] = Asm.LpmInc(16);
        program[3] = Asm.Lpm(17);
        program[10] = 0x1234;
        var m = new TestMachine(program);
        m.Steps(2);
        Assert.Equal(3, m.Step());
        Assert.Equal(3, m.Step());
        Assert.Equal(0x34, m.R(16));
        Assert.Equal(0x12, m.R(17));
    }

    [Fact]
    public void MovwCopiesARegisterPair()
    {
        var m = new TestMachine(Asm.Ldi(16, 1), Asm.Ldi(17, 2), Asm.Movw(24, 16));
        m.Steps(3);
        Assert.Equal(0x0201, m.Word(24));
    }

    [Fact]
    public void BstAndBldMoveABitThroughT()
    {
        var m = new TestMachine(Asm.Ldi(16, 0x08), Asm.Bst(16, 3), Asm.Ldi(17, 0x00), Asm.Bld(17, 6));
        m.Steps(4);
        Assert.Equal(0x40, m.R(17));
        Assert.Equal(FlagT, m.Sreg);
    }

    [Fact]
    public void StatusRegisterAndStackPointerAreIoRegisters()
    {
        var m = new TestMachine(Asm.Ldi(16, 0x55), Asm.Out(0x3F, 16), Asm.In(17, 0x3F),
            Asm.Ldi(18, 0x00), Asm.Out(0x3D, 18), Asm.Ldi(18, 0x05), Asm.Out(0x3E, 18));
        m.Steps(7);
        Assert.Equal(0x55, m.R(17));
        Assert.Equal(0x0500, m.Cpu.SP);
    }
}

public class IoAndInterruptTests
{
    const int PinbIo = 0x03, DdrbIo = 0x04, PortbIo = 0x05;

    [Fact]
    public void SbiOnPinToggleTheOutput()
    {
        var m = new TestMachine(Asm.Sbi(DdrbIo, 5), Asm.Sbi(PinbIo, 5), Asm.Sbi(PinbIo, 5));
        Assert.Equal(2, m.Step());
        Assert.Equal(0x20, m.Mcu.PortB.Ddr);
        m.Step();
        Assert.Equal(0x20, m.Mcu.PortB.PortRegister);
        m.Step();
        Assert.Equal(0x00, m.Mcu.PortB.PortRegister);
    }

    [Fact]
    public void SbisSeesThePullUpOnAnUndrivenInput()
    {
        var m = new TestMachine(Asm.Sbi(PortbIo, 0), Asm.Sbis(PinbIo, 0), Asm.Nop, Asm.Nop);
        m.Step();
        Assert.Equal(2, m.Step());
        Assert.Equal(3, m.Cpu.PC);
    }

    [Fact]
    public void OneInstructionRunsAfterSeiAndAfterReti()
    {
        var program = new ushort[34];
        var main = Asm.Program(
            Asm.Ldi(16, 1), Asm.Sts(0x6E, 16),     // TIMSK0 = TOIE0
            Asm.Ldi(16, 0xFF), Asm.Out(0x26, 16),  // TCNT0 = 0xFF
            Asm.Ldi(16, 1), Asm.Out(0x25, 16),     // TCCR0B: clock/1, overflows on the next tick
            Asm.Nop, Asm.Nop,
            Asm.Sei,                               // word 9
            Asm.Ldi(20, 0x11),                     // word 10: must run before the interrupt
            Asm.Ldi(21, 0x22),                     // word 11: must run right after RETI
            Asm.Rjmp(-1));
        main.CopyTo(program, 0);
        program[32] = Asm.Ldi(22, 0x33);           // TIMER0_OVF vector (16 * 2)
        program[33] = Asm.Reti;
        var m = new TestMachine(program);

        m.Steps(9);
        Assert.Equal(10, m.Cpu.PC);
        m.Step();
        Assert.Equal(0x11, m.R(20));
        Assert.Equal(4, m.Step());                 // interrupt entry
        Assert.Equal(32, m.Cpu.PC);
        Assert.Equal(0, m.Sreg & FlagI);
        m.Step();
        Assert.Equal(4, m.Step());                 // RETI
        Assert.Equal(11, m.Cpu.PC);
        Assert.Equal(FlagI, m.Sreg & FlagI);
        m.Step();
        Assert.Equal(0x22, m.R(21));
        Assert.Equal(0x33, m.R(22));
        Assert.Equal(1, m.Cpu.InterruptsServiced);
    }
}
