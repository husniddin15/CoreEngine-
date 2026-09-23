using System;

namespace CoreEngine.Sim.Avr
{
    /// <summary>Reads an I/O register (data addresses 0x20-0xFF).</summary>
    public delegate byte IoReadHandler(int address);

    /// <summary>
    /// Writes an I/O register. <paramref name="mask"/> is 0xFF for normal writes and a single bit
    /// for SBI/CBI, so registers whose bits are cleared by writing one (interrupt flags) and the
    /// PINx toggle registers only react to the addressed bit.
    /// </summary>
    public delegate void IoWriteHandler(int address, byte value, byte mask);

    /// <summary>
    /// Cycle-counting AVR CPU core (AVRe+ instruction set, 16-bit program counter), as specified in
    /// docs/05-arduino-emulation-spec.md §3. The program is pre-decoded once per upload; peripherals
    /// attach through I/O register hooks, interrupt lines and cycle-stamped events.
    /// </summary>
    public sealed class AvrCpu
    {
        public const int FlagC = 0x01, FlagZ = 0x02, FlagN = 0x04, FlagV = 0x08;
        public const int FlagS = 0x10, FlagH = 0x20, FlagT = 0x40, FlagI = 0x80;

        public const int AddressSpl = 0x5D, AddressSph = 0x5E, AddressSreg = 0x5F;

        const int MaxEvents = 32;

        readonly byte[] data;
        readonly byte[] flashBytes;
        readonly ushort[] flashWords;
        readonly AvrInstruction[] decoded;
        readonly int flashWordMask;
        readonly int flashByteMask;
        readonly IoReadHandler?[] readHooks = new IoReadHandler?[0x100];
        readonly IoWriteHandler?[] writeHooks = new IoWriteHandler?[0x100];
        readonly Action?[] interruptAcknowledge;
        readonly int vectorCount;

        readonly long[] eventCycles = new long[MaxEvents];
        readonly Action<long>?[] eventHandlers = new Action<long>?[MaxEvents];
        int eventCount;
        long nextEventCycle = long.MaxValue;

        int sp;
        byte sreg;
        ulong pendingInterrupts;
        bool interruptInhibit;
        bool sleeping;

        /// <summary>Total clock cycles executed since power-on.</summary>
        public long Cycles;

        /// <summary>Program counter as a word address.</summary>
        public int PC;

        /// <summary>Data address of SMCR (sleep enable in bit 0); -1 means SLEEP always sleeps.</summary>
        public int SleepControlAddress = -1;

        /// <summary>Number of interrupts serviced since power-on.</summary>
        public long InterruptsServiced { get; private set; }

        /// <summary>Raised when the program executes something the emulator does not support.</summary>
        public event Action<string>? Diagnostic;

        /// <summary>Raised by the WDR instruction.</summary>
        public event Action? WatchdogReset;

        public AvrCpu(int flashSizeBytes, int dataSizeBytes, int vectorCount)
        {
            if (flashSizeBytes <= 0 || (flashSizeBytes & (flashSizeBytes - 1)) != 0)
                throw new ArgumentException("Flash size must be a power of two.", nameof(flashSizeBytes));
            if (dataSizeBytes < 0x100)
                throw new ArgumentException("Data space must include registers and I/O.", nameof(dataSizeBytes));

            data = new byte[dataSizeBytes];
            flashBytes = new byte[flashSizeBytes];
            flashWords = new ushort[flashSizeBytes / 2];
            decoded = new AvrInstruction[flashSizeBytes / 2];
            flashWordMask = flashWords.Length - 1;
            flashByteMask = flashSizeBytes - 1;
            this.vectorCount = vectorCount;
            interruptAcknowledge = new Action?[Math.Max(vectorCount, 1)];
            for (int i = 0; i < flashBytes.Length; i++) flashBytes[i] = 0xFF;
            RebuildDecodeCache();
        }

        /// <summary>The whole data space: registers, I/O and SRAM.</summary>
        public byte[] DataSpace => data;

