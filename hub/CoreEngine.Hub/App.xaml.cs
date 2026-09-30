using System.Runtime.InteropServices;
using System.Windows;

namespace CoreEngine.Hub;

/// <summary>
/// CoreEngineHub.exe [--source &lt;web address or folder&gt;] [--home &lt;folder&gt;]
/// <list type="bullet">
/// <item>--source: where releases come from, instead of the setting (for testing a release before it is uploaded).</item>
/// <item>--home: a folder for everything the Hub keeps (settings, the game, shortcuts), instead of the player's own
/// folders: for tests.</item>
/// <item>--shots &lt;folder&gt; (with --home and --source): pictures of the window through a whole install, made without
/// showing it (<see cref="Shots"/>); --icon &lt;file&gt; draws the Hub's icon.</item>
/// </list>
/// One Hub runs at a time: starting it again brings the first one forward.
/// </summary>
public partial class App : Application
{
    Mutex? single;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = Arguments.Parse(e.Args);
        if (args.Home != null) HubHome.Use(args.Home);
        if (args.Shots != null || args.Icon != null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bool good = await Shots.RunAsync(args);
            Shutdown(good ? 0 : 1);
            return;
        }
        single = new Mutex(true, @"Local\CoreEngineHub" + (args.Home != null ? "-test" : ""), out bool first);
        if (!first)
        {
            var other = FindWindow(null, "CoreEngine Hub");
            if (other != IntPtr.Zero)
            {
                ShowWindow(other, 9); // SW_RESTORE
                SetForegroundWindow(other);
            }
            Shutdown();
            return;
        }
        var model = new HubModel(HubSettings.Load(), args.Source);
        var window = new MainWindow(model);
        MainWindow = window;
        window.Show();
        await model.CheckAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        single?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
}

public sealed record Arguments(string? Source, string? Home, string? Shots, string? Icon)
{
    public static Arguments Parse(string[] args)
    {
        string? Value(string name)
        {
            int at = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
        return new Arguments(Value("--source"), Value("--home"), Value("--shots"), Value("--icon"));
    }
}
