using System;

namespace CoreEngine.Sim.Avr
{
    /// <summary>Register addresses, interrupt vectors and output pins of one Timer/Counter.</summary>
    public sealed class TimerConfig
    {
        /// <summary>
        /// A 16-bit timer (Timer1): TCNT, OCRA, OCRB and ICR are byte pairs, low byte first, written through the
        /// TEMP byte (high byte first, then low) and TCNT and ICR read through it (low byte first).
        /// </summary>
        public bool Wide;

        public int TccrA, TccrB, TccrC = -1, Tcnt, OcrA, OcrB, Icr = -1, Timsk, Tifr;
        public int VectorCompareA, VectorCompareB, VectorOverflow, VectorCapture = -1;

        /// <summary>Clock divisor for each CS[2:0] value; 0 means stopped or external clock (not modelled).</summary>
        public int[] Divisors = Array.Empty<int>();

        /// <summary>The pins OCnA and OCnB drive when their COM bits connect them (null: no pin).</summary>
        public AvrPort? PortA, PortB;
        public int BitA, BitB;
    }

    /// <summary>
    /// A Timer/Counter of the ATmega328P: 8-bit Timer0 and Timer2, 16-bit Timer1 (docs/05 §4). It counts in
    /// normal, CTC, fast PWM, phase-correct and (Timer1) phase-and-frequency-correct modes, with TOP fixed, from
    /// OCRnA or from ICR1; sets TOV, OCFA, OCFB and ICF with their interrupts; and its output-compare units
    /// drive the OCnA and OCnB pins, which is what analogWrite and tone use:
    /// <list type="bullet">
    /// <item>normal and CTC: COM 01 toggles the pin on a compare match, 10 clears it, 11 sets it;</item>
    /// <item>fast PWM: COM 10 clears on a match and sets at BOTTOM (11 the other way round), so the pin is high
    /// for OCR of every TOP + 1 ticks; OCR at TOP keeps it high;</item>
    /// <item>phase-correct: COM 10 clears on the match counting up and sets on the one counting down, so the pin
    /// is high for OCR of every TOP ticks; OCR at TOP keeps it high, at 0 low;</item>
    /// <item>COM 01 in a PWM mode toggles OCnA only where OCRnA sets TOP (and ICR1 in Timer1's fast mode 14).</item>
    /// </list>
    /// The counter is worked out from the cycle count when read; the next flag-setting tick is scheduled as a CPU
    /// event, so the timer costs nothing between events. Not modelled yet: OCR double buffering in PWM modes (a
    /// new duty starts at once rather than at TOP), input capture from the ICP1 pin, external clocks,
    /// asynchronous Timer2, force-output-compare strobes.
    /// </summary>
    public sealed class AvrTimer
    {
        public const int FlagOverflow = 0x01, FlagCompareA = 0x02, FlagCompareB = 0x04, FlagCapture = 0x20;

        enum Kind { Normal, Ctc, Fast, PhaseCorrect, PhaseFrequency }

        enum TopFrom { Fixed, OcrA, Icr }

        readonly AvrCpu cpu;
        readonly TimerConfig config;
        readonly int eventId;
        readonly int max;       // 0xFF or 0xFFFF
        readonly int flagMask;  // the flag bits this timer has

        byte tccrA, tccrB, timsk, tifr, temp;
        int ocrA, ocrB, icr;
        int count;
        bool countingDown;
        int divisor;
        long baseTick;
        long scheduledTick = -1;
        int scheduledFlags;
        bool latchA, latchB;    // the output-compare latches OCnA and OCnB

        // The counting mode, worked out when TCCRnA or TCCRnB is written.
        Kind kind;
        TopFrom topFrom;
        int fixedTop;

