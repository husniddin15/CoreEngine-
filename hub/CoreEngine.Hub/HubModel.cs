using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CoreEngine.Hub;

public enum HubState { Checking, NoSource, Offline, Untrusted, NotInstalled, Downloading, Paused, Installing, Verifying, Ready, UpdateAvailable, Running, Error }

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class Command : ICommand
{
    readonly Action run;

    public Command(Action run) => this.run = run;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => run();
}

/// <summary>
/// What the Hub's window shows and does, as HoYoPlay's page for one game (docs/adr/ADR-0010-hub-launcher.md):
/// one big button that is always the next step (Download, Pause, Resume, Update, Start, Retry), the progress above
/// it, the version and what is new, the game's menu (Repair, Open folder, Find the game, Uninstall) and the Hub's
/// settings (language, download speed, what happens when the game starts, the download server).
/// </summary>
public sealed class HubModel : Observable
{
    /// <summary>
    /// The built-in download address: empty until the owner chooses where releases are hosted (ADR-0010's open
    /// question); a server can be set in Settings or with --source meanwhile.
    /// </summary>
    public const string BuiltInSource = "";

    public static readonly int[] SpeedLimits = { 0, 1, 2, 5, 10 }; // MB/s; 0 for none

    readonly HubSettings settings;
    readonly string? sourceOverride;
    readonly SynchronizationContext? ui;
    ReleaseSource? source;
    ReleaseManifest? latest;
    InstallState? installed;
    UpdatePlan? pendingPlan;
    Installer? running;
    CancellationTokenSource? work;
    Process? game;
    string lastError = "";

    public HubModel(HubSettings settings, string? sourceOverride = null)
    {
        this.settings = settings;
        this.sourceOverride = sourceOverride;
        ui = SynchronizationContext.Current;
        T = new Strings { Language = settings.Language >= 0 ? settings.Language : Strings.FromWindows() };
        T.PropertyChanged += (_, _) => Refresh();
        MainCommand = new Command(Main);
        MenuCommand = new Command(() => MenuOpen = !MenuOpen);
        RepairCommand = new Command(() => Menu(() => _ = RunAsync(verify: true)));
        OpenFolderCommand = new Command(() => Menu(OpenFolder));
        LocateCommand = new Command(() => Menu(Locate));
        UninstallCommand = new Command(() => Menu(() => UninstallOpen = true));
        ConfirmUninstallCommand = new Command(() => _ = UninstallAsync());
        InstallCommand = new Command(ConfirmInstall);
        ChangeFolderCommand = new Command(ChangeFolder);
        SettingsCommand = new Command(OpenSettings);
        CloseSettingsCommand = new Command(CloseSettings);
        CloseDialogCommand = new Command(CloseDialogs);
        MinimizeCommand = new Command(() => WindowRequest?.Invoke("minimize"));
        CloseCommand = new Command(() => WindowRequest?.Invoke("close"));
        Refresh();
    }

    public Strings T { get; }

    /// <summary>The window's cue: "minimize", "restore" or "close".</summary>
    public event Action<string>? WindowRequest;

    public ICommand MainCommand { get; }
    public ICommand MenuCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand LocateCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand ConfirmUninstallCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand ChangeFolderCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand CloseDialogCommand { get; }
    public ICommand MinimizeCommand { get; }
    public ICommand CloseCommand { get; }

    // ------------------------------------------------------------------ what the window shows

    HubState state = HubState.Checking;

    public HubState State
    {
        get => state;
        private set
        {
            Set(ref state, value);
            Refresh();
        }
    }

    string mainText = "";
    bool mainEnabled, mainQuiet, progressVisible, statusWarn, hasGame, menuOpen, installOpen, settingsOpen, uninstallOpen, messageOpen;
    double progress;
    string progressLeft = "", progressRight = "", status = "", notesTitle = "", installDir = "", installSpace = "", messageTitle = "", messageText = "", sourceText = "";
    bool installSpaceOk = true, desktopShortcut = true;
    IReadOnlyList<string> notes = Array.Empty<string>();

