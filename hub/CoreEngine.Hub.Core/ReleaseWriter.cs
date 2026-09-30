using System.IO.Compression;
using System.Text;

namespace CoreEngine.Hub;

/// <summary>
/// Writes a release (<see cref="ReleaseManifest"/>) into a folder that any web server can then hold: each file's
/// content Brotli-compressed under blobs/, named by its SHA-256 and written only when it is not there yet (so each
/// new version adds only what changed), then manifest.json and its signature. CoreEngine.Hub.Publish runs it on a
/// game build; the tests run it on small made-up builds.
/// </summary>
public static class ReleaseWriter
{
    /// <summary>A file to publish: where it is on this disk, and where it goes in the game's folder.</summary>
    public sealed record Input(string SourcePath, string ReleasePath);

    /// <summary>What a release holds and what writing it took.</summary>
    public sealed record Result(ReleaseManifest Manifest, int NewBlobs, long NewBytes);

    public static Result Write(IReadOnlyList<Input> files, string releaseDir, string version, string exe,
        Dictionary<string, List<string>> notes, string privateKeyPem, int quality = 6)
    {
        string blobs = Path.Combine(releaseDir, ReleaseManifest.BlobFolder);
        Directory.CreateDirectory(blobs);
        var entries = new ReleaseFile[files.Count];
        int newBlobs = 0;
        long newBytes = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
        Parallel.For(0, files.Count, options, i =>
        {
            var input = files[i];
            string? problem = ReleasePaths.Problem(input.ReleasePath);
            if (problem != null) throw new InvalidDataException($"\"{input.ReleasePath}\" cannot be in a release ({problem}).");
            string sha = Hashing.Sha256OfFile(input.SourcePath);
            long size = new FileInfo(input.SourcePath).Length;
            string blob = Path.Combine(blobs, sha);
            if (!File.Exists(blob))
            {
                string temp = blob + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var source = new FileStream(input.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
                using (var brotli = new BrotliStream(target, new BrotliCompressionOptions { Quality = quality }))
                    source.CopyTo(brotli, 1 << 20);
                try
                {
                    File.Move(temp, blob);
                    Interlocked.Increment(ref newBlobs);
                    Interlocked.Add(ref newBytes, new FileInfo(blob).Length);
                }
                catch (IOException) when (File.Exists(blob))
                {
                    File.Delete(temp); // another file with the same content got there first
                }
            }
            entries[i] = new ReleaseFile { Path = input.ReleasePath, Size = size, Sha256 = sha, Packed = new FileInfo(blob).Length };
        });

        var now = DateTime.UtcNow;
        var manifest = new ReleaseManifest
        {
            Version = version,
            Published = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc),
            Exe = exe,
            Files = entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList(),
            Notes = notes,
        };
        manifest.Validate();
        byte[] bytes = manifest.ToBytes();
        string signature = ReleaseSigning.Sign(bytes, privateKeyPem);
        WriteWhole(Path.Combine(releaseDir, ReleaseManifest.FileName), bytes);
        WriteWhole(Path.Combine(releaseDir, ReleaseManifest.SignatureName), Encoding.ASCII.GetBytes(signature + "\n"));
        return new Result(manifest, newBlobs, newBytes);
    }

    static void WriteWhole(string path, byte[] bytes)
    {
        string temp = path + ".new";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }
}