        public AvrTimer(AvrCpu cpu, string name, TimerConfig config)
        {
            this.cpu = cpu;
            this.config = config;
            Name = name;
            max = config.Wide ? 0xFFFF : 0xFF;
            flagMask = config.VectorCapture >= 0 ? 0x27 : 0x07;
            eventId = cpu.RegisterEventSource(OnEvent);

            cpu.SetReadHook(config.TccrA, _ => tccrA);
            cpu.SetWriteHook(config.TccrA, (_, v, _) => WriteControl(v, tccrB));
            cpu.SetReadHook(config.TccrB, _ => tccrB);
            cpu.SetWriteHook(config.TccrB, (_, v, _) => WriteControl(tccrA, v));
            if (config.TccrC >= 0)
            {
                cpu.SetReadHook(config.TccrC, _ => 0); // FOC1A/FOC1B are write-only strobes
                cpu.SetWriteHook(config.TccrC, (_, _, _) => { });
            }
            cpu.SetReadHook(config.Timsk, _ => timsk);
            cpu.SetWriteHook(config.Timsk, (_, v, _) => { timsk = (byte)(v & flagMask); UpdateInterrupts(); });
            cpu.SetReadHook(config.Tifr, _ => tifr);
            cpu.SetWriteHook(config.Tifr, (_, v, mask) => { tifr &= (byte)~(v & mask & flagMask); UpdateInterrupts(); });

            if (config.Wide)
            {
                // TCNT1 and ICR1 read through TEMP: the low byte first latches the high byte.
                cpu.SetReadHook(config.Tcnt, _ => { CatchUp(); temp = (byte)(count >> 8); return (byte)count; });
                cpu.SetReadHook(config.Tcnt + 1, _ => temp);
                cpu.SetWriteHook(config.Tcnt + 1, (_, v, _) => temp = v);
                cpu.SetWriteHook(config.Tcnt, (_, v, _) => { CatchUp(); count = (temp << 8) | v; Reschedule(); });
                cpu.SetReadHook(config.OcrA, _ => (byte)ocrA);
                cpu.SetReadHook(config.OcrA + 1, _ => (byte)(ocrA >> 8));
                cpu.SetWriteHook(config.OcrA + 1, (_, v, _) => temp = v);
                cpu.SetWriteHook(config.OcrA, (_, v, _) => { CatchUp(); ocrA = (temp << 8) | v; Reschedule(); });
                cpu.SetReadHook(config.OcrB, _ => (byte)ocrB);
                cpu.SetReadHook(config.OcrB + 1, _ => (byte)(ocrB >> 8));
                cpu.SetWriteHook(config.OcrB + 1, (_, v, _) => temp = v);
                cpu.SetWriteHook(config.OcrB, (_, v, _) => { CatchUp(); ocrB = (temp << 8) | v; Reschedule(); });
                cpu.SetReadHook(config.Icr, _ => { temp = (byte)(icr >> 8); return (byte)icr; });
                cpu.SetReadHook(config.Icr + 1, _ => temp);
                cpu.SetWriteHook(config.Icr + 1, (_, v, _) => temp = v);
                cpu.SetWriteHook(config.Icr, (_, v, _) => { CatchUp(); icr = (temp << 8) | v; Reschedule(); });
            }
            else
            {
                cpu.SetReadHook(config.Tcnt, _ => { CatchUp(); return (byte)count; });
                cpu.SetWriteHook(config.Tcnt, (_, v, _) => { CatchUp(); count = v; Reschedule(); });
                cpu.SetReadHook(config.OcrA, _ => (byte)ocrA);
                cpu.SetWriteHook(config.OcrA, (_, v, _) => { CatchUp(); ocrA = v; Reschedule(); });
                cpu.SetReadHook(config.OcrB, _ => (byte)ocrB);
                cpu.SetWriteHook(config.OcrB, (_, v, _) => { CatchUp(); ocrB = v; Reschedule(); });
            }

            cpu.SetInterruptAcknowledge(config.VectorOverflow, () => ClearFlag(FlagOverflow));
            cpu.SetInterruptAcknowledge(config.VectorCompareA, () => ClearFlag(FlagCompareA));
            cpu.SetInterruptAcknowledge(config.VectorCompareB, () => ClearFlag(FlagCompareB));
            if (config.VectorCapture >= 0) cpu.SetInterruptAcknowledge(config.VectorCapture, () => ClearFlag(FlagCapture));
            Decode();
        }

        public string Name { get; }

