using CoreEngine.Sim.Avr;

namespace CoreEngine.Sim.Tests.Avr;

/// <summary>An ATmega328P with a hand-written program at address 0, stepped one instruction at a time.</summary>
internal sealed class TestMachine
{
    public const int FlagC = AvrCpu.FlagC, FlagZ = AvrCpu.FlagZ, FlagN = AvrCpu.FlagN, FlagV = AvrCpu.FlagV;
    public const int FlagS = AvrCpu.FlagS, FlagH = AvrCpu.FlagH, FlagT = AvrCpu.FlagT, FlagI = AvrCpu.FlagI;

    public TestMachine(params object[] program)
    {
        Cpu.LoadWords(0, Asm.Program(program));
    }

    public Atmega328P Mcu { get; } = new();

    public AvrCpu Cpu => Mcu.Cpu;

    /// <summary>Executes one instruction (or one interrupt entry) and returns the cycles it took.</summary>
    public long Step()
    {
        long before = Cpu.Cycles;
        Cpu.Run(Cpu.Cycles + 1);
        return Cpu.Cycles - before;
    }

    public long Steps(int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++) total += Step();
        return total;
    }

    public byte R(int index) => Cpu.GetRegister(index);

    public int Sreg => Cpu.SREG;

    public int Word(int lowRegister) => Cpu.GetRegister(lowRegister) | (Cpu.GetRegister(lowRegister + 1) << 8);
}
