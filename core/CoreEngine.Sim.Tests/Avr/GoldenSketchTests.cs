using System.Text;
using CoreEngine.Sim.Avr;

namespace CoreEngine.Sim.Tests.Avr;

/// <summary>
/// Real Arduino sketches compiled by the bundled toolchain (Golden/Sketches, rebuilt with
/// tools/build-golden.ps1) must behave like on an Uno (docs/05-arduino-emulation-spec.md §10, item 3).
/// </summary>
public class GoldenSketchTests
{
    const double CyclesPerMs = Atmega328P.ClockHz / 1000.0;

    static Atmega328P Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Golden", "Hex", name + ".hex");
        var mcu = new Atmega328P();
        mcu.LoadHex(File.ReadAllText(path));
        mcu.Cpu.Diagnostic += message => throw new Xunit.Sdk.XunitException("Emulator diagnostic: " + message);
        return mcu;
    }

    static (StringBuilder text, List<(byte value, long start, long end)> bytes) CaptureSerial(Atmega328P mcu)
    {
        var text = new StringBuilder();
        var bytes = new List<(byte, long, long)>();
        mcu.Usart0.ByteTransmitted += (value, start, end) =>
        {
            text.Append((char)value);
            bytes.Add((value, start, end));
        };
        return (text, bytes);
    }

    [Fact]
    public void BlinkTogglesTheLedEverySecond()
    {
        var mcu = Load("Blink");
        Assert.Equal(924, mcu.ProgramSize); // the size the Arduino IDE reports for Blink
        var edges = new List<(long cycle, PinDrive drive)>();
        mcu.PortB.PinDriveChanged += (_, bit, drive, cycle) => { if (bit == 5) edges.Add((cycle, drive)); };

        mcu.RunSeconds(4.5);

        // pinMode(OUTPUT) -> Low, then High at the start of loop(), then a toggle every delay(1000).
        Assert.Equal(PinDrive.Low, edges[0].drive);
        Assert.Equal(PinDrive.High, edges[1].drive);
        Assert.Equal(6, edges.Count);
        for (int i = 2; i < edges.Count; i++)
        {
            double intervalMs = (edges[i].cycle - edges[i - 1].cycle) / CyclesPerMs;
            Assert.InRange(intervalMs, 1000.0, 1000.05); // delay(1000) plus a few microseconds of loop overhead
            Assert.NotEqual(edges[i - 1].drive, edges[i].drive);
        }
    }

    [Fact]
    public void SerialHelloPrintsAt9600Baud()
    {
        var mcu = Load("SerialHello");
        var (text, bytes) = CaptureSerial(mcu);

        mcu.RunSeconds(0.35);

        Assert.StartsWith("CoreEngine\r\ntick 0\r\ntick 1\r\ntick 2\r\ntick 3\r\n", text.ToString());
        // Serial.begin(9600) sets U2X and UBRR = 207, so one 10-bit frame takes 10 * 8 * 208 cycles.
        const long frame = 10 * 8 * 208;
        for (int i = 1; i < 12; i++) Assert.Equal(frame, bytes[i].end - bytes[i - 1].end); // back-to-back banner bytes
        Assert.All(bytes, b => Assert.Equal(frame, b.end - b.start));
    }

    [Fact]
    public void MillisMatchesEmulatedTime()
    {
        var mcu = Load("MillisClock");
        var (text, bytes) = CaptureSerial(mcu);
        var lines = new List<(long millis, long startCycle)>();
        var current = new StringBuilder();
        long lineStart = -1;
        mcu.Usart0.ByteTransmitted += (value, start, _) =>
        {
            if (lineStart < 0) lineStart = start;
            if (value == '\n')
            {
                lines.Add((long.Parse(current.ToString().Trim()), lineStart));
                current.Clear();
                lineStart = -1;
            }
            else current.Append((char)value);
        };

        mcu.RunSeconds(3.1);

        Assert.True(lines.Count >= 12, $"expected at least 12 lines, got {lines.Count}: {text}");
        foreach (var (millis, startCycle) in lines)
        {
            double emulatedMs = startCycle / CyclesPerMs;
            // Arduino's millis() lags true time by up to about 1 ms (1.024 ms timer overflows).
            Assert.InRange(emulatedMs - millis, -0.1, 2.0);
        }
        Assert.Equal(117_647, mcu.Usart0.ActualBaud(Atmega328P.ClockHz), 0); // 115200 requested: UBRR 16 with U2X
    }
}
