using CoreEngine.Sim.Avr;
using CoreEngine.Sim.Compile;

namespace CoreEngine.Sim.Tests;

public class IntelHexTests
{
    [Fact]
    public void ParsesDataAndExtendedAddresses()
    {
        const string hex =
            ":0400000001020304F2\n" +
            ":020000040000FA\n" +
            ":02001000AABB89\n" +
            ":00000001FF\n";
        var image = IntelHex.Parse(hex, 64, out int used);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image[..4]);
        Assert.Equal(0xFF, image[4]);            // unwritten flash stays erased
        Assert.Equal(0xAA, image[0x10]);
        Assert.Equal(0xBB, image[0x11]);
        Assert.Equal(0x12, used);
    }

    [Fact]
    public void RejectsBadChecksumsAndOversizedPrograms()
    {
        Assert.Throws<FormatException>(() => IntelHex.Parse(":0400000001020304F3\n", 64));
        Assert.Throws<FormatException>(() => IntelHex.Parse(":02004000AABB59\n", 64));
    }
}

public class CompilerDiagnosticTests
{
    [Fact]
    public void ParsesGccMessagesWithWindowsPaths()
    {
        const string output = """
            C:\Users\me\Documents\Arduino\Blink\Blink.ino: In function 'void loop()':
            C:\Users\me\Documents\Arduino\Blink\Blink.ino:9:3: error: 'digitalWrit' was not declared in this scope
               digitalWrit(LED_BUILTIN, HIGH);
               ^~~~~~~~~~~
            C:\Users\me\Documents\Arduino\Blink\Blink.ino:9:3: note: suggested alternative: 'digitalWrite'
            C:\Users\me\Documents\Arduino\Blink\Blink.ino:12:7: warning: unused variable 'x' [-Wunused-variable]
            exit status 1
            """;
        var diagnostics = ArduinoCliCompiler.ParseDiagnostics(output);
        Assert.Equal(3, diagnostics.Count);
        Assert.Equal(@"C:\Users\me\Documents\Arduino\Blink\Blink.ino", diagnostics[0].File);
        Assert.Equal((9, 3, DiagnosticSeverity.Error), (diagnostics[0].Line, diagnostics[0].Column, diagnostics[0].Severity));
        Assert.StartsWith("'digitalWrit' was not declared", diagnostics[0].Message);
        Assert.Equal(DiagnosticSeverity.Note, diagnostics[1].Severity);
        Assert.Equal((12, 7, DiagnosticSeverity.Warning), (diagnostics[2].Line, diagnostics[2].Column, diagnostics[2].Severity));
    }
}
