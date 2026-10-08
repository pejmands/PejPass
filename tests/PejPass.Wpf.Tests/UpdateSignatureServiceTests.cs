using PejPass.Wpf.Records;
using System.Security.Cryptography;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

public sealed class UpdateSignatureServiceTests
{
    [Fact]
    public void VerifyManifestSignature_WithValidSignature_ReturnsTrue()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest();
        var signature = key.SignData(
            UpdateSignatureService.BuildSigningPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        manifest = new UpdateManifest
        {
            Version = manifest.Version,
            Released = manifest.Released,
            DownloadUrl = manifest.DownloadUrl,
            Sha256 = manifest.Sha256,
            Signature = Convert.ToBase64String(signature)
        };

        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        Assert.True(
            UpdateSignatureService.VerifyManifestSignature(manifest, publicKey));
    }

    [Fact]
    public void VerifyManifestSignature_WhenVersionChanges_ReturnsFalse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = CreateSignedManifest(key);
        var tampered = new UpdateManifest
        {
            Version = "9.9.9",
            Released = signed.Released,
            DownloadUrl = signed.DownloadUrl,
            Sha256 = signed.Sha256,
            Signature = signed.Signature
        };

        Assert.False(
            UpdateSignatureService.VerifyManifestSignature(
                tampered,
                ExportPublicKey(key)));
    }

    [Fact]
    public void VerifyManifestSignature_WhenDownloadUrlChanges_ReturnsFalse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = CreateSignedManifest(key);
        var tampered = new UpdateManifest
        {
            Version = signed.Version,
            Released = signed.Released,
            DownloadUrl = "https://example.com/evil.zip",
            Sha256 = signed.Sha256,
            Signature = signed.Signature
        };

        Assert.False(
            UpdateSignatureService.VerifyManifestSignature(
                tampered,
                ExportPublicKey(key)));
    }

    [Fact]
    public void VerifyManifestSignature_WhenHashChanges_ReturnsFalse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = CreateSignedManifest(key);
        var tampered = new UpdateManifest
        {
            Version = signed.Version,
            Released = signed.Released,
            DownloadUrl = signed.DownloadUrl,
            Sha256 = new string('0', 64),
            Signature = signed.Signature
        };

        Assert.False(
            UpdateSignatureService.VerifyManifestSignature(
                tampered,
                ExportPublicKey(key)));
    }

    [Fact]
    public void VerifyManifestSignature_WithInvalidInputs_ReturnsFalse()
    {
        var manifest = CreateManifest();

        Assert.False(UpdateSignatureService.VerifyManifestSignature(manifest, null));
        Assert.False(
            UpdateSignatureService.VerifyManifestSignature(
                new UpdateManifest
                {
                    Version = manifest.Version,
                    Released = manifest.Released,
                    DownloadUrl = manifest.DownloadUrl,
                    Sha256 = manifest.Sha256,
                    Signature = "not-base64"
                },
                "not-base64"));
    }

    private static UpdateManifest CreateSignedManifest(ECDsa key)
    {
        var manifest = CreateManifest();
        var signature = key.SignData(
            UpdateSignatureService.BuildSigningPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        return new UpdateManifest
        {
            Version = manifest.Version,
            Released = manifest.Released,
            DownloadUrl = manifest.DownloadUrl,
            Sha256 = manifest.Sha256,
            Signature = Convert.ToBase64String(signature)
        };
    }

    private static string ExportPublicKey(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static UpdateManifest CreateManifest() =>
        new()
        {
            Version = "1.2.0",
            Released = "October 8, 2026",
            DownloadUrl = "https://example.com/PejPass-1.2.0.zip",
            Sha256 = new string('a', 64)
        };
}
