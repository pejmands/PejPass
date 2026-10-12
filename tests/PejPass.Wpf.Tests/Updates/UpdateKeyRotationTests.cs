using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateKeyRotationTests
{
    private static UpdateManifest Unsigned() => new()
    {
        Version = "1.2.0",
        Released = "October 8, 2026",
        DownloadUrl = "https://example.com/PejPass-1.2.0.zip",
        Sha256 = new string('a', 64)
    };

    private static string Sign(ECDsa key, UpdateManifest manifest) =>
        Convert.ToBase64String(key.SignData(
            UpdateSignatureService.BuildSigningPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));

    private static string Pub(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static UpdateManifest Signed(string? signature, params string[] additional)
    {
        var baseManifest = Unsigned();

        return new UpdateManifest
        {
            Version = baseManifest.Version,
            Released = baseManifest.Released,
            DownloadUrl = baseManifest.DownloadUrl,
            Sha256 = baseManifest.Sha256,
            Signature = signature,
            AdditionalSignatures = [.. additional]
        };
    }

    [Fact]
    public void AcceptsSignatureFromAnyTrustedKey()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Signed(Sign(k2, Unsigned()));

        Assert.True(UpdateSignatureService.VerifyAgainstKeys(manifest, [Pub(k1), Pub(k2)]));
        Assert.False(UpdateSignatureService.VerifyAgainstKeys(manifest, [Pub(k1)]));
    }

    [Fact]
    public void DualSignedManifest_IsAcceptedByClientsTrustingEitherKey()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Signed(Sign(oldKey, Unsigned()), Sign(newKey, Unsigned()));

        Assert.True(UpdateSignatureService.VerifyAgainstKeys(manifest, [Pub(oldKey)]));
        Assert.True(UpdateSignatureService.VerifyAgainstKeys(manifest, [Pub(newKey)]));
    }

    [Fact]
    public void RejectsUnknownKey()
    {
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Signed(Sign(stranger, Unsigned()), Sign(stranger, Unsigned()));

        Assert.False(UpdateSignatureService.VerifyAgainstKeys(manifest, [Pub(trusted)]));
    }

    [Fact]
    public void RejectsTamperedPayloadEvenWithValidSignatures()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var original = Unsigned();

        var tampered = new UpdateManifest
        {
            Version = "9.9.9",
            Released = original.Released,
            DownloadUrl = original.DownloadUrl,
            Sha256 = original.Sha256,
            Signature = Sign(k1, original),
            AdditionalSignatures = [Sign(k2, original)]
        };

        Assert.False(UpdateSignatureService.VerifyAgainstKeys(tampered, [Pub(k1), Pub(k2)]));
    }

    [Fact]
    public void MalformedKeyInTheList_DoesNotBlockAValidKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Signed(Sign(key, Unsigned()));

        Assert.True(UpdateSignatureService.VerifyAgainstKeys(manifest, ["not-base64", "", Pub(key)]));
    }

    [Fact]
    public void GarbageAdditionalSignature_IsIgnored_ButNeverAccepted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.True(UpdateSignatureService.VerifyAgainstKeys(
            Signed(Sign(key, Unsigned()), "not-base64"),
            [Pub(key)]));

        Assert.False(UpdateSignatureService.VerifyAgainstKeys(
            Signed("also-garbage", "not-base64"),
            [Pub(key)]));
    }

    [Fact]
    public void EmptyKeyListOrMissingSignature_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(UpdateSignatureService.VerifyAgainstKeys(Signed(Sign(key, Unsigned())), []));
        Assert.False(UpdateSignatureService.VerifyAgainstKeys(Signed(null), [Pub(key)]));
    }
}
