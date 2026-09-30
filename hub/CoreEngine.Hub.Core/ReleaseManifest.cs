using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreEngine.Hub;

/// <summary>
/// One version of the game as the Hub downloads it (docs/adr/ADR-0010-hub-launcher.md). A release is a folder of
/// plain files that any web server can hold:
/// <list type="bullet">
/// <item>manifest.json: this, with every file of the game, its size and its SHA-256;</item>
/// <item>manifest.json.sig: the release key's signature of manifest.json's exact bytes (<see cref="ReleaseSigning"/>);</item>
/// <item>blobs/&lt;sha256&gt;: each file's content, Brotli-compressed, stored once by its hash, so that the next version
/// only adds the files that changed and an update downloads only those.</item>
/// </list>
/// </summary>
public sealed class ReleaseManifest
{
    public const int CurrentFormat = 1;
    public const string FileName = "manifest.json";
    public const string SignatureName = "manifest.json.sig";
    public const string BlobFolder = "blobs";

    public int Format { get; set; } = CurrentFormat;
    public string Product { get; set; } = "CoreEngine";
    public string Version { get; set; } = "";
    public DateTime Published { get; set; }

    /// <summary>The program the Hub starts, as a path in <see cref="Files"/>.</summary>
    public string Exe { get; set; } = "";

    public List<ReleaseFile> Files { get; set; } = new();

    /// <summary>What is new in this version, by language ("en", "uz", "ru"): a few short lines each.</summary>
    public Dictionary<string, List<string>> Notes { get; set; } = new();

    /// <summary>The game's size once installed.</summary>
    [JsonIgnore]
    public long Size => Files.Sum(f => f.Size);

    /// <summary>What a first download fetches: each stored content once.</summary>
    [JsonIgnore]
    public long Packed => Files.GroupBy(f => f.Sha256).Sum(g => g.First().Packed);

    /// <summary>The notes in a language, else in English.</summary>
    public IReadOnlyList<string> NotesIn(string language) =>
        Notes.TryGetValue(language, out var lines) && lines.Count > 0 ? lines : Notes.TryGetValue("en", out var english) ? english : Array.Empty<string>();

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>A manifest from its bytes, checked (<see cref="Validate"/>); throws InvalidDataException when it is not a good one.</summary>
    public static ReleaseManifest Parse(byte[] bytes)
    {
        ReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(bytes, Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The release's manifest is not readable: " + e.Message, e);
        }
        if (manifest == null) throw new InvalidDataException("The release's manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    /// <summary>Checks everything the Hub relies on before it writes a byte: paths, hashes, sizes, the program to start.</summary>
    public void Validate()
    {
        if (Format != CurrentFormat) throw new InvalidDataException($"The release is in format {Format}; this Hub reads format {CurrentFormat}. Update the Hub.");
        if (string.IsNullOrWhiteSpace(Version) || Version.Length > 64) throw new InvalidDataException("The release has no version.");
        if (Files.Count == 0) throw new InvalidDataException("The release has no files.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            string? problem = ReleasePaths.Problem(file.Path);
            if (problem != null) throw new InvalidDataException($"The release names a file the Hub will not write: \"{file.Path}\" ({problem}).");
            if (!seen.Add(file.Path)) throw new InvalidDataException($"The release names \"{file.Path}\" twice.");
            if (!IsHash(file.Sha256)) throw new InvalidDataException($"The release's hash for \"{file.Path}\" is not a SHA-256.");
            if (file.Size < 0 || file.Packed <= 0) throw new InvalidDataException($"The release's sizes for \"{file.Path}\" are wrong.");
        }
        if (!seen.Contains(Exe)) throw new InvalidDataException($"The release's program \"{Exe}\" is not among its files.");
    }

    /// <summary>A SHA-256 as the Hub writes it: 64 lowercase hexadecimal digits.</summary>
    public static bool IsHash(string text) => text.Length == 64 && text.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    /// <summary>Where a content is stored in a release.</summary>
    public static string BlobPath(string sha256) => BlobFolder + "/" + sha256;
}

/// <summary>One file of a release.</summary>
public sealed class ReleaseFile
{
    /// <summary>Where it goes in the game's folder, with forward slashes (<see cref="ReleasePaths"/>).</summary>
    public string Path { get; set; } = "";

    public long Size { get; set; }

    /// <summary>The SHA-256 of its content, 64 lowercase hexadecimal digits: also the name of its blob.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>The size of its blob, the content Brotli-compressed.</summary>
    public long Packed { get; set; }
}
