namespace CoreEngine.Sim.Avr
{
    /// <summary>
    /// ATmega328P (Arduino Uno R3 / Nano) built from the CPU core and its peripherals, using the
    /// register addresses and interrupt vectors of the Microchip datasheet (DS40002061).
    /// Registers without a peripheral model yet (Timer1, ADC, SPI, TWI, EEPROM, watchdog, ...)
    /// behave as plain read/write storage; Phase 1 replaces them with full models.
    /// </summary>
    public sealed class Atmega328P
    {
        public const int FlashSize = 32 * 1024;
        public const int DataSize = 0x900;
        public const int RamEnd = 0x8FF;
        public const int VectorCount = 26;
        public const long ClockHz = 16_000_000;

        // Data addresses (I/O address + 0x20 for the low I/O space).
        public const int PINB = 0x23, PINC = 0x26, PIND = 0x29;
        public const int TIFR0 = 0x35, TIFR2 = 0x37;
        public const int TCCR0A = 0x44, TCCR0B = 0x45, TCNT0 = 0x46, OCR0A = 0x47, OCR0B = 0x48;
        public const int SMCR = 0x53, MCUSR = 0x54;
        public const int TIMSK0 = 0x6E, TIMSK2 = 0x70;
        public const int TCCR2A = 0xB0, TCCR2B = 0xB1, TCNT2 = 0xB2, OCR2A = 0xB3, OCR2B = 0xB4;
        public const int TWSR = 0xB9;
        public const int UCSR0A = 0xC0, UCSR0B = 0xC1, UCSR0C = 0xC2, UBRR0L = 0xC4, UBRR0H = 0xC5, UDR0 = 0xC6;

        // Interrupt vector numbers (avr-libc _VECTOR(n)).
        public const int VectorTimer2CompA = 7, VectorTimer2CompB = 8, VectorTimer2Ovf = 9;
        public const int VectorTimer0CompA = 14, VectorTimer0CompB = 15, VectorTimer0Ovf = 16;
        public const int VectorUsartRx = 18, VectorUsartUdre = 19, VectorUsartTx = 20;

        static readonly int[] Timer0Divisors = { 0, 1, 8, 64, 256, 1024, 0, 0 };
        static readonly int[] Timer2Divisors = { 0, 1, 8, 32, 64, 128, 256, 1024 };

        public Atmega328P()
        {
            Cpu = new AvrCpu(FlashSize, DataSize, VectorCount) { SleepControlAddress = SMCR };
            PortB = new AvrPort(Cpu, 'B', PINB);
            PortC = new AvrPort(Cpu, 'C', PINC);
            PortD = new AvrPort(Cpu, 'D', PIND);
            Timer0 = new AvrTimer8(Cpu, "Timer0", new Timer8Config
            {
                TccrA = TCCR0A, TccrB = TCCR0B, Tcnt = TCNT0, OcrA = OCR0A, OcrB = OCR0B,
                Timsk = TIMSK0, Tifr = TIFR0,
                VectorCompareA = VectorTimer0CompA, VectorCompareB = VectorTimer0CompB, VectorOverflow = VectorTimer0Ovf,
                Divisors = Timer0Divisors,
            });
            Timer2 = new AvrTimer8(Cpu, "Timer2", new Timer8Config
            {
                TccrA = TCCR2A, TccrB = TCCR2B, Tcnt = TCNT2, OcrA = OCR2A, OcrB = OCR2B,
                Timsk = TIMSK2, Tifr = TIFR2,
                VectorCompareA = VectorTimer2CompA, VectorCompareB = VectorTimer2CompB, VectorOverflow = VectorTimer2Ovf,
                Divisors = Timer2Divisors,
            });
            Usart0 = new AvrUsart(Cpu, "USART0", new UsartConfig
            {
                Udr = UDR0, UcsrA = UCSR0A, UcsrB = UCSR0B, UcsrC = UCSR0C, UbrrL = UBRR0L, UbrrH = UBRR0H,
                VectorReceive = VectorUsartRx, VectorDataEmpty = VectorUsartUdre, VectorTransmit = VectorUsartTx,
            });
            Reset();
        }

        public AvrCpu Cpu { get; }
        public AvrPort PortB { get; }
        public AvrPort PortC { get; }
        public AvrPort PortD { get; }
        public AvrTimer8 Timer0 { get; }
        public AvrTimer8 Timer2 { get; }
        public AvrUsart Usart0 { get; }

        /// <summary>Flash bytes used by the loaded program.</summary>
        public int ProgramSize { get; private set; }

        /// <summary>Emulated time since power-on.</summary>
        public double Seconds => Cpu.Cycles / (double)ClockHz;

        public void LoadHex(string hexText)
        {
            var image = IntelHex.Parse(hexText, FlashSize, out int used);
            ProgramSize = used;
            Cpu.LoadProgram(image);
            Reset();
        }

        /// <summary>
        /// Resets the MCU (power-on or reset pin). The Optiboot delay of a real board is not
        /// emulated here; the board model adds it (docs/05 §2).
        /// </summary>
        public void Reset()
        {
            var io = Cpu.DataSpace;
            for (int address = 0x20; address < 0x100; address++) io[address] = 0;
            io[TWSR] = 0xF8;
            io[MCUSR] = 0x01; // power-on reset flag
            PortB.Reset();
            PortC.Reset();
            PortD.Reset();
            Timer0.Reset();
            Timer2.Reset();
            Usart0.Reset();
            Cpu.Reset(RamEnd);
        }

        /// <summary>Runs for the given number of clock cycles.</summary>
        public void RunCycles(long cycles) => Cpu.Run(Cpu.Cycles + cycles);

        /// <summary>Runs for the given emulated time in seconds.</summary>
        public void RunSeconds(double seconds) => RunCycles((long)(seconds * ClockHz));

        /// <summary>Arduino pin name for a port bit: PD0-7 = D0-D7, PB0-5 = D8-D13, PC0-5 = A0-A5.</summary>
        public static string ArduinoPinName(char port, int bit)
        {
            switch (port)
            {
                case 'D': return "D" + bit;
                case 'B': return bit <= 5 ? "D" + (8 + bit) : "PB" + bit;
                case 'C': return bit <= 5 ? "A" + bit : "PC" + bit;
                default: return "P" + port + bit;
            }
        }
    }
}
