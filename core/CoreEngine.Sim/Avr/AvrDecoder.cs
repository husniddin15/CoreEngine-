namespace CoreEngine.Sim.Avr
{
    /// <summary>Instruction kinds of the AVRe+ instruction set (ATmega328P and relatives).</summary>
    internal enum AvrOp : byte
    {
        Invalid = 0,
        Nop, Movw, Muls, Mulsu, Fmul, Fmuls, Fmulsu,
        Cpc, Sbc, Add, Cpse, Cp, Sub, Adc, And, Eor, Or, Mov,
        Cpi, Sbci, Subi, Ori, Andi,
        LddY, LddZ, StdY, StdZ,
        Lds, LdZInc, LdZDec, LpmZ, LpmZInc, ElpmZ, ElpmZInc, LdYInc, LdYDec, LdX, LdXInc, LdXDec, Pop,
        Sts, StZInc, StZDec, StYInc, StYDec, StX, StXInc, StXDec, Push,
        Com, Neg, Swap, Inc, Asr, Lsr, Ror, Dec,
        Bset, Bclr, Ret, Reti, Sleep, Break, Wdr, LpmR0, ElpmR0, Spm,
        Ijmp, Icall, Eijmp, Eicall, Jmp, Call,
        Adiw, Sbiw, Cbi, Sbic, Sbi, Sbis, Mul, In, Out,
        Rjmp, Rcall, Ldi, Brbs, Brbc, Bld, Bst, Sbrc, Sbrs,
    }

    /// <summary>
    /// A pre-decoded instruction. Operand meaning depends on <see cref="Op"/>:
    /// D = destination/source register, register-pair base, I/O address (0-63) or SREG bit;
    /// R = source register or bit number;
    /// K = immediate, data/program address, displacement or signed relative offset.
    /// </summary>
    internal struct AvrInstruction
    {
        public AvrOp Op;
        public byte D;
        public byte R;
        public int K;
    }

    /// <summary>Decodes 16-bit AVR opcodes (AVR Instruction Set Manual, AVRe+ subset).</summary>
    internal static class AvrDecoder
    {
        /// <summary>True for the 32-bit instructions LDS, STS, JMP and CALL.</summary>
        public static bool IsTwoWord(ushort w) => (w & 0xFC0F) == 0x9000 || (w & 0xFE0C) == 0x940C;

        public static AvrInstruction Decode(ushort w, ushort next)
        {
            var i = new AvrInstruction();
            byte d5 = (byte)((w >> 4) & 0x1F);
            byte r5 = (byte)((w & 0x0F) | ((w >> 5) & 0x10));

            switch (w >> 12)
            {
                case 0x0:
                    switch ((w >> 10) & 0x3)
                    {
                        case 0:
                            switch ((w >> 8) & 0x3)
                            {
                                case 0:
                                    if (w == 0) i.Op = AvrOp.Nop;
                                    break;
                                case 1:
                                    i.Op = AvrOp.Movw;
                                    i.D = (byte)(((w >> 4) & 0xF) * 2);
                                    i.R = (byte)((w & 0xF) * 2);
                                    break;
                                case 2:
                                    i.Op = AvrOp.Muls;
                                    i.D = (byte)(16 + ((w >> 4) & 0xF));
                                    i.R = (byte)(16 + (w & 0xF));
                                    break;
                                default:
                                    i.D = (byte)(16 + ((w >> 4) & 0x7));
                                    i.R = (byte)(16 + (w & 0x7));
                                    switch (((w >> 6) & 2) | ((w >> 3) & 1))
                                    {
                                        case 0: i.Op = AvrOp.Mulsu; break;
                                        case 1: i.Op = AvrOp.Fmul; break;
                                        case 2: i.Op = AvrOp.Fmuls; break;
                                        default: i.Op = AvrOp.Fmulsu; break;
                                    }
                                    break;
                            }
                            break;
                        case 1: i.Op = AvrOp.Cpc; i.D = d5; i.R = r5; break;
                        case 2: i.Op = AvrOp.Sbc; i.D = d5; i.R = r5; break;
                        default: i.Op = AvrOp.Add; i.D = d5; i.R = r5; break;
                    }
                    break;

                case 0x1:
                    i.D = d5; i.R = r5;
                    switch ((w >> 10) & 0x3)
                    {
                        case 0: i.Op = AvrOp.Cpse; break;
                        case 1: i.Op = AvrOp.Cp; break;
                        case 2: i.Op = AvrOp.Sub; break;
                        default: i.Op = AvrOp.Adc; break;
                    }
                    break;

                case 0x2:
                    i.D = d5; i.R = r5;
                    switch ((w >> 10) & 0x3)
                    {
                        case 0: i.Op = AvrOp.And; break;
                        case 1: i.Op = AvrOp.Eor; break;
                        case 2: i.Op = AvrOp.Or; break;
                        default: i.Op = AvrOp.Mov; break;
                    }
                    break;

                case 0x3: DecodeImmediate(ref i, w, AvrOp.Cpi); break;
                case 0x4: DecodeImmediate(ref i, w, AvrOp.Sbci); break;
                case 0x5: DecodeImmediate(ref i, w, AvrOp.Subi); break;
                case 0x6: DecodeImmediate(ref i, w, AvrOp.Ori); break;
                case 0x7: DecodeImmediate(ref i, w, AvrOp.Andi); break;

                case 0x8:
                case 0xA:
                {
                    // LDD/STD with displacement: 10q0 qqsd dddd yqqq
                    i.D = d5;
                    i.K = ((w >> 8) & 0x20) | ((w >> 7) & 0x18) | (w & 0x7);
                    bool store = (w & 0x0200) != 0;
                    bool useY = (w & 0x0008) != 0;
                    i.Op = store ? (useY ? AvrOp.StdY : AvrOp.StdZ) : (useY ? AvrOp.LddY : AvrOp.LddZ);
                    break;
                }

                case 0x9:
                    DecodeGroup9(ref i, w, next, d5, r5);
                    break;

                case 0xB:
                    i.D = d5;
                    i.K = ((w >> 5) & 0x30) | (w & 0xF);
                    i.Op = (w & 0x0800) != 0 ? AvrOp.Out : AvrOp.In;
                    break;

                case 0xC:
                    i.Op = AvrOp.Rjmp;
                    i.K = SignExtend(w & 0xFFF, 12);
                    break;

                case 0xD:
                    i.Op = AvrOp.Rcall;
                    i.K = SignExtend(w & 0xFFF, 12);
                    break;

                case 0xE:
                    DecodeImmediate(ref i, w, AvrOp.Ldi);
                    break;

                default: // 0xF
                    switch ((w >> 10) & 0x3)
                    {
                        case 0:
                            i.Op = AvrOp.Brbs;
                            i.R = (byte)(w & 7);
                            i.K = SignExtend((w >> 3) & 0x7F, 7);
                            break;
                        case 1:
                            i.Op = AvrOp.Brbc;
                            i.R = (byte)(w & 7);
                            i.K = SignExtend((w >> 3) & 0x7F, 7);
                            break;
                        case 2:
                            if ((w & 0x8) == 0)
                            {
                                i.D = d5;
                                i.R = (byte)(w & 7);
                                i.Op = (w & 0x0200) != 0 ? AvrOp.Bst : AvrOp.Bld;
                            }
                            break;
                        default:
                            if ((w & 0x8) == 0)
                            {
                                i.D = d5;
                                i.R = (byte)(w & 7);
                                i.Op = (w & 0x0200) != 0 ? AvrOp.Sbrs : AvrOp.Sbrc;
                            }
                            break;
                    }
                    break;
            }

            return i;
        }

        static void DecodeImmediate(ref AvrInstruction i, ushort w, AvrOp op)
        {
            i.Op = op;
            i.D = (byte)(16 + ((w >> 4) & 0xF));
            i.K = ((w >> 4) & 0xF0) | (w & 0xF);
        }

        static void DecodeGroup9(ref AvrInstruction i, ushort w, ushort next, byte d5, byte r5)
        {
            switch ((w >> 9) & 0x7)
            {
                case 0: // 1001 000d dddd xxxx : loads
                    i.D = d5;
                    switch (w & 0xF)
                    {
                        case 0x0: i.Op = AvrOp.Lds; i.K = next; break;
                        case 0x1: i.Op = AvrOp.LdZInc; break;
                        case 0x2: i.Op = AvrOp.LdZDec; break;
                        case 0x4: i.Op = AvrOp.LpmZ; break;
                        case 0x5: i.Op = AvrOp.LpmZInc; break;
                        case 0x6: i.Op = AvrOp.ElpmZ; break;
                        case 0x7: i.Op = AvrOp.ElpmZInc; break;
                        case 0x9: i.Op = AvrOp.LdYInc; break;
                        case 0xA: i.Op = AvrOp.LdYDec; break;
                        case 0xC: i.Op = AvrOp.LdX; break;
                        case 0xD: i.Op = AvrOp.LdXInc; break;
                        case 0xE: i.Op = AvrOp.LdXDec; break;
                        case 0xF: i.Op = AvrOp.Pop; break;
                    }
                    break;

                case 1: // 1001 001r rrrr xxxx : stores (register kept in D)
                    i.D = d5;
                    switch (w & 0xF)
                    {
                        case 0x0: i.Op = AvrOp.Sts; i.K = next; break;
                        case 0x1: i.Op = AvrOp.StZInc; break;
                        case 0x2: i.Op = AvrOp.StZDec; break;
                        case 0x9: i.Op = AvrOp.StYInc; break;
                        case 0xA: i.Op = AvrOp.StYDec; break;
                        case 0xC: i.Op = AvrOp.StX; break;
                        case 0xD: i.Op = AvrOp.StXInc; break;
                        case 0xE: i.Op = AvrOp.StXDec; break;
                        case 0xF: i.Op = AvrOp.Push; break;
                    }
                    break;

                case 2: // 1001 010x : one-operand and control instructions
                    switch (w & 0xF)
                    {
                        case 0x0: i.Op = AvrOp.Com; i.D = d5; break;
                        case 0x1: i.Op = AvrOp.Neg; i.D = d5; break;
                        case 0x2: i.Op = AvrOp.Swap; i.D = d5; break;
                        case 0x3: i.Op = AvrOp.Inc; i.D = d5; break;
                        case 0x5: i.Op = AvrOp.Asr; i.D = d5; break;
                        case 0x6: i.Op = AvrOp.Lsr; i.D = d5; break;
                        case 0x7: i.Op = AvrOp.Ror; i.D = d5; break;
                        case 0xA: i.Op = AvrOp.Dec; i.D = d5; break;
                        case 0x8:
                            if ((w & 0x0100) == 0)
                            {
                                i.D = (byte)((w >> 4) & 7);
                                i.Op = (w & 0x0080) != 0 ? AvrOp.Bclr : AvrOp.Bset;
                            }
                            else
                            {
                                switch (w)
                                {
                                    case 0x9508: i.Op = AvrOp.Ret; break;
                                    case 0x9518: i.Op = AvrOp.Reti; break;
                                    case 0x9588: i.Op = AvrOp.Sleep; break;
                                    case 0x9598: i.Op = AvrOp.Break; break;
                                    case 0x95A8: i.Op = AvrOp.Wdr; break;
                                    case 0x95C8: i.Op = AvrOp.LpmR0; break;
                                    case 0x95D8: i.Op = AvrOp.ElpmR0; break;
                                    case 0x95E8: i.Op = AvrOp.Spm; break;
                                }
                            }
                            break;
                        case 0x9:
                            switch (w)
                            {
                                case 0x9409: i.Op = AvrOp.Ijmp; break;
                                case 0x9419: i.Op = AvrOp.Eijmp; break;
                                case 0x9509: i.Op = AvrOp.Icall; break;
                                case 0x9519: i.Op = AvrOp.Eicall; break;
                            }
                            break;
                        case 0xC:
                        case 0xD:
                            i.Op = AvrOp.Jmp;
                            i.K = (((w >> 4) & 0x1F) << 17) | ((w & 1) << 16) | next;
                            break;
                        case 0xE:
                        case 0xF:
                            i.Op = AvrOp.Call;
                            i.K = (((w >> 4) & 0x1F) << 17) | ((w & 1) << 16) | next;
                            break;
                    }
                    break;

                case 3: // 1001 011x KKdd KKKK : ADIW / SBIW
                    i.D = (byte)(24 + ((w >> 4) & 3) * 2);
                    i.K = ((w >> 2) & 0x30) | (w & 0xF);
                    i.Op = (w & 0x0100) != 0 ? AvrOp.Sbiw : AvrOp.Adiw;
                    break;

                case 4: // 1001 100x AAAA Abbb : CBI / SBIC
                    i.D = (byte)((w >> 3) & 0x1F);
                    i.R = (byte)(w & 7);
                    i.Op = (w & 0x0100) != 0 ? AvrOp.Sbic : AvrOp.Cbi;
                    break;

                case 5: // 1001 101x AAAA Abbb : SBI / SBIS
                    i.D = (byte)((w >> 3) & 0x1F);
                    i.R = (byte)(w & 7);
                    i.Op = (w & 0x0100) != 0 ? AvrOp.Sbis : AvrOp.Sbi;
                    break;

                default: // 1001 11rd dddd rrrr : MUL
                    i.Op = AvrOp.Mul;
                    i.D = d5;
                    i.R = r5;
                    break;
            }
        }

        static int SignExtend(int value, int bits)
        {
            int sign = 1 << (bits - 1);
            return (value & sign) != 0 ? value - (1 << bits) : value;
        }
    }
}
