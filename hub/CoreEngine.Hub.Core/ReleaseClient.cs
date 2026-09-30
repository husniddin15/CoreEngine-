namespace CoreEngine.Hub;

/// <summary>
/// The Hub's view of a release source: fetches manifest.json and its signature, and accepts the manifest only when
/// one of the trusted keys signed it (<see cref="ReleaseKeys"/>) and it passes <see cref="ReleaseManifest.Validate"/>.
/// </summary>
public sealed class ReleaseClient
{
    const long MaxManifestBytes = 16L * 1024 * 1024;

    public ReleaseClient(ReleaseSource source, IReadOnlyList<string>? trustedKeys = null)
    {
        Source = source;
        TrustedKeys = trustedKeys ?? ReleaseKeys.Trusted;
    }

    public ReleaseSource Source { get; }
    public IReadOnlyList<string> TrustedKeys { get; }

    /// <summary>The newest release, checked; throws when there is none, it cannot be reached or it is not signed.</summary>
    public async Task<ReleaseManifest> LatestAsync(CancellationToken token)
    {
        byte[] manifest = await Source.GetBytesAsync(ReleaseManifest.FileName, MaxManifestBytes, token);
        byte[] signature = await Source.GetBytesAsync(ReleaseManifest.SignatureName, 4096, token);
        if (TrustedKeys.Count == 0 || !ReleaseSigning.Verify(manifest, signature, TrustedKeys))
            throw new ReleaseNotTrustedException("The release is not signed with CoreEngine's key, so the Hub will not install it.");
        return ReleaseManifest.Parse(manifest);
    }
}

/// <summary>A release whose signature did not verify: never installed, never started.</summary>
public sealed class ReleaseNotTrustedException : Exception
{
    public ReleaseNotTrustedException(string message) : base(message)
    {
    }
}