        /// <summary>Waveform generation mode: WGM[2:0] of an 8-bit timer, WGM1[3:0] of Timer1.</summary>
        public int Mode => config.Wide ? (tccrA & 0x03) | ((tccrB & 0x18) >> 1) : (tccrA & 0x03) | ((tccrB & 0x08) >> 1);

        public bool IsRunning => divisor != 0;

        public int CurrentCount
        {
            get
            {
                CatchUp();
                return count;
            }
        }

        /// <summary>The level of the OCnA or OCnB latch (what the pin shows while the COM bits connect it).</summary>
        public bool OutputLevel(bool channelA) => channelA ? latchA : latchB;

        public void Reset()
        {
            tccrA = tccrB = timsk = tifr = temp = 0;
            ocrA = ocrB = icr = 0;
            count = 0;
            countingDown = false;
            divisor = 0;
            baseTick = 0;
            scheduledTick = -1;
            latchA = latchB = false;
            cpu.CancelEvent(eventId);
            Decode();
            UpdateInterrupts();
            DrivePins(cpu.Cycles);
        }

        // ------------------------------------------------------------------ modes

        void Decode()
        {
            int mode = Mode;
            if (!config.Wide)
            {
                (kind, topFrom, fixedTop) = mode switch
                {
                    1 => (Kind.PhaseCorrect, TopFrom.Fixed, 0xFF),
                    2 => (Kind.Ctc, TopFrom.OcrA, 0),
                    3 => (Kind.Fast, TopFrom.Fixed, 0xFF),
                    5 => (Kind.PhaseCorrect, TopFrom.OcrA, 0),
                    7 => (Kind.Fast, TopFrom.OcrA, 0),
                    _ => (Kind.Normal, TopFrom.Fixed, 0xFF), // 0, and the reserved 4 and 6
                };
                return;
            }
            (kind, topFrom, fixedTop) = mode switch
            {
                1 => (Kind.PhaseCorrect, TopFrom.Fixed, 0x00FF),
                2 => (Kind.PhaseCorrect, TopFrom.Fixed, 0x01FF),
                3 => (Kind.PhaseCorrect, TopFrom.Fixed, 0x03FF),
                4 => (Kind.Ctc, TopFrom.OcrA, 0),
                5 => (Kind.Fast, TopFrom.Fixed, 0x00FF),
                6 => (Kind.Fast, TopFrom.Fixed, 0x01FF),
                7 => (Kind.Fast, TopFrom.Fixed, 0x03FF),
                8 => (Kind.PhaseFrequency, TopFrom.Icr, 0),
                9 => (Kind.PhaseFrequency, TopFrom.OcrA, 0),
                10 => (Kind.PhaseCorrect, TopFrom.Icr, 0),
                11 => (Kind.PhaseCorrect, TopFrom.OcrA, 0),
                12 => (Kind.Ctc, TopFrom.Icr, 0),
                14 => (Kind.Fast, TopFrom.Icr, 0),
                15 => (Kind.Fast, TopFrom.OcrA, 0),
                _ => (Kind.Normal, TopFrom.Fixed, 0xFFFF), // 0, and the reserved 13
            };
        }

        int Top => topFrom == TopFrom.OcrA ? ocrA : topFrom == TopFrom.Icr ? icr : fixedTop;

        /// <summary>Phase-correct and phase-and-frequency-correct modes count up to TOP and back down.</summary>
        bool TwoWay => kind == Kind.PhaseCorrect || kind == Kind.PhaseFrequency;

        void WriteControl(byte a, byte b)
        {
            CatchUp();
            tccrA = a;
            // TCCRnB: FOCnA/FOCnB of an 8-bit timer are write-only strobes; Timer1's bit 5 is reserved.
            tccrB = (byte)(config.Wide ? b & 0xDF : b & 0x0F);
            int oldDivisor = divisor;
            int cs = b & 0x07;
            divisor = cs < config.Divisors.Length ? config.Divisors[cs] : 0;
            if (divisor != oldDivisor) baseTick = divisor != 0 ? cpu.Cycles / divisor : 0;
            Decode();
            Reschedule();
            DrivePins(cpu.Cycles);
        }

        // ------------------------------------------------------------------ counting

