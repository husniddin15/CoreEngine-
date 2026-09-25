using System.Diagnostics;
using System.Runtime.InteropServices;
using CoreEngine.Sim.Compile;

namespace CoreEngine.Sim.Tests;

public class ProcessRunnerTests
{
    static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("with space", "\"with space\"")]
    [InlineData(@"C:\Program Files\x\", "\"C:\\Program Files\\x\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"a\\b", @"a\\b")]
    [InlineData("", "\"\"")]
    public void QuotesArgumentsLikeCommandLineToArgv(string argument, string quoted)
    {
        Assert.Equal(quoted, Win32ProcessRunner.QuoteArgument(argument));
    }

    [Fact]
    public void Win32RunnerCapturesOutputErrorAndExitCode()
    {
        if (!OnWindows) return;
        var result = new Win32ProcessRunner().Run("cmd.exe", new[] { "/c", "echo hello from cmd & echo to stderr 1>&2 & exit 3" }, TimeSpan.FromSeconds(20));
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("hello from cmd", result.Output);
        Assert.Contains("to stderr", result.Output);
    }

    [Fact]
    public void Win32RunnerStopsAProgramThatRunsTooLong()
    {
        if (!OnWindows) return;
        var watch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() =>
            new Win32ProcessRunner().Run("cmd.exe", new[] { "/c", "ping -n 3 127.0.0.1" }, TimeSpan.FromSeconds(0.5)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// End to end through the default runner, with spaces in every folder name, as in the player's data folder.
    /// Needs tools/fetch-toolchain.ps1; without the toolchain (for example in CI) there is nothing to check.
    /// </summary>
    [Fact]
    public void CompilesBlinkToTheGoldenHexWhenTheToolchainIsInstalled()
    {
        var compiler = ArduinoCliCompiler.FindBundled(AppContext.BaseDirectory);
        if (compiler == null) return;
        string golden = Path.Combine(AppContext.BaseDirectory, "Golden");
        string root = Path.Combine(Path.GetTempPath(), "CoreEngine tests " + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            string sketch = Path.Combine(root, "Blink");
            Directory.CreateDirectory(sketch);
            File.Copy(Path.Combine(golden, "Sketches", "Blink", "Blink.ino"), Path.Combine(sketch, "Blink.ino"));
            var result = compiler.Compile(sketch, "arduino:avr:uno", Path.Combine(root, "build dir"), Path.Combine(root, "out dir"), TimeSpan.FromMinutes(3));
            Assert.True(result.Success, result.Output);
            // The same records, whichever line ends the checkout gave the golden file (.gitattributes: eol=lf).
            static string Records(string path) => File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Equal(Records(Path.Combine(golden, "Hex", "Blink.hex")), Records(result.HexPath!));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
