using System.Security.Cryptography;
using System.Text.Json;

namespace CoreEngine.Hub;

/// <summary>
/// What the Hub installed in a game's folder, kept in .hub/state.json there: the version and every file it wrote,
/// with its size, hash and time. The next check trusts a file whose size and time are unchanged, so it need not read
/// the whole game again; Repair reads everything. Only files named here are ever deleted (an update that drops them,
/// Uninstall): whatever else is in the folder stays.
/// </summary>
public sealed class InstallState
{
    public string Version { get; set; } = "";
    public string Exe { get; set; } = "";
    public List<InstalledFile> Files { get; set; } = new();

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string PathIn(string installDir) => ReleasePaths.Hub(installDir, "state.json");

    /// <summary>The Hub's record in a game's folder, or null when there is none (or it cannot be read).</summary>
    public static InstallState? Load(string installDir)
    {
        string path = PathIn(installDir);
        if (!File.Exists(path)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllBytes(path), Json);
            if (state == null) return null;
            state.Files.RemoveAll(f => ReleasePaths.Problem(f.Path) != null); // never trust a path to delete
            return state;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves it, whole or not at all (a new file, then a rename).</summary>
    public void Save(string installDir)
    {
        string path = PathIn(installDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".new";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class InstalledFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime WrittenUtc { get; set; }
}

public static class Hashing
{
    /// <summary>A file's SHA-256 as 64 lowercase hexadecimal digits.</summary>
    public static string Sha256OfFile(string path, CancellationToken token = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
