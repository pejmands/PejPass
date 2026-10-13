using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PejPass.UpdateSigner.Tests;

public sealed class SignerIntegrationTests
{
    [Fact]
    public void Run_SignsVerifiesAndRejectsTamperedManifest()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var keyPath = Path.Combine(directory, "signer-key.pem");
            var manifestPath = Path.Combine(directory, "update.json");
            var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(manifestPath, CreateManifest());

            Assert.Equal(
                0,
                Signer.Run([manifestPath, keyPath, "--public-key", publicKey]));

            using (var document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var signature = document.RootElement.GetProperty("signature").GetString();
                Assert.False(string.IsNullOrWhiteSpace(signature));
            }

            Assert.Equal(
                0,
                Signer.Run([manifestPath, "--verify-only", "--public-key", publicKey]));

            var tampered = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            tampered["version"] = "1.0.1";
            File.WriteAllText(manifestPath, tampered.ToJsonString());

            Assert.Equal(
                1,
                Signer.Run([manifestPath, "--verify-only", "--public-key", publicKey]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_WithUntrustedSigningKey_DoesNotModifyManifest()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var keyPath = Path.Combine(directory, "signer-key.pem");
            var manifestPath = Path.Combine(directory, "update.json");
            var originalManifest = CreateManifest();

            File.WriteAllText(keyPath, signingKey.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(manifestPath, originalManifest);

            var trustedPublicKey = Convert.ToBase64String(trustedKey.ExportSubjectPublicKeyInfo());

            Assert.Equal(
                1,
                Signer.Run([manifestPath, keyPath, "--public-key", trustedPublicKey]));

            Assert.Equal(originalManifest, File.ReadAllText(manifestPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PejPass.UpdateSigner.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateManifest() =>
        $$"""{"version":"1.0.0","released":"October 13, 2026","downloadUrl":"https://example.test/PejPass-1.0.0.zip","sha256":"{{new string('a', 64)}}"}""";
}