        public int FlashSize => flashBytes.Length;

        public int SP
        {
            get => sp;
            set => sp = value & 0xFFFF;
        }

        public byte SREG
        {
            get => sreg;
            set => sreg = value;
        }

        public bool IsSleeping => sleeping;

        public byte GetRegister(int index) => data[index];

        public void SetRegister(int index, byte value) => data[index] = value;

        public byte ReadFlashByte(int address) => flashBytes[address & flashByteMask];

        public ushort ReadFlashWord(int wordAddress) => flashWords[wordAddress & flashWordMask];

        /// <summary>Loads a program image (byte address 0 upwards) and pre-decodes it.</summary>
        public void LoadProgram(byte[] image)
        {
            if (image.Length > flashBytes.Length)
                throw new ArgumentException("Program is larger than the flash memory.", nameof(image));
            for (int i = 0; i < flashBytes.Length; i++) flashBytes[i] = i < image.Length ? image[i] : (byte)0xFF;
            RebuildDecodeCache();
        }

        /// <summary>Writes raw words into flash (used by tests), then re-decodes.</summary>
        public void LoadWords(int wordAddress, params ushort[] words)
        {
            for (int i = 0; i < words.Length; i++)
            {
                int byteAddress = ((wordAddress + i) & flashWordMask) * 2;
                flashBytes[byteAddress] = (byte)words[i];
                flashBytes[byteAddress + 1] = (byte)(words[i] >> 8);
            }
            RebuildDecodeCache();
        }

        void RebuildDecodeCache()
        {
            for (int i = 0; i < flashWords.Length; i++)
                flashWords[i] = (ushort)(flashBytes[2 * i] | (flashBytes[2 * i + 1] << 8));
            for (int i = 0; i < flashWords.Length; i++)
                decoded[i] = AvrDecoder.Decode(flashWords[i], flashWords[(i + 1) & flashWordMask]);
        }

        /// <summary>CPU part of a reset. Peripherals reset their own registers.</summary>
        public void Reset(int initialStackPointer)
        {
            PC = 0;
            sreg = 0;
            sp = initialStackPointer & 0xFFFF;
            pendingInterrupts = 0;
            interruptInhibit = false;
            sleeping = false;
        }

        // ------------------------------------------------------------------ peripherals

        public void SetReadHook(int address, IoReadHandler handler) => readHooks[address] = handler;

        public void SetWriteHook(int address, IoWriteHandler handler) => writeHooks[address] = handler;

        public void SetInterruptPending(int vector, bool pending)
        {
            ulong bit = 1UL << vector;
            if (pending) pendingInterrupts |= bit;
            else pendingInterrupts &= ~bit;
        }

        /// <summary>Called when the CPU enters the vector (hardware clears the flag for most sources).</summary>
        public void SetInterruptAcknowledge(int vector, Action handler) => interruptAcknowledge[vector] = handler;

        public int RegisterEventSource(Action<long> handler)
        {
            if (eventCount == MaxEvents) throw new InvalidOperationException("Too many event sources.");
            eventHandlers[eventCount] = handler;
            eventCycles[eventCount] = long.MaxValue;
            return eventCount++;
        }

        public void ScheduleEvent(int id, long cycle)
        {
            eventCycles[id] = cycle;
            if (cycle < nextEventCycle) nextEventCycle = cycle;
        }

        public void CancelEvent(int id) => eventCycles[id] = long.MaxValue;

        void RunDueEvents()
        {
            bool fired;
            do
            {
                fired = false;
                long now = Cycles;
                for (int i = 0; i < eventCount; i++)
                {
                    long cycle = eventCycles[i];
                    if (cycle <= now)
                    {
                        eventCycles[i] = long.MaxValue;
                        eventHandlers[i]!(cycle);
                        fired = true;
                    }
                }
            } while (fired);

            long min = long.MaxValue;
            for (int i = 0; i < eventCount; i++)
                if (eventCycles[i] < min) min = eventCycles[i];
            nextEventCycle = min;
        }

