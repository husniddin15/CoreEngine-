using System.Security.Cryptography;
using System.Text;

namespace CoreEngine.Hub;

/// <summary>
/// Signatures on releases. Whoever publishes a release signs manifest.json with the release key (ECDSA on P-256 over
/// SHA-256); the Hub carries the matching public key (<see cref="ReleaseKeys"/>) and refuses any manifest it does not
/// verify. Each file's SHA-256 is in the signed manifest, so a changed file on the server, or on the way, is refused
/// too: only what the key's owner published is ever installed or started.
/// </summary>
public static class ReleaseSigning
{
    /// <summary>The signature of a manifest's bytes, as the text of manifest.json.sig.</summary>
    public static string Sign(byte[] manifest, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        byte[] signature = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }

    /// <summary>Whether one of the public keys signed the manifest's bytes.</summary>
    public static bool Verify(byte[] manifest, byte[] signatureFile, IEnumerable<string> publicKeysPem)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureFile).Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        foreach (string pem in publicKeysPem)
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            if (key.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return true;
        }
        return false;
    }

    /// <summary>A new release key: the private half to keep secret, the public half to build into the Hub.</summary>
    public static (string privatePem, string publicPem) NewKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>The public half of a private key.</summary>
    public static string PublicOf(string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return key.ExportSubjectPublicKeyInfoPem();
    }
}
