using PejPass.Application.Security;
using PejPass.Wpf.Records;
using System.Security.Cryptography;

namespace PejPass.Wpf.Services;

/// <summary>
/// Builds and verifies the authenticated portion of the remote update manifest.
/// </summary>
public static class UpdateSignatureService
{
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
        VerifyAgainstKeys(manifest, TrustedUpdateKeys.PublicKeysBase64);

    public static bool VerifyManifestSignature(UpdateManifest manifest, string? publicKeyBase64) =>
        !string.IsNullOrWhiteSpace(publicKeyBase64) &&
        VerifyAgainstKeys(manifest, [publicKeyBase64]);

    /// <summary>
    /// True when any signature of the manifest ("signature" and "additionalSignatures")
    /// verifies with any of the given keys. Malformed keys or signatures are skipped, never fatal,
    /// so one bad entry cannot lock out the valid ones.
    /// </summary>
    internal static bool VerifyAgainstKeys(UpdateManifest manifest, IEnumerable<string> publicKeysBase64)
    {
        if (manifest is null || publicKeysBase64 is null)
            return false;

        var signatures = new List<byte[]>();
        AddSignature(signatures, manifest.Signature);

        if (manifest.AdditionalSignatures is { } additional)
        {
            foreach (var text in additional)
                AddSignature(signatures, text);
        }

        if (signatures.Count == 0)
            return false;

        var payload = BuildSigningPayload(manifest);

        foreach (var keyText in publicKeysBase64)
        {
            if (!TryImportKey(keyText, out var key))
                continue;

            using (key)
            {
                foreach (var signature in signatures)
                {
                    if (Verify(key!, payload, signature))
                        return true;
                }
            }
        }

        return false;
    }

    private static void AddSignature(List<byte[]> signatures, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            signatures.Add(Convert.FromBase64String(text.Trim()));
        }
        catch (FormatException)
        {
            // Ignore malformed additional signatures and continue checking other candidates.
        }
    }

    private static bool TryImportKey(string? text, out ECDsa? key)
    {
        key = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        ECDsa? candidate = null;

        try
        {
            var bytes = Convert.FromBase64String(text.Trim());
            candidate = ECDsa.Create();
            candidate.ImportSubjectPublicKeyInfo(bytes, out var bytesRead);

            if (bytesRead != bytes.Length)
            {
                candidate.Dispose();
                return false;
            }

            key = candidate;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            candidate?.Dispose();
            return false;
        }
    }

    private static bool Verify(ECDsa key, byte[] payload, byte[] signature)
    {
        try
        {
            return key.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
