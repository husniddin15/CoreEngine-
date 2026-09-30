namespace CoreEngine.Hub;

/// <summary>What bringing a game's folder to a release takes: the files to download and the ones to delete.</summary>
public sealed class UpdatePlan
{
    public UpdatePlan(ReleaseManifest target, string installDir, List<ReleaseFile> fetch, List<string> remove, bool installed)
    {
        Target = target;
        InstallDir = installDir;
        Fetch = fetch;
        Remove = remove;
        Installed = installed;
    }

    public ReleaseManifest Target { get; }
    public string InstallDir { get; }

    /// <summary>Files that are missing or not as the release has them.</summary>
    public List<ReleaseFile> Fetch { get; }

    /// <summary>Files the Hub installed before that this release no longer has.</summary>
    public List<string> Remove { get; }

    /// <summary>Whether the game was there before (an update or a repair) rather than a first download.</summary>
    public bool Installed { get; }

    public bool UpToDate => Fetch.Count == 0 && Remove.Count == 0;

    /// <summary>What will be downloaded: each content needed once.</summary>
    public long DownloadBytes => Fetch.GroupBy(f => f.Sha256).Sum(g => g.First().Packed);

    /// <summary>What will be written into the game's folder.</summary>
    public long WriteBytes => Fetch.Sum(f => f.Size);

    /// <summary>Free space the work needs on the game's drive: the unpacked files wait beside the game, the downloads with them.</summary>
    public long SpaceNeeded => WriteBytes + DownloadBytes + 64L * 1024 * 1024;
}

public static class UpdatePlanner
{
    /// <summary>
    /// Compares a game's folder with a release. Files are compared by size, then by the Hub's record (size, hash and
    /// time as it wrote them); a file it has no record of, or one that changed since, is read and hashed. With
    /// <paramref name="verify"/> (Repair, or adopting a folder found on the disk) every file is read and hashed.
    /// </summary>
    public static UpdatePlan Plan(ReleaseManifest target, string installDir, bool verify = false,
        IProgress<(long done, long total)>? reading = null, CancellationToken token = default)
    {
        var state = InstallState.Load(installDir);
        var known = new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        if (state != null) foreach (var file in state.Files) known[file.Path] = file;
        var fetch = new List<ReleaseFile>();
        long total = target.Size, done = 0;
        foreach (var file in target.Files)
        {
            token.ThrowIfCancellationRequested();
            string full = ReleasePaths.Resolve(installDir, file.Path);
            var info = new FileInfo(full);
            bool good;
            if (!info.Exists || info.Length != file.Size) good = false;
            else if (!verify && known.TryGetValue(file.Path, out var record) && record.Sha256 == file.Sha256 &&
                     record.Size == file.Size && Math.Abs((record.WrittenUtc - info.LastWriteTimeUtc).TotalSeconds) < 2)
                good = true;
            else good = Hashing.Sha256OfFile(full, token) == file.Sha256;
            if (!good) fetch.Add(file);
            done += file.Size;
            reading?.Report((done, total));
        }
        var inRelease = new HashSet<string>(target.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var remove = state == null ? new List<string>() : state.Files.Select(f => f.Path).Where(p => !inRelease.Contains(p)).ToList();
        bool installed = state != null || target.Files.Any(f => File.Exists(ReleasePaths.Resolve(installDir, f.Path)));
        return new UpdatePlan(target, installDir, fetch, remove, installed);
    }
}
