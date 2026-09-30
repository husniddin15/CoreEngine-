using System.Diagnostics;

namespace CoreEngine.Hub.Tests;

public class GameProcessTests
{
    [Fact]
    public async Task AProgramThatRunsIsFoundByItsPathAndItsWindowIsWaitedFor()
    {
        // A small Windows program without a window, standing in for the game: ping runs for about three seconds.
        string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        using var process = Process.Start(new ProcessStartInfo(ping, "-n 4 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            using (var found = GameProcess.Find(ping))
            {
                Assert.NotNull(found);
                Assert.True(GameProcess.IsRunning(found));
            }
            // The same name in another folder is another program (a developer's build of the game beside the installed one).
            Assert.Null(GameProcess.Find(Path.Combine(Path.GetTempPath(), "CoreEngine.Hub.Tests", "PING.EXE")));

            // It shows no window: the wait ends when the time runs out, and when the program closes.
            Assert.False(await GameProcess.WaitForWindowAsync(process, TimeSpan.FromMilliseconds(300)));
            Assert.True(GameProcess.IsRunning(process));
            var closing = Stopwatch.StartNew();
            Assert.False(await GameProcess.WaitForWindowAsync(process, TimeSpan.FromMinutes(1)));
            Assert.True(closing.Elapsed < TimeSpan.FromSeconds(30));
            Assert.False(GameProcess.IsRunning(process));
        }
        finally
        {
            if (GameProcess.IsRunning(process)) process.Kill();
        }
    }

    [Fact]
    public void AGameThatDoesNotRunIsNotFound()
    {
        string folder = Path.Combine(Path.GetTempPath(), "CoreEngine.Hub.Tests", Guid.NewGuid().ToString("N"));
        Assert.Null(GameProcess.Find(Path.Combine(folder, "CoreEngineSpike.exe")));
    }
}