        // ------------------------------------------------------------------ data space

        public byte ReadData(int address)
        {
            if (address >= 0x100) return address < data.Length ? data[address] : (byte)0;
            if (address < 0x20) return data[address];
            return ReadIo(address);
        }

        public void WriteData(int address, byte value)
        {
            if (address >= 0x100)
            {
                if (address < data.Length) data[address] = value;
                return;
            }
            if (address < 0x20)
            {
                data[address] = value;
                return;
            }
            WriteIo(address, value, 0xFF);
        }

        byte ReadIo(int address)
        {
            switch (address)
            {
                case AddressSreg: return sreg;
                case AddressSpl: return (byte)sp;
                case AddressSph: return (byte)(sp >> 8);
            }
            var hook = readHooks[address];
            return hook != null ? hook(address) : data[address];
        }

        void WriteIo(int address, byte value, byte mask)
        {
            switch (address)
            {
                case AddressSreg: sreg = value; return;
                case AddressSpl: sp = (sp & 0xFF00) | value; return;
                case AddressSph: sp = (sp & 0x00FF) | (value << 8); return;
            }
            var hook = writeHooks[address];
            if (hook != null) hook(address, value, mask);
            else data[address] = value;
        }

        void Push16(int value)
        {
            WriteData(sp, (byte)value);
            sp = (sp - 1) & 0xFFFF;
            WriteData(sp, (byte)(value >> 8));
            sp = (sp - 1) & 0xFFFF;
        }

        int Pop16()
        {
            sp = (sp + 1) & 0xFFFF;
            int high = ReadData(sp);
            sp = (sp + 1) & 0xFFFF;
            int low = ReadData(sp);
            return (high << 8) | low;
        }

        // ------------------------------------------------------------------ execution

