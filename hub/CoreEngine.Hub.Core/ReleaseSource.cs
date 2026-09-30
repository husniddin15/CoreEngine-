using System.Net;
using System.Net.Http.Headers;

namespace CoreEngine.Hub;

/// <summary>
/// Where releases come from: a web address (https://…/stable/) or, for testing and for copies passed around on a USB
/// stick, a folder. Files are read relative to it (manifest.json, blobs/…). A download goes into a ".part" file and,
/// if it is stopped (Pause, a lost connection, the Hub closed), carries on from where it was: HTTP Range requests on
/// the web, a seek in a folder.
/// </summary>
public class ReleaseSource
{
    static readonly HttpClient SharedHttp = CreateHttp();

    readonly HttpClient http;

    public ReleaseSource(Uri baseUri, HttpClient? client = null)
    {
        if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeFile)
            throw new ArgumentException("A release source is a web address or a folder.", nameof(baseUri));
        string text = baseUri.AbsoluteUri;
        BaseUri = new Uri(text.EndsWith('/') ? text : text + "/");
        http = client ?? SharedHttp;
    }

    public Uri BaseUri { get; }

    /// <summary>A source from what the player or a setting typed: a web address, a file: address or a folder.</summary>
    public static ReleaseSource From(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeFile))
            return new ReleaseSource(uri);
        return new ReleaseSource(new Uri(Path.GetFullPath(text) + Path.DirectorySeparatorChar));
    }

    static HttpClient CreateHttp()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None, // blobs are compressed already; ranges must mean bytes of the file
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // large files: each read is watched instead (ReadTimeout)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CoreEngineHub", typeof(ReleaseSource).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }

    /// <summary>How long a download may wait for its next bytes before it is given up (and can be resumed).</summary>
    public static TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    Uri Resolve(string relative) => new Uri(BaseUri, relative);

    /// <summary>A small file (the manifest, its signature) in full; at most <paramref name="maxBytes"/>.</summary>
    public virtual async Task<byte[]> GetBytesAsync(string relative, long maxBytes, CancellationToken token)
    {
        var uri = Resolve(relative);
        if (uri.IsFile)
        {
            var info = new FileInfo(uri.LocalPath);
            if (!info.Exists) throw new FileNotFoundException($"The release has no {relative}.", uri.LocalPath);
            if (info.Length > maxBytes) throw new InvalidDataException($"The release's {relative} is too large.");
            return await File.ReadAllBytesAsync(uri.LocalPath, token);
        }
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException($"The release has no {relative}.", uri.ToString());
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException($"The release's {relative} is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > maxBytes) throw new InvalidDataException($"The release's {relative} is too large.");
        }
        return memory.ToArray();
    }

    /// <summary>
    /// Downloads a file into <paramref name="partPath"/>, carrying on from what is there already, until it is
    /// <paramref name="length"/> bytes long. <paramref name="onBytes"/> hears of every new block (for progress).
    /// </summary>
    public virtual async Task DownloadAsync(string relative, string partPath, long length, Action<long> onBytes, SpeedLimit? limit, CancellationToken token)
    {
        var uri = Resolve(relative);
        long have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (have > length) // not the file it was: start again
        {
            File.Delete(partPath);
            have = 0;
        }
        if (have == length) return;

        if (uri.IsFile)
        {
            await using var source = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            if (source.Length != length) throw new InvalidDataException($"The release's {relative} is {source.Length} bytes, not {length}.");
            source.Seek(have, SeekOrigin.Begin);
            await using var target = new FileStream(partPath, FileMode.Append, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await Copy(source, target, onBytes, limit, token);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException($"The release has no {relative}.", uri.ToString());
        response.EnsureSuccessStatusCode();
        bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed && have > 0) have = 0; // the server sent it all again
        await using (var body = await response.Content.ReadAsStreamAsync(token))
        await using (var target = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            await Copy(body, target, onBytes, limit, token);
        long now = new FileInfo(partPath).Length;
        if (now != length) throw new IOException($"The download of {relative} stopped at {now} of {length} bytes.");
    }

    static async Task Copy(Stream from, Stream to, Action<long> onBytes, SpeedLimit? limit, CancellationToken token)
    {
        var buffer = new byte[1 << 16];
        while (true)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(ReadTimeout);
            int read;
            try
            {
                read = await from.ReadAsync(buffer, wait.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new IOException($"No data for {ReadTimeout.TotalSeconds:F0} s: the connection seems lost.");
            }
            if (read == 0) break;
            if (limit != null) await limit.TakeAsync(read, token);
            await to.WriteAsync(buffer.AsMemory(0, read), token);
            onBytes(read);
        }
    }
}

/// <summary>A download speed limit shared by all downloads at once (a token bucket); 0 means none.</summary>
public sealed class SpeedLimit
{
    readonly object gate = new();
    double tokens;
    long lastTicks = Environment.TickCount64;

    public SpeedLimit(long bytesPerSecond) => BytesPerSecond = bytesPerSecond;

    /// <summary>The limit; it can change while downloads run.</summary>
    public long BytesPerSecond { get; set; }

    public async Task TakeAsync(int bytes, CancellationToken token)
    {
        while (true)
        {
            TimeSpan wait;
            lock (gate)
            {
                long rate = BytesPerSecond;
                if (rate <= 0) return;
                long now = Environment.TickCount64;
                tokens = Math.Min(rate, tokens + (now - lastTicks) / 1000.0 * rate); // at most one second saved up
                lastTicks = now;
                if (tokens >= bytes || tokens >= rate)
                {
                    tokens -= bytes;
                    return;
                }
                wait = TimeSpan.FromSeconds(Math.Max(0.005, (Math.Min(bytes, rate) - tokens) / rate));
            }
            await Task.Delay(wait, token);
        }
    }
}
