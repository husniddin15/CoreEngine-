using System.IO.Compression;
using System.Security.Cryptography;

namespace CoreEngine.Hub;

public enum InstallPhase { Downloading, Installing, Done }

/// <summary>How far an install or update has come: bytes downloaded while downloading, files placed while installing.</summary>
public sealed record InstallProgress(InstallPhase Phase, long Done, long Total, double BytesPerSecond, TimeSpan? Remaining);

/// <summary>
/// Brings a game's folder to a release (an <see cref="UpdatePlan"/>), in two steps, so that a stopped download never
/// leaves a half-updated game:
/// <list type="number">
/// <item>Download: each content needed, once, a few at a time, into .hub/downloads (a ".part" carries on after a
/// pause or a lost connection); then unpacked into .hub/staging and checked against the signed manifest's SHA-256.
/// A damaged download is fetched once more, then refused.</item>
/// <item>Install: only when everything is there, the unpacked files are moved into place, the files the release no
/// longer has are deleted, and the Hub's record (<see cref="InstallState"/>) is written. This step is short and is
/// not interrupted.</item>
/// </list>
/// </summary>
public sealed class Installer
{
    public Installer(ReleaseSource source) => Source = source;

    public ReleaseSource Source { get; }

    /// <summary>How many downloads run at once.</summary>
    public int Parallel { get; init; } = 3;

    /// <summary>The download speed limit (0: none); it can change while downloads run.</summary>
    public SpeedLimit Limit { get; } = new SpeedLimit(0);

