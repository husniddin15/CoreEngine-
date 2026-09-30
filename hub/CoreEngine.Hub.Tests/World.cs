using System.Text;

namespace CoreEngine.Hub.Tests;

/// <summary>
/// A publisher and a player on one disk, in a temporary folder: made-up game builds, a release folder signed with a
/// key made for the test, and a game folder with spaces in its name (as under "Program Files" or a player's name).
/// </summary>
sealed class World : IDisposable
{
    int builds;

    public World()
    {
        Root = Path.Combine(Path.GetTempPath(), "CoreEngine Hub test " + Guid.NewGuid().ToString("N")[..8]);
        Release = Path.Combine(Root, "release");
        Install = Path.Combine(Root, "My Games", "CoreEngine");
        (PrivateKey, PublicKey) = ReleaseSigning.NewKey();
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string Release { get; }
    public string Install { get; }
    public string PrivateKey { get; }
    public string PublicKey { get; }

    public string Build(params (string path, string text)[] files) =>
        Build(files.Select(f => (f.path, Encoding.UTF8.GetBytes(f.text))).ToArray());

    public string Build(params (string path, byte[] bytes)[] files)
    {
        string folder = Path.Combine(Root, "build" + ++builds);
        foreach (var (path, bytes) in files)
        {
            string full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        Directory.CreateDirectory(folder);
        return folder;
    }

    public ReleaseWriter.Result Publish(string version, string build, string exe = "Game.exe")
    {
        var inputs = Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories)
            .Select(p => new ReleaseWriter.Input(p, Path.GetRelativePath(build, p).Replace('\\', '/'))).ToList();
        var notes = new Dictionary<string, List<string>> { ["en"] = new() { "What is new in " + version } };
        return ReleaseWriter.Write(inputs, Release, version, exe, notes, PrivateKey);
    }

    public ReleaseClient Client(ReleaseSource? source = null) => new(source ?? ReleaseSource.From(Release), new[] { PublicKey });

    /// <summary>What the Hub does on Update: the newest release, the plan for the game's folder, the install.</summary>
    public async Task<UpdatePlan> Update(ReleaseSource? source = null, CancellationToken token = default,
        IProgress<InstallProgress>? progress = null, long limit = 0, bool verify = false)
    {
        source ??= ReleaseSource.From(Release);
        var manifest = await Client(source).LatestAsync(token);
        var plan = UpdatePlanner.Plan(manifest, Install, verify);
        var installer = new Installer(source);
        installer.Limit.BytesPerSecond = limit;
        await installer.RunAsync(plan, progress, token);
        return plan;
    }

    /// <summary>The game's folder holds exactly the build's files (besides the Hub's own folder and <paramref name="others"/>).</summary>
    public void AssertInstalledAs(string build, params string[] others)
    {
        var expected = Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(build, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var actual = Directory.EnumerateFiles(Install, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Install, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith(ReleasePaths.HubFolder + "/", StringComparison.Ordinal) && !others.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, actual);
        foreach (string path in expected)
            Assert.Equal(File.ReadAllBytes(Path.Combine(build, path)), File.ReadAllBytes(Path.Combine(Install, path)));
    }

    public static byte[] Noise(int bytes, int seed)
    {
        var data = new byte[bytes];
        new Random(seed).NextBytes(data); // does not compress: the download is as large as the file
        return data;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>A release folder that counts what is downloaded from it.</summary>
sealed class CountingSource : ReleaseSource
{
    long bytes;
    int downloads;

    public CountingSource(string folder) : base(new Uri(Path.GetFullPath(folder) + Path.DirectorySeparatorChar))
    {
    }

    public long Bytes => Interlocked.Read(ref bytes);
    public int Downloads => downloads;

    public override Task DownloadAsync(string relative, string partPath, long length, Action<long> onBytes, SpeedLimit? limit, CancellationToken token)
    {
        Interlocked.Increment(ref downloads);
        return base.DownloadAsync(relative, partPath, length, n =>
        {
            Interlocked.Add(ref bytes, n);
            onBytes(n);
        }, limit, token);
    }
}
