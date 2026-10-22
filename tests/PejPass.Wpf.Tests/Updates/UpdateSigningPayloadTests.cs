using PejPass.Application.Security;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using System.Text;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateSigningPayloadTests
{
    [Fact]
    public void Build_ProducesTheExactSignedFormat()
    {
        var payload = UpdateSigningPayload.Build(
            "1.2.0",
            "October 8, 2026",
            "https://example.com/PejPass-1.2.0.zip",
            new string('A', 64));

        Assert.Equal(
            "{\"schema\":\"PejPass.UpdateSignature.v1\"," +
            "\"version\":\"1.2.0\"," +
            "\"released\":\"October 8, 2026\"," +
            "\"downloadUrl\":\"https://example.com/PejPass-1.2.0.zip\"," +
            "\"sha256\":\"" + new string('a', 64) + "\"}",
            Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void Build_WithMissingOptionalFields_WritesNulls()
    {
        var payload = UpdateSigningPayload.Build("1.0.0", null, null, null);

        Assert.Equal(
            "{\"schema\":\"PejPass.UpdateSignature.v1\"," +
            "\"version\":\"1.0.0\",\"released\":null,\"downloadUrl\":null,\"sha256\":null}",
            Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void Build_TrimsValuesAndLowercasesTheHash()
    {
        var padded = UpdateSigningPayload.Build(
            " 1.0.0 ",
            " today ",
            " https://x.test/a.zip ",
            " ABCDEF ");
        var clean = UpdateSigningPayload.Build(
            "1.0.0",
            "today",
            "https://x.test/a.zip",
            "abcdef");

        Assert.Equal(clean, padded);
    }

    [Fact]
    public void AppSignatureService_UsesTheSharedPayload()
    {
        var manifest = new UpdateManifest
        {
            Version = "1.2.0",
            Released = "October 8, 2026",
            DownloadUrl = "https://example.com/PejPass-1.2.0.zip",
            Sha256 = new string('B', 64)
        };

        Assert.Equal(
            UpdateSigningPayload.Build(
                manifest.Version,
                manifest.Released,
                manifest.DownloadUrl,
                manifest.Sha256),
            UpdateSignatureService.BuildSigningPayload(manifest));
    }
}