        void CatchUp()
        {
            if (divisor == 0) return;
            long nowTick = cpu.Cycles / divisor;
            if (nowTick > baseTick)
            {
                Advance(nowTick - baseTick);
                baseTick = nowTick;
            }
        }

        void Advance(long ticks)
        {
            if (ticks <= 0) return;
            int top = Top;
            if (TwoWay)
            {
                if (top == 0)
                {
                    count = 0;
                    return;
                }
                if (count > top)
                {
                    count = top;
                    countingDown = true;
                }
                long period = 2L * top;
                long pos = countingDown ? period - count : count;
                pos = (pos + ticks) % period;
                if (pos <= top)
                {
                    count = (int)pos;
                    countingDown = false;
                }
                else
                {
                    count = (int)(period - pos);
                    countingDown = true;
                }
                return;
            }

            if (count > top)
            {
                // Above a lowered TOP the counter runs on to MAX and wraps.
                long toWrap = (long)max + 1 - count;
                if (ticks < toWrap)
                {
                    count += (int)ticks;
                    return;
                }
                ticks -= toWrap;
                count = 0;
            }
            count = (int)((count + ticks) % ((long)top + 1));
        }

        void Reschedule()
        {
            if (divisor == 0)
            {
                scheduledTick = -1;
                cpu.CancelEvent(eventId);
                return;
            }

            long best = long.MaxValue;
            int flags = 0;
            int top = Top;

            if (TwoWay)
            {
                if (top > 0)
                {
                    long period = 2L * top;
                    long pos = countingDown ? period - count : count;
                    Consider(ref best, ref flags, TicksToPosition(pos, 0, period), FlagOverflow); // TOV at BOTTOM
                    Consider(ref best, ref flags, TicksToValueTwoWay(pos, ocrA, top, period), FlagCompareA);
                    Consider(ref best, ref flags, TicksToValueTwoWay(pos, ocrB, top, period), FlagCompareB);
                    if (topFrom == TopFrom.Icr) Consider(ref best, ref flags, TicksToPosition(pos, top, period), FlagCapture); // ICF1 at TOP
                }
            }
            else if (count > top)
            {
                // After TOP was lowered below the counter: up to MAX and wrap (sets TOV; BOTTOM for fast PWM).
                Consider(ref best, ref flags, (long)max + 1 - count, FlagOverflow);
                if (ocrB > count && ocrB <= max) Consider(ref best, ref flags, ocrB - count, FlagCompareB);
            }
            else
            {
                long period = (long)top + 1;
                long wrap = period - count;
                // CTC sets TOV only when it counts through MAX; the other modes at every wrap.
                if (kind != Kind.Ctc || top == max) Consider(ref best, ref flags, wrap, FlagOverflow);
                if (topFrom == TopFrom.Icr) Consider(ref best, ref flags, wrap, FlagCapture); // ICF1 when ICR1 is TOP
                if (ocrA <= top) Consider(ref best, ref flags, TicksToValue(count, ocrA, period), FlagCompareA);
                if (ocrB <= top) Consider(ref best, ref flags, TicksToValue(count, ocrB, period), FlagCompareB);
            }

            if (best == long.MaxValue)
            {
                scheduledTick = -1;
                cpu.CancelEvent(eventId);
                return;
            }

            scheduledTick = baseTick + best;
            scheduledFlags = flags;
            cpu.ScheduleEvent(eventId, scheduledTick * divisor);
        }

        static void Consider(ref long best, ref int flags, long ticks, int flag)
        {
            if (ticks <= 0 || ticks == long.MaxValue) return;
            if (ticks < best)
            {
                best = ticks;
                flags = flag;
            }
            else if (ticks == best)
            {
                flags |= flag;
            }
        }

        /// <summary>Ticks until an up-counter reaches <paramref name="target"/> (a full period if it is there now).</summary>
        static long TicksToValue(int current, int target, long period)
        {
            long diff = ((target - current) % period + period) % period;
            return diff == 0 ? period : diff;
        }

        static long TicksToPosition(long position, long target, long period)
        {
            long diff = ((target - position) % period + period) % period;
            return diff == 0 ? period : diff;
        }

