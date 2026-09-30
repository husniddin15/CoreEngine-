namespace CoreEngine.Hub;

/// <summary>
/// The rules for the file paths a release names. A release comes from the internet, so each path is checked before
/// anything is written: relative, forward slashes, no "." or ".." parts, no drive, nothing the Hub keeps for itself
/// (.hub/), no names Windows reserves; and every path is resolved inside the game's folder, never outside it.
/// </summary>
public static class ReleasePaths
{
    /// <summary>The Hub's own folder inside a game's folder: its record of what it installed, downloads in progress.</summary>
    public const string HubFolder = ".hub";

    const int MaxLength = 240;

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why a path may not be in a release, or null when it may.</summary>
    public static string? Problem(string path)
    {
        if (string.IsNullOrEmpty(path)) return "empty";
        if (path.Length > MaxLength) return "too long";
        if (path.Contains('\\')) return "backslash (use /)";
        if (path.StartsWith('/')) return "starts at the root";
        if (path.Contains(':')) return "names a drive or a stream";
        foreach (char c in path)
            if (c < 32 || "<>\"|?*".Contains(c)) return "a character Windows does not allow";
        var parts = path.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0) return "an empty part (//)";
            if (part == "." || part == "..") return "a . or .. part";
            if (part.EndsWith('.') || part.EndsWith(' ')) return "a part ending in a dot or a space";
            string stem = part.Split('.')[0];
            if (Reserved.Contains(stem)) return $"a name Windows reserves ({part})";
        }
        if (string.Equals(parts[0], HubFolder, StringComparison.OrdinalIgnoreCase)) return "inside the Hub's own folder";
        return null;
    }

    /// <summary>The full path of a release path inside the game's folder; throws when it would land outside it.</summary>
    public static string Resolve(string installDir, string path)
    {
        string? problem = Problem(path);
        if (problem != null) throw new InvalidDataException($"The release names a file the Hub will not write: \"{path}\" ({problem}).");
        string root = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The release names a file outside the game's folder: \"{path}\".");
        return full;
    }

    /// <summary>The Hub's own folder inside a game's folder.</summary>
    public static string Hub(string installDir, params string[] more) =>
        Path.Combine(new[] { Path.GetFullPath(installDir), HubFolder }.Concat(more).ToArray());
}
