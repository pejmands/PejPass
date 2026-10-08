using PejPass.Wpf.Records;
using System.Security.Cryptography;
using System.Text.Json;

namespace PejPass.Wpf.Services;

/// <summary>
/// Builds and verifies the authenticated portion of the remote update manifest.
/// </summary>
public static class UpdateSignatureService
{
    private const string PayloadSchema = "PejPass.UpdateSignature.v1";

    internal static byte[] BuildSigningPayload(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("schema", PayloadSchema);
        writer.WriteString("version", manifest.Version.Trim());
        writer.WriteString("released", manifest.Released?.Trim());
        writer.WriteString("downloadUrl", manifest.DownloadUrl?.Trim());
        writer.WriteString("sha256", manifest.Sha256?.Trim().ToLowerInvariant());
        writer.WriteEndObject();
        writer.Flush();

        return stream.ToArray();
    }

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