        static long TicksToValueTwoWay(long position, int value, int top, long period)
        {
            if (value > top) return long.MaxValue;
            long up = TicksToPosition(position, value, period);
            if (value == 0 || value == top) return up;
            long down = TicksToPosition(position, period - value, period);
            return Math.Min(up, down);
        }

        void OnEvent(long cycle)
        {
            if (scheduledTick < 0 || divisor == 0) return;
            long tick = scheduledTick;
            if (tick > baseTick)
            {
                Advance(tick - baseTick);
                baseTick = tick;
            }
            int flags = scheduledFlags;
            tifr |= (byte)(flags & flagMask);
            UpdateInterrupts();
            UpdateLatches(flags);
            DrivePins(cycle);
            Reschedule();
        }

        // ------------------------------------------------------------------ output compare

        int ComA => (tccrA >> 6) & 0x03;

        int ComB => (tccrA >> 4) & 0x03;

        /// <summary>Whether a channel's COM bits connect its pin in this mode.</summary>
        bool Connected(int com, bool channelA)
        {
            if (com == 0) return false;
            if (com != 1 || kind == Kind.Normal || kind == Kind.Ctc) return true;
            // COM 01 in a PWM mode: only OCnA toggles, and only where OCRnA (or Timer1's ICR1 in mode 14) sets TOP.
            if (!channelA) return false;
            if (!config.Wide) return topFrom == TopFrom.OcrA;
            int mode = Mode;
            return mode == 14 || mode == 15 || mode == 9 || mode == 11;
        }

        void UpdateLatches(int flags)
        {
            // Fast PWM sets (COM 10) or clears (COM 11) its outputs at BOTTOM, where it wraps; a match at the
            // same tick (OCR at 0) then clears them again.
            if (kind == Kind.Fast && (flags & FlagOverflow) != 0)
            {
                AtBottom(ComA, ref latchA);
                AtBottom(ComB, ref latchB);
            }
            if ((flags & FlagCompareA) != 0 && Connected(ComA, true)) AtMatch(ComA, ocrA, ref latchA);
            if ((flags & FlagCompareB) != 0 && Connected(ComB, false)) AtMatch(ComB, ocrB, ref latchB);
        }

        static void AtBottom(int com, ref bool latch)
        {
            if (com == 2) latch = true;
            else if (com == 3) latch = false;
        }

        void AtMatch(int com, int ocr, ref bool latch)
        {
            if (com == 1)
            {
                latch = !latch;
                return;
            }
            bool high = com == 3; // what a match sets it to in normal and CTC modes, and in fast PWM
            switch (kind)
            {
                case Kind.Fast:
                    if (ocr < Top) latch = high; // OCR at TOP keeps the pin as BOTTOM left it
                    break;
                case Kind.PhaseCorrect:
                case Kind.PhaseFrequency:
                    // Non-inverting: cleared counting up, set counting down; OCR at TOP keeps it high.
                    latch = ocr >= Top ? !high : (com == 2 ? countingDown : !countingDown);
                    break;
                default:
                    latch = high;
                    break;
            }
        }

        /// <summary>Gives each connected channel's pin its latch, and a disconnected one back to its port.</summary>
        void DrivePins(long cycle)
        {
            config.PortA?.SetAlternateOutput(config.BitA, Connected(ComA, true) ? latchA : (bool?)null, cycle);
            config.PortB?.SetAlternateOutput(config.BitB, Connected(ComB, false) ? latchB : (bool?)null, cycle);
        }

        // ------------------------------------------------------------------ interrupts

        void ClearFlag(int flag)
        {
            tifr &= (byte)~flag;
            UpdateInterrupts();
        }

        void UpdateInterrupts()
        {
            int active = tifr & timsk;
            cpu.SetInterruptPending(config.VectorOverflow, (active & FlagOverflow) != 0);
            cpu.SetInterruptPending(config.VectorCompareA, (active & FlagCompareA) != 0);
            cpu.SetInterruptPending(config.VectorCompareB, (active & FlagCompareB) != 0);
            if (config.VectorCapture >= 0) cpu.SetInterruptPending(config.VectorCapture, (active & FlagCapture) != 0);
        }
    }
}
