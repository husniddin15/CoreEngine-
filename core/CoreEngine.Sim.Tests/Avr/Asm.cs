namespace CoreEngine.Sim.Tests.Avr;

/// <summary>Minimal AVR instruction encoder for tests (AVR Instruction Set Manual encodings).</summary>
internal static class Asm
{
    public const ushort Nop = 0x0000, Ret = 0x9508, Reti = 0x9518, Sleep = 0x9588;
    public const ushort Sei = 0x9478, Cli = 0x94F8, Sec = 0x9408, Clc = 0x9488, Sez = 0x9418, Set = 0x9468, Clt = 0x94E8;
    public const ushort Ijmp = 0x9409, Icall = 0x9509;

    static ushort TwoReg(int op, int d, int r) => (ushort)(op | ((r & 0x10) << 5) | (d << 4) | (r & 0x0F));
    static ushort Imm(int op, int d, int k) => (ushort)(op | ((k & 0xF0) << 4) | ((d - 16) << 4) | (k & 0x0F));
    static ushort One(int low, int d) => (ushort)(0x9400 | (d << 4) | low);

    public static ushort Add(int d, int r) => TwoReg(0x0C00, d, r);
    public static ushort Adc(int d, int r) => TwoReg(0x1C00, d, r);
    public static ushort Sub(int d, int r) => TwoReg(0x1800, d, r);
    public static ushort Sbc(int d, int r) => TwoReg(0x0800, d, r);
    public static ushort Cp(int d, int r) => TwoReg(0x1400, d, r);
    public static ushort Cpc(int d, int r) => TwoReg(0x0400, d, r);
    public static ushort Cpse(int d, int r) => TwoReg(0x1000, d, r);
    public static ushort And(int d, int r) => TwoReg(0x2000, d, r);
    public static ushort Eor(int d, int r) => TwoReg(0x2400, d, r);
    public static ushort Or(int d, int r) => TwoReg(0x2800, d, r);
    public static ushort Mov(int d, int r) => TwoReg(0x2C00, d, r);
    public static ushort Mul(int d, int r) => TwoReg(0x9C00, d, r);

    public static ushort Ldi(int d, int k) => Imm(0xE000, d, k);
    public static ushort Cpi(int d, int k) => Imm(0x3000, d, k);
    public static ushort Sbci(int d, int k) => Imm(0x4000, d, k);
    public static ushort Subi(int d, int k) => Imm(0x5000, d, k);
    public static ushort Ori(int d, int k) => Imm(0x6000, d, k);
    public static ushort Andi(int d, int k) => Imm(0x7000, d, k);

    public static ushort Com(int d) => One(0x0, d);
    public static ushort Neg(int d) => One(0x1, d);
    public static ushort Swap(int d) => One(0x2, d);
    public static ushort Inc(int d) => One(0x3, d);
    public static ushort Asr(int d) => One(0x5, d);
    public static ushort Lsr(int d) => One(0x6, d);
    public static ushort Ror(int d) => One(0x7, d);
    public static ushort Dec(int d) => One(0xA, d);

    public static ushort Adiw(int d, int k) => (ushort)(0x9600 | ((k & 0x30) << 2) | (((d - 24) / 2) << 4) | (k & 0x0F));
    public static ushort Sbiw(int d, int k) => (ushort)(0x9700 | ((k & 0x30) << 2) | (((d - 24) / 2) << 4) | (k & 0x0F));
    public static ushort Movw(int d, int r) => (ushort)(0x0100 | ((d / 2) << 4) | (r / 2));
    public static ushort Muls(int d, int r) => (ushort)(0x0200 | ((d - 16) << 4) | (r - 16));
    public static ushort Mulsu(int d, int r) => (ushort)(0x0300 | ((d - 16) << 4) | (r - 16));
    public static ushort Fmul(int d, int r) => (ushort)(0x0308 | ((d - 16) << 4) | (r - 16));
    public static ushort Fmuls(int d, int r) => (ushort)(0x0380 | ((d - 16) << 4) | (r - 16));
    public static ushort Fmulsu(int d, int r) => (ushort)(0x0388 | ((d - 16) << 4) | (r - 16));

