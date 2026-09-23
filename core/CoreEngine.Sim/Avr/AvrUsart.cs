using System.Collections.Generic;

namespace CoreEngine.Sim.Avr
{
    /// <summary>Register addresses and interrupt vectors of one USART.</summary>
    public sealed class UsartConfig
    {
        public int Udr, UcsrA, UcsrB, UcsrC, UbrrL, UbrrH;
        public int VectorReceive, VectorDataEmpty, VectorTransmit;
    }

    /// <summary>Signature of <see cref="AvrUsart.ByteTransmitted"/>.</summary>
    public delegate void UsartByteHandler(byte value, long startCycle, long endCycle);

    /// <summary>
    /// Asynchronous USART (ATmega328P USART0) with the transmit buffer + shift register pipeline,
    /// UDRE/TXC/RXC flags and interrupts, and exact frame timing from UBRR, U2X, data bits,
    /// parity and stop bits. Bytes leave through <see cref="ByteTransmitted"/> when their stop bit
    /// ends; the host feeds received bytes with <see cref="Receive"/>.
    /// Not modelled yet (Phase 1): bit-level TX/RX pin waveforms, synchronous and SPI-master modes,
    /// the two-level receive FIFO, parity/frame error injection, multi-processor mode.
    /// </summary>
    public sealed class AvrUsart
    {
        const byte Rxc = 0x80, Txc = 0x40, Udre = 0x20, Dor = 0x08, U2x = 0x02, Mpcm = 0x01;
        const byte RxcIe = 0x80, TxcIe = 0x40, UdrIe = 0x20, RxEn = 0x10, TxEn = 0x08, Ucsz2 = 0x04;

        readonly AvrCpu cpu;
        readonly UsartConfig config;
        readonly int txEventId;
        readonly int rxEventId;
        readonly Queue<byte> rxQueue = new Queue<byte>();

        byte ucsrA, ucsrB, ucsrC;
        int ubrr;
        bool txShifting;
        byte txShiftValue;
        long txFrameStart;
        bool txBufferFull;
        byte txBuffer;
        byte rxData;
        bool rxInFlight;

        public AvrUsart(AvrCpu cpu, string name, UsartConfig config)
        {
            this.cpu = cpu;
            this.config = config;
            Name = name;
            txEventId = cpu.RegisterEventSource(OnTransmitComplete);
            rxEventId = cpu.RegisterEventSource(OnReceiveComplete);

            cpu.SetReadHook(config.Udr, _ => ReadData());
            cpu.SetWriteHook(config.Udr, (_, v, _) => WriteData(v));
            cpu.SetReadHook(config.UcsrA, _ => ucsrA);
            cpu.SetWriteHook(config.UcsrA, (_, v, mask) => WriteStatus(v, mask));
            cpu.SetReadHook(config.UcsrB, _ => ucsrB);
            cpu.SetWriteHook(config.UcsrB, (_, v, _) => { ucsrB = v; UpdateInterrupts(); });
            cpu.SetReadHook(config.UcsrC, _ => ucsrC);
            cpu.SetWriteHook(config.UcsrC, (_, v, _) => ucsrC = v);
            cpu.SetReadHook(config.UbrrL, _ => (byte)ubrr);
            cpu.SetWriteHook(config.UbrrL, (_, v, _) => ubrr = (ubrr & 0xF00) | v);
            cpu.SetReadHook(config.UbrrH, _ => (byte)(ubrr >> 8));
            cpu.SetWriteHook(config.UbrrH, (_, v, _) => ubrr = (ubrr & 0x0FF) | ((v & 0x0F) << 8));

            cpu.SetInterruptAcknowledge(config.VectorTransmit, () =>
            {
                ucsrA &= unchecked((byte)~Txc);
                UpdateInterrupts();
            });
        }

        public string Name { get; }

        /// <summary>Raised when a byte's frame has been completely shifted out.</summary>
        public event UsartByteHandler? ByteTransmitted;

        /// <summary>Clock cycles per bit with the current UBRR and U2X settings.</summary>
        public long CyclesPerBit => ((ucsrA & U2x) != 0 ? 8L : 16L) * (ubrr + 1);

