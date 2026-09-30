using System.Text;

namespace CoreEngine.Hub.Tests;

public class ReleaseTests
{
    [Theory]
    [InlineData("CoreEngine.exe")]
    [InlineData("CoreEngine_Data/Managed/Assembly-CSharp.dll")]
    [InlineData("tools/arduino/data/packages/arduino/hardware/avr/1.8.6/cores/arduino/main.cpp")]
    [InlineData("a file with spaces.txt")]
    public void GoodPathsAreAllowed(string path) => Assert.Null(ReleasePaths.Problem(path));

    [Theory]
    [InlineData("")]
    [InlineData("../evil.exe")]
    [InlineData("data/../../evil.exe")]
    [InlineData("./x")]
    [InlineData("/Windows/System32/x.dll")]
    [InlineData("C:/Windows/x.dll")]
    [InlineData("C:x")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    [InlineData("file.txt:stream")]
    [InlineData("NUL")]
    [InlineData("data/con.txt")]
    [InlineData("name.")]
    [InlineData(".hub/state.json")]
    [InlineData("a?b")]
    public void UnsafePathsAreRefused(string path)
    {
        Assert.NotNull(ReleasePaths.Problem(path));
        Assert.Throws<InvalidDataException>(() => ReleasePaths.Resolve(Path.GetTempPath(), path));
    }

    [Fact]
    public void AReleaseIsTrustedOnlyWithItsKeyAndUnchanged()
    {
        var (privatePem, publicPem) = ReleaseSigning.NewKey();
        var (_, otherPublic) = ReleaseSigning.NewKey();
        byte[] manifest = Encoding.UTF8.GetBytes("{\"version\":\"1.0\"}");
        byte[] signature = Encoding.ASCII.GetBytes(ReleaseSigning.Sign(manifest, privatePem));
        Assert.True(ReleaseSigning.Verify(manifest, signature, new[] { publicPem }));
        Assert.True(ReleaseSigning.Verify(manifest, signature, new[] { otherPublic, publicPem })); // a second key during a change of keys
        Assert.False(ReleaseSigning.Verify(manifest, signature, new[] { otherPublic }));
        byte[] changed = (byte[])manifest.Clone();
        changed[5] ^= 1;
        Assert.False(ReleaseSigning.Verify(changed, signature, new[] { publicPem }));
        Assert.False(ReleaseSigning.Verify(manifest, Encoding.ASCII.GetBytes("not base64 at all!"), new[] { publicPem }));
        Assert.Equal(publicPem.Trim(), ReleaseSigning.PublicOf(privatePem).Trim());
    }

    [Fact]
    public void AManifestIsCheckedBeforeAnythingIsWritten()
    {
        string hash = new string('a', 64);
        ReleaseManifest Make(params (string path, string sha)[] files) => new()
        {
            Version = "1.0",
            Exe = "Game.exe",
            Files = files.Select(f => new ReleaseFile { Path = f.path, Size = 1, Sha256 = f.sha, Packed = 1 }).ToList(),
        };
        Make(("Game.exe", hash)).Validate();
        Assert.Throws<InvalidDataException>(() => Make(("Game.exe", hash), ("../x", hash)).Validate());
        Assert.Throws<InvalidDataException>(() => Make(("Game.exe", hash), ("game.EXE", hash)).Validate()); // twice, Windows sees one file
        Assert.Throws<InvalidDataException>(() => Make(("Game.exe", "ABC")).Validate());
        Assert.Throws<InvalidDataException>(() => Make(("Other.exe", hash)).Validate()); // the program is not in the release
        var future = Make(("Game.exe", hash));
        future.Format = 2;
        Assert.Throws<InvalidDataException>(() => future.Validate());
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes("{ not json")));
    }

    [Fact]
    public async Task AnUnsignedOrForeignReleaseIsRefused()
    {
        using var world = new World();
        world.Publish("1.0", world.Build(("Game.exe", "program")));
        var (_, stranger) = ReleaseSigning.NewKey();
        var client = new ReleaseClient(ReleaseSource.From(world.Release), new[] { stranger });
        await Assert.ThrowsAsync<ReleaseNotTrustedException>(() => client.LatestAsync(CancellationToken.None));
        var none = new ReleaseClient(ReleaseSource.From(world.Release), Array.Empty<string>());
        await Assert.ThrowsAsync<ReleaseNotTrustedException>(() => none.LatestAsync(CancellationToken.None));
        // the manifest changed after signing (a server serving a doctored list)
        string manifest = Path.Combine(world.Release, ReleaseManifest.FileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("1.0", "9.9"));
        await Assert.ThrowsAsync<ReleaseNotTrustedException>(() => world.Client().LatestAsync(CancellationToken.None));
    }
}
