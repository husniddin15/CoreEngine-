using System;

namespace CoreEngine.Sim.Avr
{
    /// <summary>Register addresses and interrupt vectors of one 8-bit Timer/Counter.</summary>
    public sealed class Timer8Config
    {
        public int TccrA, TccrB, Tcnt, OcrA, OcrB, Timsk, Tifr;
        public int VectorCompareA, VectorCompareB, VectorOverflow;

        /// <summary>Clock divisor for each CS[2:0] value; 0 means stopped or external clock (not modelled).</summary>
        public int[] Divisors = Array.Empty<int>();
    }

    /// <summary>
    /// 8-bit Timer/Counter (Timer0/Timer2 of the ATmega328P): normal, CTC, fast PWM and
    /// phase-correct PWM counting with TOV/OCFA/OCFB flags and interrupts.
    /// The counter value is computed from the cycle count on demand; the next flag-setting tick is
    /// scheduled as a CPU event, so the timer costs nothing between events.
    /// Not modelled yet (Phase 1): output-compare pins, OCR double buffering in PWM modes,
    /// external clock inputs, asynchronous Timer2 operation, force-output-compare strobes.
    /// </summary>
    public sealed class AvrTimer8
    {
        public const int FlagOverflow = 0x01, FlagCompareA = 0x02, FlagCompareB = 0x04;

        readonly AvrCpu cpu;
        readonly Timer8Config config;
        readonly int eventId;

        byte tccrA, tccrB, ocrA, ocrB, timsk, tifr;
        int count;
        bool countingDown;
        int divisor;
        long baseTick;
        long scheduledTick = -1;
        int scheduledFlags;

        public AvrTimer8(AvrCpu cpu, string name, Timer8Config config)
        {
            this.cpu = cpu;
            this.config = config;
            Name = name;
            eventId = cpu.RegisterEventSource(OnEvent);

            cpu.SetReadHook(config.TccrA, _ => tccrA);
            cpu.SetWriteHook(config.TccrA, (_, v, _) => { CatchUp(); tccrA = v; Reschedule(); });
            cpu.SetReadHook(config.TccrB, _ => tccrB);
            cpu.SetWriteHook(config.TccrB, (_, v, _) => WriteControlB(v));
            cpu.SetReadHook(config.Tcnt, _ => { CatchUp(); return (byte)count; });
            cpu.SetWriteHook(config.Tcnt, (_, v, _) => { CatchUp(); count = v; Reschedule(); });
            cpu.SetReadHook(config.OcrA, _ => ocrA);
            cpu.SetWriteHook(config.OcrA, (_, v, _) => { CatchUp(); ocrA = v; Reschedule(); });
            cpu.SetReadHook(config.OcrB, _ => ocrB);
            cpu.SetWriteHook(config.OcrB, (_, v, _) => { CatchUp(); ocrB = v; Reschedule(); });
            cpu.SetReadHook(config.Timsk, _ => timsk);
            cpu.SetWriteHook(config.Timsk, (_, v, _) => { timsk = (byte)(v & 0x07); UpdateInterrupts(); });
            cpu.SetReadHook(config.Tifr, _ => tifr);
            cpu.SetWriteHook(config.Tifr, (_, v, mask) => { tifr &= (byte)~(v & mask & 0x07); UpdateInterrupts(); });

            cpu.SetInterruptAcknowledge(config.VectorOverflow, () => ClearFlag(FlagOverflow));
            cpu.SetInterruptAcknowledge(config.VectorCompareA, () => ClearFlag(FlagCompareA));
            cpu.SetInterruptAcknowledge(config.VectorCompareB, () => ClearFlag(FlagCompareB));
        }

        public string Name { get; }

        /// <summary>Waveform generation mode WGM[2:0].</summary>
        public int Mode => (tccrA & 0x03) | ((tccrB & 0x08) >> 1);

        public bool IsRunning => divisor != 0;

        public int CurrentCount
        {
            get
            {
                CatchUp();
                return count;
            }
        }

        public void Reset()
        {
            tccrA = tccrB = ocrA = ocrB = timsk = tifr = 0;
            count = 0;
            countingDown = false;
            divisor = 0;
            baseTick = 0;
            scheduledTick = -1;
            cpu.CancelEvent(eventId);
            UpdateInterrupts();
        }

        int Top
        {
            get
            {
                switch (Mode)
                {
                    case 2:
                    case 5:
                    case 7:
                        return ocrA;
                    default:
                        return 0xFF;
                }
            }
        }

        bool IsPhaseCorrect => Mode == 1 || Mode == 5;

        void WriteControlB(byte value)
        {
            CatchUp();
            int oldDivisor = divisor;
            tccrB = (byte)(value & 0x0F); // FOC0A/FOC0B are write-only strobes and read as zero
            int cs = value & 0x07;
            divisor = cs < config.Divisors.Length ? config.Divisors[cs] : 0;
            if (divisor != oldDivisor) baseTick = divisor != 0 ? cpu.Cycles / divisor : 0;
            Reschedule();
        }

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
            if (IsPhaseCorrect)
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
                long toWrap = 256 - count;
                if (ticks < toWrap)
                {
                    count += (int)ticks;
                    return;
                }
                ticks -= toWrap;
                count = 0;
            }
            count = (int)((count + ticks) % (top + 1));
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
            int mode = Mode;

            if (IsPhaseCorrect)
            {
                if (top > 0)
                {
                    long period = 2L * top;
                    long pos = countingDown ? period - count : count;
                    Consider(ref best, ref flags, TicksToPosition(pos, 0, period), FlagOverflow);
                    Consider(ref best, ref flags, TicksToValuePhaseCorrect(pos, ocrA, top, period), FlagCompareA);
                    Consider(ref best, ref flags, TicksToValuePhaseCorrect(pos, ocrB, top, period), FlagCompareB);
                }
            }
            else if (count > top)
            {
                // After OCRA was lowered below the counter: count up to 0xFF and wrap (sets TOV).
                Consider(ref best, ref flags, 256 - count, FlagOverflow);
                if (ocrB > count) Consider(ref best, ref flags, ocrB - count, FlagCompareB);
            }
            else
            {
                long period = top + 1;
                bool overflowAtWrap = mode != 2 || top == 0xFF;
                if (overflowAtWrap) Consider(ref best, ref flags, period - count, FlagOverflow);
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

        static long TicksToValuePhaseCorrect(long position, int value, int top, long period)
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
            tifr |= (byte)(scheduledFlags & 0x07);
            UpdateInterrupts();
            Reschedule();
        }

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
        }
    }
}