        /// <summary>Bits per frame: start + data + optional parity + stop bits.</summary>
        public int FrameBits
        {
            get
            {
                int size = ((ucsrC >> 1) & 0x03) | (ucsrB & Ucsz2);
                int dataBits = size switch { 0 => 5, 1 => 6, 2 => 7, 7 => 9, _ => 8 };
                int parity = (ucsrC & 0x20) != 0 ? 1 : 0;
                int stop = (ucsrC & 0x08) != 0 ? 2 : 1;
                return 1 + dataBits + parity + stop;
            }
        }

        public long FrameCycles => CyclesPerBit * FrameBits;

        /// <summary>Baud rate that the current settings really produce at the given clock.</summary>
        public double ActualBaud(double clockHz) => clockHz / CyclesPerBit;

        public void Reset()
        {
            ucsrA = Udre;
            ucsrB = 0;
            ucsrC = 0x06;
            ubrr = 0;
            txShifting = false;
            txBufferFull = false;
            rxInFlight = false;
            rxQueue.Clear();
            cpu.CancelEvent(txEventId);
            cpu.CancelEvent(rxEventId);
            UpdateInterrupts();
        }

        /// <summary>Queues bytes arriving on RXD, for example from the Serial Monitor.</summary>
        public void Receive(byte value)
        {
            rxQueue.Enqueue(value);
            if (!rxInFlight) StartNextReceive(cpu.Cycles);
        }

        void StartNextReceive(long startCycle)
        {
            if (rxQueue.Count == 0)
            {
                rxInFlight = false;
                return;
            }
            rxInFlight = true;
            cpu.ScheduleEvent(rxEventId, startCycle + FrameCycles);
        }

        void OnReceiveComplete(long cycle)
        {
            byte value = rxQueue.Dequeue();
            if ((ucsrB & RxEn) != 0)
            {
                if ((ucsrA & Rxc) != 0) ucsrA |= Dor; // previous byte not read yet: data overrun
                else
                {
                    rxData = value;
                    ucsrA |= Rxc;
                }
            }
            UpdateInterrupts();
            StartNextReceive(cycle);
        }

        byte ReadData()
        {
            byte value = rxData;
            ucsrA &= unchecked((byte)~(Rxc | Dor));
            UpdateInterrupts();
            return value;
        }

        void WriteData(byte value)
        {
            if ((ucsrB & TxEn) == 0) return; // transmitter disabled: the write is ignored
            if (!txShifting)
            {
                StartFrame(value, cpu.Cycles);
            }
            else if (!txBufferFull)
            {
                txBuffer = value;
                txBufferFull = true;
                ucsrA &= unchecked((byte)~Udre);
            }
            // Writes while UDRE is clear are ignored by the hardware.
            UpdateInterrupts();
        }

        void WriteStatus(byte value, byte mask)
        {
            ucsrA = (byte)((ucsrA & ~(U2x | Mpcm)) | (value & (U2x | Mpcm)));
            if ((value & mask & Txc) != 0) ucsrA &= unchecked((byte)~Txc); // TXC clears by writing one
            UpdateInterrupts();
        }

        void StartFrame(byte value, long startCycle)
        {
            txShifting = true;
            txShiftValue = value;
            txFrameStart = startCycle;
            cpu.ScheduleEvent(txEventId, startCycle + FrameCycles);
        }

        void OnTransmitComplete(long cycle)
        {
            ByteTransmitted?.Invoke(txShiftValue, txFrameStart, cycle);
            if (txBufferFull)
            {
                txBufferFull = false;
                ucsrA |= Udre;
                StartFrame(txBuffer, cycle);
            }
            else
            {
                txShifting = false;
                ucsrA |= Txc;
            }
            UpdateInterrupts();
        }

        void UpdateInterrupts()
        {
            cpu.SetInterruptPending(config.VectorReceive, (ucsrA & Rxc) != 0 && (ucsrB & RxcIe) != 0);
            cpu.SetInterruptPending(config.VectorDataEmpty, (ucsrA & Udre) != 0 && (ucsrB & UdrIe) != 0);
            cpu.SetInterruptPending(config.VectorTransmit, (ucsrA & Txc) != 0 && (ucsrB & TxcIe) != 0);
        }
    }
}
