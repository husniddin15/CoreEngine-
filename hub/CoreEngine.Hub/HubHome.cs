using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CoreEngine.Hub;

/// <summary>
/// Where the Hub keeps things on the player's computer, all in the player's own folders (no administrator needed):
/// its settings in %LOCALAPPDATA%\CoreEngine Hub, the game by default in %LOCALAPPDATA%\Programs\CoreEngine, the
/// shortcuts on the desktop and in the Start menu. With --home &lt;folder&gt; everything goes into that folder instead,
/// for tests, which must not touch the player's desktop or programs.
/// </summary>
public static class HubHome
{
    static string? home;

    public static void Use(string folder) => home = Path.GetFullPath(folder);

    public static bool IsSandbox => home != null;

    static string LocalAppData => home ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string SettingsFolder => Path.Combine(LocalAppData, "CoreEngine Hub");

    public static string DefaultInstallDir => Path.Combine(LocalAppData, "Programs", "CoreEngine");

    public static string HubInstallDir => Path.Combine(LocalAppData, "Programs", "CoreEngine Hub");

    public static string Desktop => home != null ? Path.Combine(home, "Desktop") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string StartMenu => home != null ? Path.Combine(home, "Start Menu") : Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>
    /// The Hub itself in its own folder, so its shortcuts keep working after the downloaded file is moved or deleted:
    /// when it runs from elsewhere (Downloads), its files are copied there. Returns the program to point shortcuts at.
    /// </summary>
    public static string EnsureHubInstalled()
    {
        string running = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CoreEngineHub.exe");
        string from = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string to = Path.GetFullPath(HubInstallDir).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return running;
        try
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.EnumerateFiles(from))
            {
                string name = Path.GetFileName(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".exe" or ".dll" or ".json" or ".pri")) continue; // the program and what it needs, nothing else from Downloads
                string target = Path.Combine(to, name);
                if (!File.Exists(target) || File.GetLastWriteTimeUtc(target) < File.GetLastWriteTimeUtc(file)) File.Copy(file, target, overwrite: true);
            }
            return Path.Combine(to, Path.GetFileName(running));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return running; // a copy in use by another Hub: shortcuts point at this one
        }
    }

    /// <summary>A Windows shortcut (.lnk) to a program, made with the shell's own ShellLink.</summary>
    public static void CreateShortcut(string lnkPath, string target, string? icon = null, string arguments = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetArguments(arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetIconLocation(icon ?? target, 0);
        link.SetDescription("CoreEngine");
        ((IPersistFile)link).Save(lnkPath, true);
        Marshal.FinalReleaseComObject(link);
    }

    public static void DeleteShortcut(string lnkPath)
    {
        try
        {
            if (File.Exists(lnkPath)) File.Delete(lnkPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int size, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }
}

/// <summary>The Hub's settings, kept between runs in settings.json (<see cref="HubHome.SettingsFolder"/>).</summary>
public sealed class HubSettings
{
    /// <summary>Where the game is installed; empty until the first install or Find game.</summary>
    public string InstallDir { get; set; } = "";

    /// <summary>0 English, 1 Uzbek, 2 Russian; -1 until the player chooses (then the Windows language).</summary>
    public int Language { get; set; } = -1;

    /// <summary>The download speed limit in MB/s; 0 for none.</summary>
    public int SpeedLimitMBps { get; set; }

    /// <summary>When the game starts: 0 minimise the Hub, 1 close it, 2 keep it open.</summary>
    public int AfterStart { get; set; }

    /// <summary>Where releases come from; empty for the built-in address.</summary>
    public string Source { get; set; } = "";

    public bool DesktopShortcut { get; set; } = true;

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    static string FilePath => Path.Combine(HubHome.SettingsFolder, "settings.json");

    public static HubSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<HubSettings>(File.ReadAllBytes(FilePath), Json) ?? new HubSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return new HubSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(HubHome.SettingsFolder);
            File.WriteAllBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(this, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