    public string MainText { get => mainText; private set => Set(ref mainText, value); }
    public bool MainEnabled { get => mainEnabled; private set => Set(ref mainEnabled, value); }

    /// <summary>The big button in its quiet look (Pause, and while busy), else the green one.</summary>
    public bool MainQuiet { get => mainQuiet; private set => Set(ref mainQuiet, value); }

    public bool ProgressVisible { get => progressVisible; private set => Set(ref progressVisible, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public string ProgressLeft { get => progressLeft; private set => Set(ref progressLeft, value); }
    public string ProgressRight { get => progressRight; private set => Set(ref progressRight, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool StatusWarn { get => statusWarn; private set => Set(ref statusWarn, value); }
    public string NotesTitle { get => notesTitle; private set => Set(ref notesTitle, value); }
    public IReadOnlyList<string> Notes { get => notes; private set => Set(ref notes, value); }

    /// <summary>Whether the game is installed (the menu's Repair, Open folder and Uninstall need it).</summary>
    public bool IsInstalled { get => hasGame; private set => Set(ref hasGame, value); }

    public bool MenuOpen { get => menuOpen; set => Set(ref menuOpen, value); }
    public bool InstallOpen { get => installOpen; set => Set(ref installOpen, value); }
    public bool SettingsOpen { get => settingsOpen; set => Set(ref settingsOpen, value); }
    public bool UninstallOpen { get => uninstallOpen; set => Set(ref uninstallOpen, value); }
    public bool MessageOpen { get => messageOpen; set => Set(ref messageOpen, value); }
    public string MessageTitle { get => messageTitle; private set => Set(ref messageTitle, value); }
    public string MessageText { get => messageText; private set => Set(ref messageText, value); }

    public string InstallDir
    {
        get => installDir;
        set
        {
            Set(ref installDir, value);
            MeasureSpace();
        }
    }

    public string InstallSpace { get => installSpace; private set => Set(ref installSpace, value); }
    public bool InstallSpaceOk { get => installSpaceOk; private set => Set(ref installSpaceOk, value); }
    public bool DesktopShortcut { get => desktopShortcut; set => Set(ref desktopShortcut, value); }

    public string UninstallText => T.Format("uninstall.text", settings.InstallDir);

    public string HubVersion => "CoreEngine Hub " + (typeof(HubModel).Assembly.GetName().Version?.ToString(3) ?? "");

    public int LanguageIndex
    {
        get => T.Language;
        set
        {
            T.Language = value;
            settings.Language = value;
            settings.Save();
            Changed(nameof(LanguageIndex));
            Changed(nameof(UninstallText));
        }
    }

    public int SpeedIndex
    {
        get => Math.Max(0, Array.IndexOf(SpeedLimits, settings.SpeedLimitMBps));
        set
        {
            settings.SpeedLimitMBps = SpeedLimits[Math.Clamp(value, 0, SpeedLimits.Length - 1)];
            settings.Save();
            if (running != null) running.Limit.BytesPerSecond = settings.SpeedLimitMBps * 1048576L;
            Changed(nameof(SpeedIndex));
        }
    }

    public int AfterStartIndex
    {
        get => settings.AfterStart;
        set
        {
            settings.AfterStart = Math.Clamp(value, 0, 2);
            settings.Save();
            Changed(nameof(AfterStartIndex));
        }
    }

    public string SourceText { get => sourceText; set => Set(ref sourceText, value); }

    /// <summary>Puts the button, the progress, the status and the notes in words for the state and the language.</summary>
    void Refresh()
    {
        bool busy = State is HubState.Downloading or HubState.Installing or HubState.Verifying or HubState.Paused;
        ProgressVisible = busy;
        MainQuiet = State is HubState.Downloading or HubState.Installing or HubState.Verifying or HubState.Running or HubState.Checking;
        (MainText, MainEnabled) = State switch
        {
            HubState.Checking => (T["btn.checking"], false),
            HubState.NotInstalled => (T["btn.download"], true),
            HubState.Downloading => (T["btn.pause"], true),
            HubState.Paused => (T["btn.resume"], true),
            HubState.Installing => (T["btn.installing"], false),
            HubState.Verifying => (T["btn.checking"], false),
            HubState.Ready => (T["btn.start"], true),
            HubState.UpdateAvailable => (T["btn.update"], true),
            HubState.Running => (T["btn.running"], false),
            HubState.NoSource => (T["settings"], true), // it opens the settings, where the server is set
            _ => (T["btn.retry"], true),
        };
        StatusWarn = State is HubState.NoSource or HubState.Offline or HubState.Untrusted or HubState.Error || (State == HubState.Ready && latest == null);
        Status = State switch
        {
            HubState.NoSource => T["status.noSource"],
            HubState.Offline => T["status.offline"],
            HubState.Untrusted => T["status.untrusted"],
            HubState.Error => lastError,
            HubState.NotInstalled when latest != null => T.Format("status.download", latest.Version, T.Size(latest.Packed), T.Size(latest.Size)),
            HubState.Ready when installed != null && Preparing => T["status.preparing"],
            HubState.Ready when installed != null => latest == null
                ? (SourceIsSet ? T.Format("status.offlineInstalled", installed.Version) : T.Format("status.upToDate", installed.Version))
                : T.Format("status.upToDate", installed.Version),
            HubState.UpdateAvailable when latest != null && pendingPlan != null => T.Format("status.update", latest.Version, T.Size(pendingPlan.DownloadBytes)),
            HubState.Running => T["status.running"],
            _ => "",
        };
        if (State == HubState.Ready && latest == null && !SourceIsSet) StatusWarn = false;
        var shown = latest;
        if (shown != null)
        {
            string date = shown.Published.ToLocalTime().ToString("d MMMM yyyy", Culture());
            NotesTitle = $"{T["news"]} · {shown.Version} · {date}";
            Notes = shown.NotesIn(T.Code);
        }
        else
        {
            NotesTitle = T["news"];
            Notes = new[] { T["news.none"] };
        }
        IsInstalled = installed != null;
        if (State == HubState.Paused)
        {
            ProgressLeft = T.Format("status.paused", T.Size(pausedDone), T.Size(pausedTotal));
            ProgressRight = "";
        }
        Changed(nameof(UninstallText));
    }

    bool SourceIsSet => (sourceOverride ?? (settings.Source.Length > 0 ? settings.Source : BuiltInSource)).Length > 0;

    CultureInfo Culture() => T.Code switch
    {
        "uz" => SafeCulture("uz-Latn-UZ"),
        "ru" => SafeCulture("ru-RU"),
        _ => SafeCulture("en-GB"),
    };

    static CultureInfo SafeCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    // ------------------------------------------------------------------ checking for the newest release

    /// <summary>What is installed, and what the server has: the state the button shows next.</summary>
    public async Task CheckAsync()
    {
        State = HubState.Checking;
        installed = settings.InstallDir.Length > 0 ? InstallState.Load(settings.InstallDir) : null;
        latest = null;
        pendingPlan = null;
        string where = sourceOverride ?? (settings.Source.Length > 0 ? settings.Source : BuiltInSource);
        if (where.Length == 0)
        {
            State = installed != null ? HubState.Ready : HubState.NoSource;
            return;
        }
        try
        {
            source = ReleaseSource.From(where);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            latest = await new ReleaseClient(source).LatestAsync(timeout.Token);
        }
        catch (ReleaseNotTrustedException)
        {
            State = installed != null ? HubState.Ready : HubState.Untrusted;
            return;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            State = installed != null ? HubState.Ready : HubState.Offline;
            return;
        }
        if (installed == null)
        {
            State = HubState.NotInstalled;
            return;
        }
        try
        {
            var target = latest;
            string dir = settings.InstallDir;
            pendingPlan = await Task.Run(() => UpdatePlanner.Plan(target, dir));
            State = pendingPlan.UpToDate ? HubState.Ready : HubState.UpdateAvailable;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Fail(e.Message);
        }
    }

    // ------------------------------------------------------------------ the big button

    void Main()
    {
        switch (State)
        {
            case HubState.NotInstalled:
                OpenInstall();
                break;
            case HubState.UpdateAvailable:
            case HubState.Paused:
                _ = RunAsync();
                break;
            case HubState.Downloading:
                work?.Cancel(); // Pause
                break;
            case HubState.Ready:
                StartGame();
                break;
            case HubState.NoSource:
                OpenSettings();
                break;
            case HubState.Offline:
            case HubState.Untrusted:
            case HubState.Error:
                _ = CheckAsync();
                break;
        }
    }

    void OpenInstall()
    {
        DesktopShortcut = settings.DesktopShortcut;
        InstallDir = settings.InstallDir.Length > 0 ? settings.InstallDir : HubHome.DefaultInstallDir;
        InstallOpen = true;
    }

    void MeasureSpace()
    {
        if (latest == null || installDir.Length == 0)
        {
            InstallSpace = "";
            InstallSpaceOk = latest != null;
            return;
        }
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(installDir)) ?? "";
            long free = new DriveInfo(root).AvailableFreeSpace;
            long needs = latest.Size + latest.Packed + 64L * 1024 * 1024;
            InstallSpaceOk = free >= needs;
            InstallSpace = T.Format(InstallSpaceOk ? "install.space" : "install.noSpace", T.Size(needs), T.Size(free), root.TrimEnd('\\'));
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            InstallSpace = e.Message;
            InstallSpaceOk = false;
        }
    }

    void ChangeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T["install.folder"] };
        string parent = Path.GetDirectoryName(installDir) ?? "";
        if (Directory.Exists(parent)) dialog.InitialDirectory = parent;
        if (dialog.ShowDialog() != true) return;
        string chosen = dialog.FolderName;
        InstallDir = string.Equals(Path.GetFileName(chosen), "CoreEngine", StringComparison.OrdinalIgnoreCase) ? chosen : Path.Combine(chosen, "CoreEngine");
    }

