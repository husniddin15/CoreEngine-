namespace CoreEngine.Hub;

/// <summary>
/// The public halves of the keys that sign CoreEngine's releases (<see cref="ReleaseSigning"/>). The private half
/// never enters the repository: `CoreEngine.Hub.Publish keygen` writes it to %USERPROFILE%\.coreengine\hub\release-key.pem
/// on the publisher's computer. A second key can be added here before the first is retired, so Hubs already out in
/// the world keep trusting the releases through the change.
/// </summary>
public static class ReleaseKeys
{
    public static readonly string[] Trusted =
    {
        // The first release key, made 2026-09-29.
        "-----BEGIN PUBLIC KEY-----\n" +
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4g//TGaKATz0cvG59XVhd569MZWm\n" +
        "wQsOyXhbZUeU0hSfQEoxkJHMyi0VK5QtNQ0D6Ruz0EtrB3wb4TveudKnWg==\n" +
        "-----END PUBLIC KEY-----",
    };
}