        /// <summary>Executes until <see cref="Cycles"/> reaches <paramref name="targetCycle"/>.</summary>
        public void Run(long targetCycle)
        {
            var d = data;
            var dec = decoded;
            int mask = flashWordMask;

            while (Cycles < targetCycle)
            {
                if (Cycles >= nextEventCycle) RunDueEvents();

                if (interruptInhibit)
                {
                    interruptInhibit = false;
                }
                else if (pendingInterrupts != 0 && (sreg & FlagI) != 0)
                {
                    ServiceInterrupt();
                    continue;
                }

                if (sleeping)
                {
                    long wake = nextEventCycle < targetCycle ? nextEventCycle : targetCycle;
                    Cycles = wake > Cycles ? wake : Cycles + 1;
                    continue;
                }

                PC &= mask; // the program counter wraps around the end of flash, as on the chip
                ref AvrInstruction ins = ref dec[PC];
                switch (ins.Op)
                {
                    // ---------------------------------------------------- arithmetic and logic
                    case AvrOp.Add:
                    {
                        int a = d[ins.D], b = d[ins.R], r = a + b;
                        d[ins.D] = (byte)r;
                        sreg = FlagsAdd(sreg, a, b, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Adc:
                    {
                        int a = d[ins.D], b = d[ins.R], r = a + b + (sreg & FlagC);
                        d[ins.D] = (byte)r;
                        sreg = FlagsAdd(sreg, a, b, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Sub:
                    {
                        int a = d[ins.D], b = d[ins.R], r = a - b;
                        d[ins.D] = (byte)r;
                        sreg = FlagsSub(sreg, a, b, r, false);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Sbc:
                    {
                        int a = d[ins.D], b = d[ins.R], r = a - b - (sreg & FlagC);
                        d[ins.D] = (byte)r;
                        sreg = FlagsSub(sreg, a, b, r, true);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Subi:
                    {
                        int a = d[ins.D], b = ins.K, r = a - b;
                        d[ins.D] = (byte)r;
                        sreg = FlagsSub(sreg, a, b, r, false);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Sbci:
                    {
                        int a = d[ins.D], b = ins.K, r = a - b - (sreg & FlagC);
                        d[ins.D] = (byte)r;
                        sreg = FlagsSub(sreg, a, b, r, true);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Cp:
                    {
                        int a = d[ins.D], b = d[ins.R];
                        sreg = FlagsSub(sreg, a, b, a - b, false);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Cpc:
                    {
                        int a = d[ins.D], b = d[ins.R];
                        sreg = FlagsSub(sreg, a, b, a - b - (sreg & FlagC), true);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Cpi:
                    {
                        int a = d[ins.D], b = ins.K;
                        sreg = FlagsSub(sreg, a, b, a - b, false);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.And:
                    {
                        int r = d[ins.D] & d[ins.R];
                        d[ins.D] = (byte)r;
                        sreg = FlagsLogic(sreg, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Andi:
                    {
                        int r = d[ins.D] & ins.K;
                        d[ins.D] = (byte)r;
                        sreg = FlagsLogic(sreg, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Or:
                    {
                        int r = d[ins.D] | d[ins.R];
                        d[ins.D] = (byte)r;
                        sreg = FlagsLogic(sreg, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Ori:
                    {
                        int r = d[ins.D] | ins.K;
                        d[ins.D] = (byte)r;
                        sreg = FlagsLogic(sreg, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Eor:
                    {
                        int r = d[ins.D] ^ d[ins.R];
                        d[ins.D] = (byte)r;
                        sreg = FlagsLogic(sreg, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Com:
                    {
                        int r = ~d[ins.D] & 0xFF;
                        d[ins.D] = (byte)r;
                        sreg = (byte)(FlagsLogic(sreg, r) | FlagC);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Neg:
                    {
                        int a = d[ins.D], r = (-a) & 0xFF;
                        d[ins.D] = (byte)r;
                        int f = sreg & (FlagI | FlagT);
                        if (((r | a) & 0x08) != 0) f |= FlagH;
                        if (r == 0x80) f |= FlagV;
                        if (r != 0) f |= FlagC;
                        sreg = WithNzs(f, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Inc:
                    {
                        int r = (d[ins.D] + 1) & 0xFF;
                        d[ins.D] = (byte)r;
                        int f = sreg & (FlagI | FlagT | FlagH | FlagC);
                        if (r == 0x80) f |= FlagV;
                        sreg = WithNzs(f, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Dec:
                    {
                        int r = (d[ins.D] - 1) & 0xFF;
                        d[ins.D] = (byte)r;
                        int f = sreg & (FlagI | FlagT | FlagH | FlagC);
                        if (r == 0x7F) f |= FlagV;
                        sreg = WithNzs(f, r);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Asr:
                    {
                        int a = d[ins.D], r = (a >> 1) | (a & 0x80);
                        d[ins.D] = (byte)r;
                        sreg = FlagsShiftRight(sreg, r, a & 1);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Lsr:
                    {
                        int a = d[ins.D], r = a >> 1;
                        d[ins.D] = (byte)r;
                        sreg = FlagsShiftRight(sreg, r, a & 1);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Ror:
                    {
                        int a = d[ins.D], r = (a >> 1) | ((sreg & FlagC) << 7);
                        d[ins.D] = (byte)r;
                        sreg = FlagsShiftRight(sreg, r, a & 1);
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Swap:
                    {
                        int a = d[ins.D];
                        d[ins.D] = (byte)((a << 4) | (a >> 4));
                        PC++; Cycles++;
                        break;
                    }
                    case AvrOp.Adiw:
                    {
                        int lo = ins.D, w = d[lo] | (d[lo + 1] << 8), r = (w + ins.K) & 0xFFFF;
                        d[lo] = (byte)r;
                        d[lo + 1] = (byte)(r >> 8);
                        int f = sreg & (FlagI | FlagT | FlagH);
                        bool rdh7 = (w & 0x8000) != 0, r15 = (r & 0x8000) != 0;
                        if (!rdh7 && r15) f |= FlagV;
                        if (r15) f |= FlagN;
                        if (r == 0) f |= FlagZ;
                        if (!r15 && rdh7) f |= FlagC;
                        sreg = WithSign(f);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.Sbiw:
                    {
                        int lo = ins.D, w = d[lo] | (d[lo + 1] << 8), r = (w - ins.K) & 0xFFFF;
                        d[lo] = (byte)r;
                        d[lo + 1] = (byte)(r >> 8);
                        int f = sreg & (FlagI | FlagT | FlagH);
                        bool rdh7 = (w & 0x8000) != 0, r15 = (r & 0x8000) != 0;
                        if (rdh7 && !r15) f |= FlagV;
                        if (r15) f |= FlagN;
                        if (r == 0) f |= FlagZ;
                        if (r15 && !rdh7) f |= FlagC;
                        sreg = WithSign(f);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.Mul:
                        SetProduct(d[ins.D] * d[ins.R], false);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.Muls:
                        SetProduct((sbyte)d[ins.D] * (sbyte)d[ins.R], false);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.Mulsu:
                        SetProduct((sbyte)d[ins.D] * d[ins.R], false);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.Fmul:
                        SetProduct(d[ins.D] * d[ins.R], true);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.Fmuls:
                        SetProduct((sbyte)d[ins.D] * (sbyte)d[ins.R], true);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.Fmulsu:
                        SetProduct((sbyte)d[ins.D] * d[ins.R], true);
                        PC++; Cycles += 2;
                        break;

                    // ---------------------------------------------------- data transfer
                    case AvrOp.Mov:
                        d[ins.D] = d[ins.R];
                        PC++; Cycles++;
                        break;
                    case AvrOp.Movw:
                        d[ins.D] = d[ins.R];
                        d[ins.D + 1] = d[ins.R + 1];
                        PC++; Cycles++;
                        break;
                    case AvrOp.Ldi:
                        d[ins.D] = (byte)ins.K;
                        PC++; Cycles++;
                        break;
                    case AvrOp.In:
                        d[ins.D] = ReadIo(ins.K + 0x20);
                        PC++; Cycles++;
                        break;
                    case AvrOp.Out:
                        WriteIo(ins.K + 0x20, d[ins.D], 0xFF);
                        PC++; Cycles++;
                        break;
                    case AvrOp.Lds:
                        d[ins.D] = ReadData(ins.K);
                        PC += 2; Cycles += 2;
                        break;
                    case AvrOp.Sts:
                        WriteData(ins.K, d[ins.D]);
                        PC += 2; Cycles += 2;
                        break;
                    case AvrOp.LdX:
                        d[ins.D] = ReadData(d[26] | (d[27] << 8));
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.LdXInc:
                    {
                        int x = d[26] | (d[27] << 8);
                        d[ins.D] = ReadData(x);
                        x++;
                        d[26] = (byte)x; d[27] = (byte)(x >> 8);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.LdXDec:
                    {
                        int x = ((d[26] | (d[27] << 8)) - 1) & 0xFFFF;
                        d[26] = (byte)x; d[27] = (byte)(x >> 8);
                        d[ins.D] = ReadData(x);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.LdYInc:
                    {
                        int y = d[28] | (d[29] << 8);
                        d[ins.D] = ReadData(y);
                        y++;
                        d[28] = (byte)y; d[29] = (byte)(y >> 8);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.LdYDec:
                    {
                        int y = ((d[28] | (d[29] << 8)) - 1) & 0xFFFF;
                        d[28] = (byte)y; d[29] = (byte)(y >> 8);
                        d[ins.D] = ReadData(y);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.LdZInc:
                    {
                        int z = d[30] | (d[31] << 8);
                        d[ins.D] = ReadData(z);
                        z++;
                        d[30] = (byte)z; d[31] = (byte)(z >> 8);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.LdZDec:
                    {
                        int z = ((d[30] | (d[31] << 8)) - 1) & 0xFFFF;
                        d[30] = (byte)z; d[31] = (byte)(z >> 8);
                        d[ins.D] = ReadData(z);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.LddY:
                        d[ins.D] = ReadData((d[28] | (d[29] << 8)) + ins.K);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.LddZ:
                        d[ins.D] = ReadData((d[30] | (d[31] << 8)) + ins.K);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.StX:
                        WriteData(d[26] | (d[27] << 8), d[ins.D]);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.StXInc:
                    {
                        int x = d[26] | (d[27] << 8);
                        WriteData(x, d[ins.D]);
                        x++;
                        d[26] = (byte)x; d[27] = (byte)(x >> 8);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.StXDec:
                    {
                        int x = ((d[26] | (d[27] << 8)) - 1) & 0xFFFF;
                        d[26] = (byte)x; d[27] = (byte)(x >> 8);
                        WriteData(x, d[ins.D]);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.StYInc:
                    {
                        int y = d[28] | (d[29] << 8);
                        WriteData(y, d[ins.D]);
                        y++;
                        d[28] = (byte)y; d[29] = (byte)(y >> 8);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.StYDec:
                    {
                        int y = ((d[28] | (d[29] << 8)) - 1) & 0xFFFF;
                        d[28] = (byte)y; d[29] = (byte)(y >> 8);
                        WriteData(y, d[ins.D]);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.StZInc:
                    {
                        int z = d[30] | (d[31] << 8);
                        WriteData(z, d[ins.D]);
                        z++;
                        d[30] = (byte)z; d[31] = (byte)(z >> 8);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.StZDec:
                    {
                        int z = ((d[30] | (d[31] << 8)) - 1) & 0xFFFF;
                        d[30] = (byte)z; d[31] = (byte)(z >> 8);
                        WriteData(z, d[ins.D]);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.StdY:
                        WriteData((d[28] | (d[29] << 8)) + ins.K, d[ins.D]);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.StdZ:
                        WriteData((d[30] | (d[31] << 8)) + ins.K, d[ins.D]);
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.LpmR0:
                    case AvrOp.ElpmR0:
                        d[0] = flashBytes[(d[30] | (d[31] << 8)) & flashByteMask];
                        PC++; Cycles += 3;
                        break;
                    case AvrOp.LpmZ:
                    case AvrOp.ElpmZ:
                        d[ins.D] = flashBytes[(d[30] | (d[31] << 8)) & flashByteMask];
                        PC++; Cycles += 3;
                        break;
                    case AvrOp.LpmZInc:
                    case AvrOp.ElpmZInc:
                    {
                        int z = d[30] | (d[31] << 8);
                        d[ins.D] = flashBytes[z & flashByteMask];
                        z++;
                        d[30] = (byte)z; d[31] = (byte)(z >> 8);
                        PC++; Cycles += 3;
                        break;
                    }
                    case AvrOp.Push:
                        WriteData(sp, d[ins.D]);
                        sp = (sp - 1) & 0xFFFF;
                        PC++; Cycles += 2;
                        break;
                    case AvrOp.Pop:
                        sp = (sp + 1) & 0xFFFF;
                        d[ins.D] = ReadData(sp);
                        PC++; Cycles += 2;
                        break;

                    // ---------------------------------------------------- flow control
                    case AvrOp.Rjmp:
                        PC = (PC + 1 + ins.K) & mask;
                        Cycles += 2;
                        break;
                    case AvrOp.Rcall:
                        Push16(PC + 1);
                        PC = (PC + 1 + ins.K) & mask;
                        Cycles += 3;
                        break;
                    case AvrOp.Jmp:
                        PC = ins.K & mask;
                        Cycles += 3;
                        break;
                    case AvrOp.Call:
                        Push16(PC + 2);
                        PC = ins.K & mask;
                        Cycles += 4;
                        break;
                    case AvrOp.Ijmp:
                    case AvrOp.Eijmp:
                        PC = (d[30] | (d[31] << 8)) & mask;
                        Cycles += 2;
                        break;
                    case AvrOp.Icall:
                    case AvrOp.Eicall:
                        Push16(PC + 1);
                        PC = (d[30] | (d[31] << 8)) & mask;
                        Cycles += 3;
                        break;
                    case AvrOp.Ret:
                        PC = Pop16() & mask;
                        Cycles += 4;
                        break;
                    case AvrOp.Reti:
                        PC = Pop16() & mask;
                        sreg |= FlagI;
                        interruptInhibit = true;
                        Cycles += 4;
                        break;
                    case AvrOp.Brbs:
                        if ((sreg & (1 << ins.R)) != 0)
                        {
                            PC = (PC + 1 + ins.K) & mask;
                            Cycles += 2;
                        }
                        else
                        {
                            PC++; Cycles++;
                        }
                        break;
                    case AvrOp.Brbc:
                        if ((sreg & (1 << ins.R)) == 0)
                        {
                            PC = (PC + 1 + ins.K) & mask;
                            Cycles += 2;
                        }
                        else
                        {
                            PC++; Cycles++;
                        }
                        break;
                    case AvrOp.Cpse:
                        if (d[ins.D] == d[ins.R]) SkipNext();
                        else { PC++; Cycles++; }
                        break;
                    case AvrOp.Sbrc:
                        if ((d[ins.D] & (1 << ins.R)) == 0) SkipNext();
                        else { PC++; Cycles++; }
                        break;
                    case AvrOp.Sbrs:
                        if ((d[ins.D] & (1 << ins.R)) != 0) SkipNext();
                        else { PC++; Cycles++; }
                        break;
                    case AvrOp.Sbic:
                        if ((ReadIo(ins.D + 0x20) & (1 << ins.R)) == 0) SkipNext();
                        else { PC++; Cycles++; }
                        break;
                    case AvrOp.Sbis:
                        if ((ReadIo(ins.D + 0x20) & (1 << ins.R)) != 0) SkipNext();
                        else { PC++; Cycles++; }
                        break;

                    // ---------------------------------------------------- bit operations
                    case AvrOp.Sbi:
                    {
                        int address = ins.D + 0x20;
                        byte bit = (byte)(1 << ins.R);
                        WriteIo(address, (byte)(ReadIo(address) | bit), bit);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.Cbi:
                    {
                        int address = ins.D + 0x20;
                        byte bit = (byte)(1 << ins.R);
                        WriteIo(address, (byte)(ReadIo(address) & ~bit), bit);
                        PC++; Cycles += 2;
                        break;
                    }
                    case AvrOp.Bset:
                        sreg |= (byte)(1 << ins.D);
                        if (ins.D == 7) interruptInhibit = true;
                        PC++; Cycles++;
                        break;
                    case AvrOp.Bclr:
                        sreg &= (byte)~(1 << ins.D);
                        PC++; Cycles++;
                        break;
                    case AvrOp.Bst:
                        if ((d[ins.D] & (1 << ins.R)) != 0) sreg |= FlagT;
                        else sreg &= unchecked((byte)~FlagT);
                        PC++; Cycles++;
                        break;
                    case AvrOp.Bld:
                        if ((sreg & FlagT) != 0) d[ins.D] |= (byte)(1 << ins.R);
                        else d[ins.D] &= (byte)~(1 << ins.R);
                        PC++; Cycles++;
                        break;

                    // ---------------------------------------------------- MCU control
                    case AvrOp.Nop:
                    case AvrOp.Break:
                        PC++; Cycles++;
                        break;
                    case AvrOp.Sleep:
                        if (SleepControlAddress < 0 || (ReadData(SleepControlAddress) & 1) != 0) sleeping = true;
                        PC++; Cycles++;
                        break;
                    case AvrOp.Wdr:
                        WatchdogReset?.Invoke();
                        PC++; Cycles++;
                        break;
                    case AvrOp.Spm:
                        Report($"SPM at 0x{PC * 2:X4} is not supported (self-programming).");
                        PC++; Cycles++;
                        break;
                    default:
                        Report($"Invalid instruction 0x{flashWords[PC]:X4} at 0x{PC * 2:X4} executed as NOP.");
                        PC++; Cycles++;
                        break;
                }
            }

            // Leave peripherals consistent with Cycles, so reads between runs see every due event.
            if (Cycles >= nextEventCycle) RunDueEvents();
        }

        void SkipNext()
        {
            int next = (PC + 1) & flashWordMask;
            int length = AvrDecoder.IsTwoWord(flashWords[next]) ? 2 : 1;
            PC = (PC + 1 + length) & flashWordMask;
            Cycles += 1 + length;
        }

        void ServiceInterrupt()
        {
            ulong pending = pendingInterrupts;
            int vector = 0;
            while ((pending & 1) == 0)
            {
                pending >>= 1;
                vector++;
            }

            sleeping = false;
            Push16(PC);
            sreg &= unchecked((byte)~FlagI);
            PC = (vector * 2) & flashWordMask;
            Cycles += 4;
            InterruptsServiced++;
            if (vector < interruptAcknowledge.Length) interruptAcknowledge[vector]?.Invoke();
        }

        void SetProduct(int product, bool fractional)
        {
            int p16 = product & 0xFFFF;
            int f = sreg & ~(FlagC | FlagZ);
            if ((p16 & 0x8000) != 0) f |= FlagC;
            int result = fractional ? (p16 << 1) & 0xFFFF : p16;
            if (result == 0) f |= FlagZ;
            data[0] = (byte)result;
            data[1] = (byte)(result >> 8);
            sreg = (byte)f;
        }

        void Report(string message) => Diagnostic?.Invoke(message);

        // ------------------------------------------------------------------ flag helpers

        static byte FlagsAdd(byte sreg, int a, int b, int result)
        {
            int r = result & 0xFF;
            int f = sreg & (FlagI | FlagT);
            int carries = (a & b) | (b & ~r) | (~r & a);
            if ((carries & 0x08) != 0) f |= FlagH;
            if ((carries & 0x80) != 0) f |= FlagC;
            if ((((a & b & ~r) | (~a & ~b & r)) & 0x80) != 0) f |= FlagV;
            return WithNzs(f, r);
        }

        static byte FlagsSub(byte sreg, int a, int b, int result, bool keepZero)
        {
            int r = result & 0xFF;
            int f = sreg & (FlagI | FlagT);
            int borrows = (~a & b) | (b & r) | (r & ~a);
            if ((borrows & 0x08) != 0) f |= FlagH;
            if ((borrows & 0x80) != 0) f |= FlagC;
            if ((((a & ~b & ~r) | (~a & b & r)) & 0x80) != 0) f |= FlagV;
            if ((r & 0x80) != 0) f |= FlagN;
            if (r == 0 && (!keepZero || (sreg & FlagZ) != 0)) f |= FlagZ;
            return WithSign(f);
        }

        static byte FlagsLogic(byte sreg, int r)
        {
            int f = sreg & (FlagI | FlagT | FlagH | FlagC);
            return WithNzs(f, r);
        }

        static byte FlagsShiftRight(byte sreg, int r, int carryOut)
        {
            int f = sreg & (FlagI | FlagT | FlagH);
            if (carryOut != 0) f |= FlagC;
            if ((r & 0x80) != 0) f |= FlagN;
            if ((((f >> 2) ^ f) & 1) != 0) f |= FlagV; // V = N xor C
            if (r == 0) f |= FlagZ;
            return WithSign(f);
        }

        /// <summary>Sets N and Z from an 8-bit result, then S = N xor V.</summary>
        static byte WithNzs(int f, int r)
        {
            if ((r & 0x80) != 0) f |= FlagN;
            if ((r & 0xFF) == 0) f |= FlagZ;
            return WithSign(f);
        }

        static byte WithSign(int f)
        {
            if ((((f >> 2) ^ (f >> 3)) & 1) != 0) f |= FlagS;
            return (byte)f;
        }
    }
}
