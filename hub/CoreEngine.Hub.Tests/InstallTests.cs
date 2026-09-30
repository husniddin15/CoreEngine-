namespace CoreEngine.Hub.Tests;

public class InstallTests
{
    [Fact]
    public async Task AFirstDownloadInstallsEveryFileAndRecordsIt()
    {
        using var world = new World();
        string build = world.Build(
            ("Game.exe", "the program"),
            ("Game_Data/level0", "a level"),
            ("Game_Data/copy of level0", "a level"), // the same content: one blob for both
            ("Game_Data/empty", ""),
            ("tools/arduino/bin/arduino-cli.exe", "the compiler"));
        var published = world.Publish("1.0", build);
        Assert.Equal(4, published.NewBlobs);

        var plan = await world.Update();
        Assert.False(plan.Installed);
        Assert.Equal(5, plan.Fetch.Count);
        world.AssertInstalledAs(build);
        var state = InstallState.Load(world.Install)!;
        Assert.Equal("1.0", state.Version);
        Assert.Equal("Game.exe", state.Exe);
        Assert.Equal(5, state.Files.Count);
        Assert.False(Directory.Exists(ReleasePaths.Hub(world.Install, "staging")));
        Assert.False(Directory.Exists(ReleasePaths.Hub(world.Install, "downloads")));

        var again = UpdatePlanner.Plan(await world.Client().LatestAsync(default), world.Install);
        Assert.True(again.UpToDate);
        Assert.True(again.Installed);
    }

