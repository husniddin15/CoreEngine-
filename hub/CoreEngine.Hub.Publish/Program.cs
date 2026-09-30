using System.Diagnostics;
using System.Text.Json;
using CoreEngine.Hub;

// CoreEngine.Hub.Publish: releases for CoreEngine Hub (docs/adr/ADR-0010-hub-launcher.md).
//
//   keygen  [--key <file>]
//       A new release key. The private half goes to the file (by default %USERPROFILE%\.coreengine\hub\release-key.pem,
//       never into the repository) and the public half is printed, for hub/CoreEngine.Hub.Core/ReleaseKeys.cs.
//
//   publish --build <game build folder> --out <release folder> --version <version>
//           [--toolchain <tools/arduino folder>] [--exe <program>] [--notes <notes.json>] [--key <file>]
//       Writes (or adds to) a release: the game's files without Unity's do-not-ship folders, the Arduino toolchain
//       under tools/arduino with neutral settings, a manifest and its signature. The folder can then be copied to any
//       web server; a new version written into the same folder adds only the files that changed.
//
//   check   --release <release folder> [--full]
//       Checks a release as the Hub will: the signature against ReleaseKeys.cs, then every blob's size (with --full,
//       every blob unpacked and hashed).

return Tool.Run(args);

static class Tool
{
    static readonly string DefaultKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".coreengine", "hub", "release-key.pem");

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage();
            var options = Options(args.Skip(1).ToArray());
            return args[0] switch
            {
                "keygen" => KeyGen(options),
                "publish" => Publish(options),
                "check" => Check(options),
                _ => Usage(),
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    static int Usage()
    {
        Console.Error.WriteLine("usage: keygen [--key <file>] | publish --build <dir> --out <dir> --version <v> [--toolchain <dir>] [--exe <name>] [--notes <file>] [--key <file>] | check --release <dir> [--full]");
        return 2;
    }

    static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new ArgumentException($"unexpected \"{args[i]}\"");
            string name = args[i][2..];
            options[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
        }
        return options;
    }

    static string Need(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new ArgumentException($"--{name} is needed");

    // ------------------------------------------------------------------ keygen

    static int KeyGen(Dictionary<string, string> options)
    {
        string path = options.TryGetValue("key", out var given) && given.Length > 0 ? given : DefaultKey;
        if (File.Exists(path)) throw new IOException($"{path} exists already: a release key is never overwritten (every Hub trusts it).");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var (privatePem, publicPem) = ReleaseSigning.NewKey();
        File.WriteAllText(path, privatePem);
        Console.WriteLine($"The private release key is in {path}. Keep it secret and keep a copy somewhere safe:");
        Console.WriteLine("without it no update can be signed for the Hubs that trust it.");
        Console.WriteLine("Its public half, for hub/CoreEngine.Hub.Core/ReleaseKeys.cs:");
        Console.WriteLine(publicPem);
        return 0;
    }

    // ------------------------------------------------------------------ publish

    static int Publish(Dictionary<string, string> options)
    {
        string build = Path.GetFullPath(Need(options, "build"));
        string release = Path.GetFullPath(Need(options, "out"));
        string version = Need(options, "version");
        string keyPath = options.TryGetValue("key", out var key) && key.Length > 0 ? key : DefaultKey;
        if (!File.Exists(keyPath)) throw new IOException($"no release key at {keyPath}: make one with keygen first");
        string privatePem = File.ReadAllText(keyPath);

        var files = GameFiles(build).ToList();
        string exe = options.TryGetValue("exe", out var named) && named.Length > 0 ? named
            : files.Select(f => f.ReleasePath).FirstOrDefault(p => !p.Contains('/') && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException("no program found in the build: name it with --exe");
        string? neutral = null;
        if (options.TryGetValue("toolchain", out var tools) && tools.Length > 0)
        {
            neutral = Path.Combine(Path.GetTempPath(), "coreengine-arduino-cli-" + Guid.NewGuid().ToString("N") + ".yaml");
            File.WriteAllText(neutral, NeutralToolchainSettings);
            files.AddRange(ToolchainFiles(Path.GetFullPath(tools), neutral));
        }
        var notes = options.TryGetValue("notes", out var notesPath) && notesPath.Length > 0
            ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(notesPath)) ?? new()
            : DefaultNotes(version);

        var watch = Stopwatch.StartNew();
        try
        {
            var result = ReleaseWriter.Write(files, release, version, exe, notes, privatePem);
            var m = result.Manifest;
            Console.WriteLine($"{m.Product} {m.Version}: {m.Files.Count} files, {Mb(m.Size)} installed, {Mb(m.Packed)} to download the first time;");
            Console.WriteLine($"{result.NewBlobs} new blobs ({Mb(result.NewBytes)}) written to {release} in {watch.Elapsed.TotalSeconds:F0} s; program {m.Exe}.");
            string publicPem = ReleaseSigning.PublicOf(privatePem).Trim();
            if (!ReleaseKeys.Trusted.Any(k => k.Trim() == publicPem))
                Console.WriteLine("note: this key is not in ReleaseKeys.cs, so the Hub will not trust this release.");
        }
        finally
        {
            if (neutral != null) File.Delete(neutral);
        }
        return 0;
    }

    /// <summary>The game's files: everything in the build but Unity's do-not-ship folders and debug symbols.</summary>
    static IEnumerable<ReleaseWriter.Input> GameFiles(string build)
    {
        foreach (string path in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(build, path).Replace('\\', '/');
            string top = relative.Split('/')[0];
            if (top.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.Ordinal) ||
                top.EndsWith("_BurstDebugInformation_DoNotShip", StringComparison.Ordinal) ||
                top.Equals(ReleasePaths.HubFolder, StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return new ReleaseWriter.Input(path, relative);
        }
    }

    /// <summary>
    /// The Arduino toolchain the game compiles with (ADR-0003: bundled, offline), where the game looks for it: beside
    /// its data folder, under tools/arduino. Only the program and its data go; downloads, caches and test builds stay.
    /// </summary>
    static IEnumerable<ReleaseWriter.Input> ToolchainFiles(string tools, string neutralSettings)
    {
        foreach (string part in new[] { "bin", "data" })
        {
            string folder = Path.Combine(tools, part);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"the toolchain has no {part} folder: {folder}");
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                string relative = part + "/" + Path.GetRelativePath(folder, path).Replace('\\', '/');
                if (relative.StartsWith("data/tmp/", StringComparison.Ordinal) || relative.StartsWith("data/staging/", StringComparison.Ordinal)) continue;
                yield return new ReleaseWriter.Input(path, "tools/arduino/" + relative);
            }
        }
        yield return new ReleaseWriter.Input(neutralSettings, "tools/arduino/arduino-cli.yaml");
    }

    /// <summary>
    /// The toolchain's settings as shipped: no folders of the computer it was published on. The game writes
    /// arduino-cli.local.yaml for the folder it is installed in (ArduinoCliCompiler.ConfigFor).
    /// </summary>
    const string NeutralToolchainSettings =
        "board_manager:\n  additional_urls: []\nbuild_cache:\n  path: cache\ndirectories:\n  data: data\n  downloads: staging\n  user: user\nlocale: en\nupdater:\n  enable_notification: false\n";

    static Dictionary<string, List<string>> DefaultNotes(string version) => new()
    {
        ["en"] = new() { $"CoreEngine {version}." },
        ["uz"] = new() { $"CoreEngine {version}." },
        ["ru"] = new() { $"CoreEngine {version}." },
    };

    // ------------------------------------------------------------------ check

    static int Check(Dictionary<string, string> options)
    {
        string release = Path.GetFullPath(Need(options, "release"));
        bool full = options.ContainsKey("full");
        byte[] bytes = File.ReadAllBytes(Path.Combine(release, ReleaseManifest.FileName));
        byte[] signature = File.ReadAllBytes(Path.Combine(release, ReleaseManifest.SignatureName));
        bool trusted = ReleaseKeys.Trusted.Length > 0 && ReleaseSigning.Verify(bytes, signature, ReleaseKeys.Trusted);
        var manifest = ReleaseManifest.Parse(bytes);
        int bad = 0;
        foreach (var content in manifest.Files.GroupBy(f => f.Sha256).Select(g => g.First()))
        {
            string blob = Path.Combine(release, ReleaseManifest.BlobFolder, content.Sha256);
            if (!File.Exists(blob) || new FileInfo(blob).Length != content.Packed)
            {
                Console.WriteLine($"missing or wrong size: {content.Path}");
                bad++;
                continue;
            }
            if (!full) continue;
            using var input = File.OpenRead(blob);
            using var brotli = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 16];
            int read;
            long size = 0;
            while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                size += read;
            }
            if (size != content.Size || Convert.ToHexStringLower(sha.GetHashAndReset()) != content.Sha256)
            {
                Console.WriteLine($"damaged: {content.Path}");
                bad++;
            }
        }
        Console.WriteLine($"{manifest.Product} {manifest.Version}: {manifest.Files.Count} files; signature trusted by this Hub: {(trusted ? "yes" : "NO")}; " +
                          $"blobs {(bad == 0 ? "all good" : bad + " bad")}{(full ? " (unpacked and hashed)" : "")}.");
        return trusted && bad == 0 ? 0 : 1;
    }

    static string Mb(long bytes) => $"{bytes / 1048576.0:F1} MB";
}
