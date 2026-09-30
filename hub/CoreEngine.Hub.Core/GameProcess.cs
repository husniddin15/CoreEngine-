using System.ComponentModel;
using System.Diagnostics;

namespace CoreEngine.Hub;

/// <summary>
/// The game's program while it runs. The Hub steps aside once the game's window is up (by default it closes), so a Hub
/// opened again while the game runs finds it here: it says the game is running and keeps Start, Update, Repair and
/// Uninstall waiting until the game closes, since its files are in use.
/// </summary>
public static class GameProcess
{
    /// <summary>
    /// The program at this path while it runs (started by an earlier Hub, a shortcut or by hand); null when it does not.
    /// A program of the same name in another folder is another program (a developer's build of the game, say).
    /// </summary>
    public static Process? Find(string exe)
    {
        string path = Path.GetFullPath(exe);
        Process? found = null;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            if (found == null && RunsFrom(process, path)) found = process;
            else process.Dispose();
        }
        return found;
    }

    static bool RunsFrom(Process process, string path)
    {
        try
        {
            return !process.HasExited && string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false; // gone meanwhile, or not ours to read (another user's, an administrator's)
        }
    }

    public static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits until the program shows its window: true then; false when it closed first or the time ran out (a program
    /// without a window, a very slow first start).
    /// </summary>
    public static async Task<bool> WaitForWindowAsync(Process process, TimeSpan limit, CancellationToken token = default)
    {
        var until = DateTime.UtcNow + limit;
        while (true)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return false;
                if (process.MainWindowHandle != IntPtr.Zero) return true;
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                return false;
            }
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(200, token);
        }
    }
}
