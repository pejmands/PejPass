using PejPass.Infrastructure.Totp;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Services;

public sealed class QrTotpImportTests
{
    [Theory]
    [InlineData(8192, 1024, false)]
    [InlineData(4096, 2049, true)]
    [InlineData(8193, 1, true)]
    [InlineData(0, 100, true)]
    public void IsImageTooLarge_EnforcesPixelAndDimensionLimits(int width, int height, bool expected)
    {
        Assert.Equal(expected, QrTotpImport.IsImageTooLarge(width, height));
    }

    [Fact]
    public void TryParseTotpPayload_DefaultParameters_StoresBareSecret()
    {
        var ok = QrTotpImport.TryParseTotpPayload(
            "otpauth://totp/Example:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example",
            out var result,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("JBSWY3DPEHPK3PXP", result!.Secret);
        Assert.Equal("Example", result.Issuer);
        Assert.Equal("alice@example.com", result.Account);
    }

    [Theory]
    [InlineData("Example%252FService", "Example%2FService")]
    [InlineData("Example%20Inc", "Example Inc")]
    public void TryParseTotpPayload_DecodesIssuerExactlyOnce(string encodedIssuer, string expectedIssuer)
    {
        var payload = $"otpauth://totp/alice?secret=JBSWY3DPEHPK3PXP&issuer={encodedIssuer}";

        var ok = QrTotpImport.TryParseTotpPayload(payload, out var result, out var error);

        Assert.True(ok, error);
        Assert.Equal(expectedIssuer, result!.Issuer);
    }

    [Fact]
    public void TryParseTotpPayload_NonDefaultParameters_KeepsThemInStoredSecret()
    {
        var ok = QrTotpImport.TryParseTotpPayload(
            "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&algorithm=SHA256&digits=8&period=60",
            out var result,
            out var error);

        Assert.True(ok, error);
        Assert.StartsWith("otpauth://totp/PejPass?secret=", result!.Secret);

        Assert.True(TotpHelper.TryParse(result.Secret, out var parameters, out _));
        Assert.Equal(TotpAlgorithm.Sha256, parameters!.Algorithm);
        Assert.Equal(8, parameters.Digits);
        Assert.Equal(60, parameters.PeriodSeconds);
    }

    [Theory]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&encoder=steam")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=10")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    [InlineData("otpauth://hotp/x?secret=JBSWY3DPEHPK3PXP&counter=0")]
    public void TryParseTotpPayload_UnsupportedSettings_AreRejectedInsteadOfProducingWrongCodes(string payload)
    {
        Assert.False(QrTotpImport.TryParseTotpPayload(payload, out var result, out var error));
        Assert.Null(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