    public static ushort Brbs(int s, int k) => (ushort)(0xF000 | ((k & 0x7F) << 3) | s);
    public static ushort Brbc(int s, int k) => (ushort)(0xF400 | ((k & 0x7F) << 3) | s);
    public static ushort Breq(int k) => Brbs(1, k);
    public static ushort Brne(int k) => Brbc(1, k);
    public static ushort Rjmp(int k) => (ushort)(0xC000 | (k & 0xFFF));
    public static ushort Rcall(int k) => (ushort)(0xD000 | (k & 0xFFF));
    public static ushort[] Jmp(int address) => new[] { (ushort)(0x940C | (((address >> 17) & 0x1F) << 4) | ((address >> 16) & 1)), (ushort)address };
    public static ushort[] Call(int address) => new[] { (ushort)(0x940E | (((address >> 17) & 0x1F) << 4) | ((address >> 16) & 1)), (ushort)address };

    public static ushort In(int d, int ioAddress) => (ushort)(0xB000 | ((ioAddress & 0x30) << 5) | (d << 4) | (ioAddress & 0x0F));
    public static ushort Out(int ioAddress, int r) => (ushort)(0xB800 | ((ioAddress & 0x30) << 5) | (r << 4) | (ioAddress & 0x0F));
    public static ushort Sbi(int ioAddress, int bit) => (ushort)(0x9A00 | (ioAddress << 3) | bit);
    public static ushort Cbi(int ioAddress, int bit) => (ushort)(0x9800 | (ioAddress << 3) | bit);
    public static ushort Sbic(int ioAddress, int bit) => (ushort)(0x9900 | (ioAddress << 3) | bit);
    public static ushort Sbis(int ioAddress, int bit) => (ushort)(0x9B00 | (ioAddress << 3) | bit);
    public static ushort Sbrc(int r, int bit) => (ushort)(0xFC00 | (r << 4) | bit);
    public static ushort Sbrs(int r, int bit) => (ushort)(0xFE00 | (r << 4) | bit);
    public static ushort Bld(int d, int bit) => (ushort)(0xF800 | (d << 4) | bit);
    public static ushort Bst(int d, int bit) => (ushort)(0xFA00 | (d << 4) | bit);

    public static ushort Push(int r) => (ushort)(0x920F | (r << 4));
    public static ushort Pop(int d) => (ushort)(0x900F | (d << 4));
    public static ushort[] Lds(int d, int address) => new[] { (ushort)(0x9000 | (d << 4)), (ushort)address };
    public static ushort[] Sts(int address, int r) => new[] { (ushort)(0x9200 | (r << 4)), (ushort)address };
    public static ushort LdX(int d) => (ushort)(0x900C | (d << 4));
    public static ushort LdXInc(int d) => (ushort)(0x900D | (d << 4));
    public static ushort LdXDec(int d) => (ushort)(0x900E | (d << 4));
    public static ushort LdYInc(int d) => (ushort)(0x9009 | (d << 4));
    public static ushort LdZInc(int d) => (ushort)(0x9001 | (d << 4));
    public static ushort StX(int r) => (ushort)(0x920C | (r << 4));
    public static ushort StXInc(int r) => (ushort)(0x920D | (r << 4));
    public static ushort StYDec(int r) => (ushort)(0x920A | (r << 4));
    public static ushort StZInc(int r) => (ushort)(0x9201 | (r << 4));
    public static ushort Ldd(int d, bool useY, int q) =>
        (ushort)(0x8000 | ((q & 0x20) << 8) | ((q & 0x18) << 7) | (d << 4) | (useY ? 0x8 : 0) | (q & 0x7));
    public static ushort Std(bool useY, int q, int r) => (ushort)(Ldd(r, useY, q) | 0x0200);
    public static ushort Lpm(int d) => (ushort)(0x9004 | (d << 4));
    public static ushort LpmInc(int d) => (ushort)(0x9005 | (d << 4));

    /// <summary>Flattens single words and word arrays into one program.</summary>
    public static ushort[] Program(params object[] parts)
    {
        var list = new List<ushort>();
        foreach (var part in parts)
        {
            switch (part)
            {
                case ushort w: list.Add(w); break;
                case ushort[] ws: list.AddRange(ws); break;
                case int i: list.Add((ushort)i); break;
                default: throw new ArgumentException($"Unsupported program part {part}");
            }
        }
        return list.ToArray();
    }
}