    public async Task RunAsync(UpdatePlan plan, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        string dir = plan.InstallDir;
        string downloads = ReleasePaths.Hub(dir, "downloads"), staging = ReleasePaths.Hub(dir, "staging");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(staging);

        // 1. Download and unpack each content needed, once.
        var contents = plan.Fetch.GroupBy(f => f.Sha256).Select(g => g.First()).ToList();
        long total = contents.Sum(c => c.Packed), done = 0;
        var meter = new SpeedMeter();
        void Count(long bytes)
        {
            long now = Math.Min(Interlocked.Add(ref done, bytes), total);
            meter.Add(bytes);
            progress?.Report(new InstallProgress(InstallPhase.Downloading, now, total, meter.BytesPerSecond, meter.Remaining(total - now)));
        }
        progress?.Report(new InstallProgress(InstallPhase.Downloading, 0, total, 0, null));
        using (var gate = new SemaphoreSlim(Math.Max(1, Parallel)))
        {
            var work = contents.Select(async content =>
            {
                await gate.WaitAsync(token);
                try
                {
                    await Fetch(content, downloads, staging, Count, token);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();
            await Task.WhenAll(work);
        }

        // 2. Into place, all at once; not stopped half way.
        progress?.Report(new InstallProgress(InstallPhase.Installing, 0, plan.Fetch.Count, 0, null));
        var uses = plan.Fetch.GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.Count());
        int placed = 0;
        foreach (var file in plan.Fetch)
        {
            string target = ReleasePaths.Resolve(dir, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string staged = Path.Combine(staging, file.Sha256);
            try
            {
                if (--uses[file.Sha256] == 0) File.Move(staged, target, overwrite: true);
                else File.Copy(staged, target, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"\"{file.Path}\" cannot be replaced: close the game (and anything using its files) and try again.", e);
            }
            progress?.Report(new InstallProgress(InstallPhase.Installing, ++placed, plan.Fetch.Count, 0, null));
        }
        foreach (string path in plan.Remove)
        {
            string full = ReleasePaths.Resolve(dir, path);
            try
            {
                if (File.Exists(full)) File.Delete(full);
                RemoveEmptyFolders(Path.GetDirectoryName(full)!, dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // left for the next update; the new record no longer names it, so it is not in the way
            }
        }
        new InstallState
        {
            Version = plan.Target.Version,
            Exe = plan.Target.Exe,
            Files = plan.Target.Files.Select(f => new InstalledFile
            {
                Path = f.Path,
                Size = f.Size,
                Sha256 = f.Sha256,
                WrittenUtc = File.GetLastWriteTimeUtc(ReleasePaths.Resolve(dir, f.Path)),
            }).ToList(),
        }.Save(dir);
        TryDelete(staging);
        TryDelete(downloads);
        progress?.Report(new InstallProgress(InstallPhase.Done, plan.Fetch.Count, plan.Fetch.Count, 0, null));
    }

    /// <summary>One content: already unpacked and good, or downloaded (from where it stopped), unpacked and checked.</summary>
    async Task Fetch(ReleaseFile content, string downloads, string staging, Action<long> count, CancellationToken token)
    {
        string staged = Path.Combine(staging, content.Sha256);
        if (File.Exists(staged) && new FileInfo(staged).Length == content.Size &&
            await Task.Run(() => Hashing.Sha256OfFile(staged, token), token) == content.Sha256)
        {
            count(content.Packed);
            return;
        }
        string part = Path.Combine(downloads, content.Sha256 + ".part");
        for (int attempt = 1; ; attempt++)
        {
            long had = File.Exists(part) ? Math.Min(new FileInfo(part).Length, content.Packed) : 0;
            if (had > 0) count(had);
            await Source.DownloadAsync(ReleaseManifest.BlobPath(content.Sha256), part, content.Packed, count, Limit, token);
            string temp = staged + ".tmp";
            try
            {
                await Task.Run(() => Unpack(part, temp, content, token), token);
            }
            catch (InvalidDataException) when (attempt == 1)
            {
                TryDeleteFile(part); // damaged on the way or on the server: once more from the start
                continue;
            }
            File.Move(temp, staged, overwrite: true);
            TryDeleteFile(part);
            return;
        }
    }

    /// <summary>Unpacks a downloaded content and checks its size and SHA-256 against the manifest.</summary>
    static void Unpack(string part, string temp, ReleaseFile content, CancellationToken token)
    {
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            using (var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var brotli = new BrotliStream(input, CompressionMode.Decompress))
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    size += read;
                    if (size > content.Size) throw new InvalidDataException("longer than it should be");
                    sha.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }
            }
            if (size != content.Size || Convert.ToHexStringLower(sha.GetHashAndReset()) != content.Sha256)
                throw new InvalidDataException("not as the release describes it");
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException) // bad data; a full disk stays an IOException
        {
            TryDeleteFile(temp);
            throw new InvalidDataException($"The download of \"{content.Path}\" is damaged ({e.Message}).", e);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    /// <summary>
    /// Deletes the game the Hub installed: the files it wrote (named in its record) and its own folder, then the
    /// folders left empty. Anything else in the folder stays.
    /// </summary>
    public static void Uninstall(string installDir)
    {
        var state = InstallState.Load(installDir);
        if (state != null)
            foreach (var file in state.Files)
            {
                string full = ReleasePaths.Resolve(installDir, file.Path);
                if (File.Exists(full)) File.Delete(full);
                RemoveEmptyFolders(Path.GetDirectoryName(full)!, installDir);
            }
        TryDelete(ReleasePaths.Hub(installDir));
        try
        {
            if (Directory.Exists(installDir) && !Directory.EnumerateFileSystemEntries(installDir).Any()) Directory.Delete(installDir);
        }
        catch (IOException)
        {
        }
    }

    static void RemoveEmptyFolders(string folder, string root)
    {
        string top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var dir = new DirectoryInfo(folder);
        while (dir != null && dir.FullName.TrimEnd(Path.DirectorySeparatorChar).Length > top.Length &&
               dir.FullName.StartsWith(top, StringComparison.OrdinalIgnoreCase))
        {
            if (!dir.Exists || dir.EnumerateFileSystemInfos().Any()) break;
            dir.Delete();
            dir = dir.Parent;
        }
    }

    static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>The download speed over the last three seconds, and the time left at that speed.</summary>
public sealed class SpeedMeter
{
    readonly Queue<(long ticks, long bytes)> samples = new();
    readonly object gate = new();
    long sum;

    public void Add(long bytes)
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            samples.Enqueue((now, bytes));
            sum += bytes;
            while (samples.Count > 1 && now - samples.Peek().ticks > 3000) sum -= samples.Dequeue().bytes;
        }
    }

    public double BytesPerSecond
    {
        get
        {
            lock (gate)
            {
                if (samples.Count < 2) return 0;
                long span = Math.Max(500, Environment.TickCount64 - samples.Peek().ticks);
                return sum * 1000.0 / span;
            }
        }
    }

    public TimeSpan? Remaining(long bytesLeft)
    {
        double speed = BytesPerSecond;
        return speed > 1 ? TimeSpan.FromSeconds(bytesLeft / speed) : null;
    }
}