    [Fact]
    public async Task AnUpdateDownloadsOnlyWhatChangedAndRemovesWhatWentAway()
    {
        using var world = new World();
        string v1 = world.Build(("Game.exe", "the program 1"), ("Game_Data/a", "same in both"), ("Game_Data/old", "only in 1"));
        world.Publish("1.0", v1);
        await world.Update();
        File.WriteAllText(Path.Combine(world.Install, "saved by the player.txt"), "not the Hub's"); // stays

        string v2 = world.Build(("Game.exe", "the program 2"), ("Game_Data/a", "same in both"), ("Game_Data/new/b", "only in 2"));
        var published = world.Publish("2.0", v2);
        Assert.Equal(2, published.NewBlobs); // the release adds only what changed

        var source = new CountingSource(world.Release);
        var plan = await world.Update(source);
        Assert.Equal(new[] { "Game.exe", "Game_Data/new/b" }, plan.Fetch.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(new[] { "Game_Data/old" }, plan.Remove);
        Assert.Equal(2, source.Downloads);
        Assert.Equal(plan.DownloadBytes, source.Bytes);
        world.AssertInstalledAs(v2, "saved by the player.txt");
        Assert.Equal("2.0", InstallState.Load(world.Install)!.Version);
    }

    [Fact]
    public async Task APausedDownloadCarriesOnFromWhereItStopped()
    {
        using var world = new World();
        string build = world.Build(("Game.exe", World.Noise(1_500_000, 1)), ("Game_Data/a", World.Noise(1_500_000, 2)), ("Game_Data/b", World.Noise(1_500_000, 3)));
        var manifest = world.Publish("1.0", build).Manifest;

        var source = new CountingSource(world.Release);
        using (var pause = new CancellationTokenSource())
        {
            var progress = new SyncProgress<InstallProgress>(p =>
            {
                if (p.Phase == InstallPhase.Downloading && p.Done > 1_200_000) pause.Cancel();
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Update(source, pause.Token, progress, limit: 3_000_000));
        }
        long before = source.Bytes;
        Assert.InRange(before, 1_200_000, manifest.Packed - 1);
        Assert.False(File.Exists(Path.Combine(world.Install, "Game.exe"))); // nothing is put in place before everything is downloaded

        await world.Update(source); // Resume
        world.AssertInstalledAs(build);
        Assert.True(source.Bytes < manifest.Packed + 400_000, $"downloaded {source.Bytes} of {manifest.Packed}: it started again");
    }

    [Fact]
    public async Task RepairPutsBackAChangedFile()
    {
        using var world = new World();
        string build = world.Build(("Game.exe", "the program"), ("Game_Data/level", "the level as published"));
        world.Publish("1.0", build);
        await world.Update();
        string level = Path.Combine(world.Install, "Game_Data", "level");
        var written = File.GetLastWriteTimeUtc(level);
        File.WriteAllText(level, "THE LEVEL as published"); // the same length, the same time: only reading it shows it
        File.SetLastWriteTimeUtc(level, written);

        var manifest = await world.Client().LatestAsync(default);
        Assert.True(UpdatePlanner.Plan(manifest, world.Install).UpToDate);
        var repair = UpdatePlanner.Plan(manifest, world.Install, verify: true);
        Assert.Equal(new[] { "Game_Data/level" }, repair.Fetch.Select(f => f.Path));
        await new Installer(ReleaseSource.From(world.Release)).RunAsync(repair, null, default);
        world.AssertInstalledAs(build);
    }

    [Fact]
    public async Task ADamagedDownloadIsRefusedAndTheGameStaysAsItWas()
    {
        using var world = new World();
        string v1 = world.Build(("Game.exe", "the program 1"));
        world.Publish("1.0", v1);
        await world.Update();
        string v2 = world.Build(("Game.exe", "the program 2, with a change"));
        var manifest = world.Publish("2.0", v2).Manifest;
        string blob = Path.Combine(world.Release, ReleaseManifest.BlobPath(manifest.Files[0].Sha256));
        byte[] bytes = File.ReadAllBytes(blob);
        bytes[^1] ^= 0x5A;
        File.WriteAllBytes(blob, bytes); // changed on the server

        await Assert.ThrowsAsync<InvalidDataException>(() => world.Update());
        world.AssertInstalledAs(v1);
        Assert.Equal("1.0", InstallState.Load(world.Install)!.Version);
    }

    [Fact]
    public async Task UninstallTakesOnlyTheHubsFiles()
    {
        using var world = new World();
        string build = world.Build(("Game.exe", "the program"), ("Game_Data/level", "a level"));
        world.Publish("1.0", build);
        await world.Update();
        Directory.CreateDirectory(Path.Combine(world.Install, "Screenshots"));
        File.WriteAllText(Path.Combine(world.Install, "Screenshots", "mine.png"), "the player's");

        Installer.Uninstall(world.Install);
        Assert.False(File.Exists(Path.Combine(world.Install, "Game.exe")));
        Assert.False(Directory.Exists(Path.Combine(world.Install, "Game_Data")));
        Assert.False(Directory.Exists(ReleasePaths.Hub(world.Install)));
        Assert.True(File.Exists(Path.Combine(world.Install, "Screenshots", "mine.png")));

        File.Delete(Path.Combine(world.Install, "Screenshots", "mine.png"));
        Directory.Delete(Path.Combine(world.Install, "Screenshots"));
        Installer.Uninstall(world.Install);
        Assert.False(Directory.Exists(world.Install));
    }

    [Fact]
    public async Task AGameFoundOnTheDiskIsAdoptedByReadingIt()
    {
        using var world = new World();
        string build = world.Build(("Game.exe", "the program"), ("Game_Data/level", "a level"));
        world.Publish("1.0", build);
        // copied by hand (a USB stick), no record of the Hub's
        foreach (string file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(world.Install, Path.GetRelativePath(build, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var source = new CountingSource(world.Release);
        var plan = await world.Update(source, verify: true);
        Assert.True(plan.Installed);
        Assert.Empty(plan.Fetch);
        Assert.Equal(0, source.Downloads);
        Assert.Equal("1.0", InstallState.Load(world.Install)!.Version);
    }
}

/// <summary>An IProgress that calls back at once, on the reporting thread (Progress&lt;T&gt; posts to a queue).</summary>
sealed class SyncProgress<T> : IProgress<T>
{
    readonly Action<T> report;

    public SyncProgress(Action<T> report) => this.report = report;

    public void Report(T value) => report(value);
}
