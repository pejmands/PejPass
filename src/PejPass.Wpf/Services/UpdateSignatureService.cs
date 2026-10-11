using PejPass.Application.Security;
using PejPass.Wpf.Records;
using System.Security.Cryptography;

namespace PejPass.Wpf.Services;

/// <summary>
/// Builds and verifies the authenticated portion of the remote update manifest.
/// </summary>
public static class UpdateSignatureService
{
    private const string TrustedPublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExWfWThPa8RrjVMZbubArG4H7vwym/rXR3HcXdl2lajZ7lfT0HfnNYItF7JEQmdPB/3Frhx2t42H8VosD+QrvoA==";

    internal static byte[] BuildSigningPayload(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return UpdateSigningPayload.Build(
            manifest.Version,
            manifest.Released,
            manifest.DownloadUrl,
            manifest.Sha256);
    }

    public static bool VerifyManifestSignature(UpdateManifest manifest) =>
        VerifyManifestSignature(manifest, TrustedPublicKeyBase64);

    public static bool VerifyManifestSignature(
        UpdateManifest manifest,
        string? publicKeyBase64)
    {
        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.Signature)
            || string.IsNullOrWhiteSpace(publicKeyBase64))
            return false;

        try
        {
            var publicKey = Convert.FromBase64String(publicKeyBase64.Trim());
            var signature = Convert.FromBase64String(manifest.Signature.Trim());

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);

            if (bytesRead != publicKey.Length)
                return false;

            var payload = BuildSigningPayload(manifest);

            return ecdsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
