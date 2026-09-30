using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CoreEngine.Hub.Tests;

public class WebTests
{
    [Fact]
    public async Task TheGameComesFromAWebServerAndAPauseResumesThere()
    {
        using var world = new World();
        string build = world.Build(("Game.exe", World.Noise(1_000_000, 7)), ("Game_Data/a", World.Noise(1_000_000, 8)));
        var manifest = world.Publish("1.0", build).Manifest;
        using var server = new TinyWebServer(world.Release);
        var source = new ReleaseSource(server.Address);

        using (var pause = new CancellationTokenSource())
        {
            var progress = new SyncProgress<InstallProgress>(p =>
            {
                if (p.Phase == InstallPhase.Downloading && p.Done > 600_000) pause.Cancel();
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Update(source, pause.Token, progress, limit: 2_000_000));
        }
        await world.Update(source);
        world.AssertInstalledAs(build);
        Assert.True(server.RangeRequests > 0, "the download started again instead of carrying on");
        Assert.Equal("1.0", (await new ReleaseClient(source, new[] { world.PublicKey }).LatestAsync(default)).Version);
        Assert.True(manifest.Packed > 1_900_000);
    }

    [Fact]
    public async Task AServerThatCannotBeReachedIsReportedAsSuch()
    {
        using var world = new World();
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        } // closed again: nothing listens there
        var client = world.Client(new ReleaseSource(new Uri($"http://127.0.0.1:{port}/stable/")));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.LatestAsync(default));
    }
}

/// <summary>A web server on this computer for the tests: files from a folder, with Range for resumed downloads.</summary>
sealed class TinyWebServer : IDisposable
{
    readonly TcpListener listener;
    readonly string root;
    readonly CancellationTokenSource stop = new();
    int rangeRequests;

    public TinyWebServer(string root)
    {
        this.root = root;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(Loop);
    }

    public int Port { get; }
    public Uri Address => new($"http://127.0.0.1:{Port}/");
    public int RangeRequests => rangeRequests;

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                string? request = await reader.ReadLineAsync();
                if (request == null) return;
                string path = Uri.UnescapeDataString(request.Split(' ')[1].TrimStart('/'));
                long from = 0;
                bool ranged = false;
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
                    if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        from = long.Parse(header["Range: bytes=".Length..].TrimEnd('-'));
                        ranged = true;
                        Interlocked.Increment(ref rangeRequests);
                    }
                string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(file))
                {
                    await Write(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return;
                }
                long length = new FileInfo(file).Length;
                string head = ranged
                    ? $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {from}-{length - 1}/{length}\r\nContent-Length: {length - from}\r\n"
                    : $"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\n";
                await Write(stream, head + "Accept-Ranges: bytes\r\nConnection: close\r\n\r\n");
                await using var input = File.OpenRead(file);
                input.Seek(from, SeekOrigin.Begin);
                await input.CopyToAsync(stream);
            }
            catch (IOException)
            {
                // the Hub stopped reading (a pause)
            }
        }
    }

    static Task Write(Stream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}