    void ConfirmInstall()
    {
        if (!InstallSpaceOk || installDir.Length == 0) return;
        settings.InstallDir = Path.GetFullPath(installDir);
        settings.DesktopShortcut = DesktopShortcut;
        settings.Save();
        InstallOpen = false;
        _ = RunAsync();
    }

    long pausedDone, pausedTotal;

    /// <summary>Downloads and installs what the newest release needs (a first install, an update, a resumed one or a repair).</summary>
    public async Task RunAsync(bool verify = false)
    {
        if (latest == null || source == null)
        {
            await CheckAsync();
            if (latest == null || source == null) return;
        }
        var target = latest;
        string dir = settings.InstallDir;
        work = new CancellationTokenSource();
        var token = work.Token;
        try
        {
            UpdatePlan plan;
            if (verify)
            {
                State = HubState.Verifying;
                Progress = 0;
                ProgressLeft = T["progress.checking"];
                ProgressRight = "";
                var reading = new Progress<(long done, long total)>(p =>
                {
                    Progress = p.total > 0 ? (double)p.done / p.total : 0;
                    ProgressLeft = $"{T["progress.checking"]} · {Progress * 100:F0}%";
                });
                plan = await Task.Run(() => UpdatePlanner.Plan(target, dir, verify: true, reading, token), token);
            }
            else plan = await Task.Run(() => UpdatePlanner.Plan(target, dir, false, null, token), token);

            State = HubState.Downloading;
            var installer = new Installer(source);
            installer.Limit.BytesPerSecond = settings.SpeedLimitMBps * 1048576L;
            running = installer;
            await installer.RunAsync(plan, new Progress<InstallProgress>(Show), token);
            installed = InstallState.Load(dir);
            pendingPlan = null;
            if (!plan.Installed && settings.DesktopShortcut) MakeShortcuts(); // the first install, however many pauses it took
            if (plan.Fetch.Any(f => f.Path.StartsWith("tools/arduino/", StringComparison.Ordinal))) _ = PrepareCompilerAsync(dir);
            State = HubState.Ready;
            if (verify) ShowMessage(T["repair.title"], plan.Fetch.Count == 0 ? T["repair.fine"] : T.Format("repair.fixed", plan.Fetch.Count));
        }
        catch (OperationCanceledException)
        {
            State = HubState.Paused;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
        {
            installed = settings.InstallDir.Length > 0 ? InstallState.Load(settings.InstallDir) : null;
            ShowMessage(T["error.title"], e.Message);
            State = installed == null ? HubState.NotInstalled : HubState.UpdateAvailable;
            if (pendingPlan == null && installed != null) _ = CheckAsync();
        }
        finally
        {
            running = null;
            work = null;
        }
    }

    bool preparing;

    /// <summary>Whether the Arduino compiler is being prepared, once, after an install (<see cref="PrepareCompilerAsync"/>).</summary>
    public bool Preparing
    {
        get => preparing;
        private set
        {
            Set(ref preparing, value);
            Refresh();
        }
    }

    /// <summary>
    /// Compiles a small sketch once with the game's own Arduino toolchain, in the background, after an install or an update
    /// that brought it: the first compile on a PC is slow (the antivirus reads each new compiler program, the Arduino core
    /// is built: over 2 minutes on a busy PC, 2026-09-30), so the player's first Upload in the game is not. It uses the
    /// game's compiler cache (tools/arduino/cache) and its own settings file, not the game's; a failure only means the
    /// game's first compile is the slow one.
    /// </summary>
    async Task PrepareCompilerAsync(string dir)
    {
        string tools = Path.Combine(dir, "tools", "arduino");
        string cli = Path.Combine(tools, "bin", "arduino-cli.exe");
        if (!File.Exists(cli)) return;
        Preparing = true;
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "CoreEngine Hub", "compiler");
            string sketch = Path.Combine(root, "Warmup");
            Directory.CreateDirectory(sketch);
            await File.WriteAllTextAsync(Path.Combine(sketch, "Warmup.ino"),
                "void setup() { pinMode(13, OUTPUT); Serial.begin(9600); }\nvoid loop() { digitalWrite(13, !digitalRead(13)); delay(500); }\n");
            string folder = tools.Replace('\\', '/');
            string settingsFile = Path.Combine(root, "arduino-cli.yaml");
            await File.WriteAllTextAsync(settingsFile,
                $"build_cache:\n  path: \"{folder}/cache\"\ndirectories:\n  data: \"{folder}/data\"\n  downloads: \"{folder}/staging\"\n  user: \"{folder}/user\"\n" +
                "locale: en\nupdater:\n  enable_notification: false\n");
            var start = new ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[] { "compile", "--fqbn", "arduino:avr:uno", "--config-file", settingsFile, "--build-path", Path.Combine(root, "build"), sketch })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process == null) return;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
            }
            await Task.WhenAll(output, errors);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
        finally
        {
            Preparing = false;
        }
    }

    void Show(InstallProgress p)
    {
        if (p.Phase == InstallPhase.Installing && State == HubState.Downloading) State = HubState.Installing;
        if (State != HubState.Downloading && State != HubState.Installing) return;
        Progress = p.Total > 0 ? (double)p.Done / p.Total : 0;
        if (p.Phase == InstallPhase.Downloading)
        {
            pausedDone = p.Done;
            pausedTotal = p.Total;
            ProgressLeft = $"{T["progress.downloading"]} · {Progress * 100:F0}%";
            var parts = new List<string>();
            if (p.BytesPerSecond > 0) parts.Add(T.Speed(p.BytesPerSecond));
            if (p.Remaining is { } left) parts.Add(T.Format("progress.left", T.Duration(left)));
            parts.Add($"{T.Size(p.Done)} / {T.Size(p.Total)}");
            ProgressRight = string.Join(" · ", parts);
        }
        else
        {
            ProgressLeft = $"{T["progress.installing"]} · {Progress * 100:F0}%";
            ProgressRight = "";
        }
    }

    void MakeShortcuts()
    {
        try
        {
            string hub = HubHome.EnsureHubInstalled();
            HubHome.CreateShortcut(Path.Combine(HubHome.Desktop, "CoreEngine.lnk"), hub);
            HubHome.CreateShortcut(Path.Combine(HubHome.StartMenu, "CoreEngine.lnk"), hub);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // the game works without them
        }
    }

    void StartGame()
    {
        if (installed == null) return;
        string exe;
        try
        {
            exe = ReleasePaths.Resolve(settings.InstallDir, installed.Exe);
        }
        catch (InvalidDataException e)
        {
            Fail(e.Message);
            return;
        }
        if (!File.Exists(exe))
        {
            ShowMessage(T["error.title"], exe);
            _ = CheckAsync();
            return;
        }
        try
        {
            game = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = settings.InstallDir, UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            ShowMessage(T["error.title"], e.Message);
            return;
        }
        if (game == null) return;
        game.EnableRaisingEvents = true;
        game.Exited += (_, _) => Post(() =>
        {
            game = null;
            WindowRequest?.Invoke("restore");
            _ = CheckAsync(); // an update may have come out while playing
        });
        State = HubState.Running;
        WindowRequest?.Invoke(settings.AfterStart switch { 1 => "close", 0 => "minimize", _ => "none" });
    }

    // ------------------------------------------------------------------ the game's menu

    void Menu(Action action)
    {
        MenuOpen = false;
        action();
    }

    void OpenFolder()
    {
        if (settings.InstallDir.Length > 0 && Directory.Exists(settings.InstallDir))
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + settings.InstallDir + "\"") { UseShellExecute = false });
    }

    /// <summary>Find the game: a folder the player copied or installed before is read and adopted (what differs is downloaded).</summary>
    void Locate()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T["menu.locate"] };
        if (dialog.ShowDialog() != true) return;
        string folder = dialog.FolderName;
        string exe = latest?.Exe ?? installed?.Exe ?? "CoreEngineSpike.exe";
        if (!File.Exists(Path.Combine(folder, exe.Replace('/', Path.DirectorySeparatorChar))))
        {
            ShowMessage(T["menu.locate"], T["locate.notFound"]);
            return;
        }
        settings.InstallDir = folder;
        settings.Save();
        _ = RunAsync(verify: true);
    }

    async Task UninstallAsync()
    {
        UninstallOpen = false;
        string dir = settings.InstallDir;
        State = HubState.Checking;
        try
        {
            await Task.Run(() => Installer.Uninstall(dir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowMessage(T["error.title"], e.Message);
        }
        HubHome.DeleteShortcut(Path.Combine(HubHome.Desktop, "CoreEngine.lnk"));
        HubHome.DeleteShortcut(Path.Combine(HubHome.StartMenu, "CoreEngine.lnk"));
        settings.InstallDir = "";
        settings.Save();
        installed = null;
        State = latest != null ? HubState.NotInstalled : HubState.Offline;
        if (latest == null) await CheckAsync();
    }

    // ------------------------------------------------------------------ settings and messages

    void OpenSettings()
    {
        SourceText = settings.Source;
        SettingsOpen = true;
    }

    void CloseSettings()
    {
        SettingsOpen = false;
        string typed = SourceText.Trim();
        if (typed == settings.Source) return;
        settings.Source = typed;
        settings.Save();
        if (State is not (HubState.Downloading or HubState.Installing or HubState.Verifying or HubState.Running)) _ = CheckAsync();
    }

    void CloseDialogs()
    {
        InstallOpen = false;
        UninstallOpen = false;
        MessageOpen = false;
        MenuOpen = false;
        if (SettingsOpen) CloseSettings();
    }

    void ShowMessage(string title, string text)
    {
        MessageTitle = title;
        MessageText = text;
        MessageOpen = true;
    }

    void Fail(string message)
    {
        lastError = message;
        State = HubState.Error;
    }

    void Post(Action action)
    {
        if (ui != null) ui.Post(_ => action(), null);
        else action();
    }

    /// <summary>For the pictures of the window (--shots): a state with the numbers of the moment.</summary>
    internal void Pose(HubState pose, double progressValue = 0, string left = "", string right = "")
    {
        State = pose;
        if (pose is HubState.Downloading or HubState.Installing or HubState.Verifying or HubState.Paused)
        {
            Progress = progressValue;
            if (left.Length > 0) ProgressLeft = left;
            ProgressRight = right;
        }
    }
}
