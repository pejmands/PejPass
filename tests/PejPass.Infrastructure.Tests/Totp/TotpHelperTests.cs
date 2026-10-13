using OtpNet;
using PejPass.Infrastructure.Totp;
using System.Text;

namespace PejPass.Infrastructure.Tests.Totp;

public sealed class TotpHelperTests
{
    // RFC 6238 Appendix B test vectors use ASCII secrets encoded as Base32 for otpauth URIs.
    private const string Sha1Key = "12345678901234567890";
    private const string Sha256Key = "12345678901234567890123456789012";
    private const string Sha512Key = "1234567890123456789012345678901234567890123456789012345678901234";

    [Theory]
    [InlineData("SHA1", Sha1Key, 59L, "94287082")]
    [InlineData("SHA256", Sha256Key, 59L, "46119246")]
    [InlineData("SHA512", Sha512Key, 59L, "90693936")]
    [InlineData("SHA1", Sha1Key, 1111111109L, "07081804")]
    [InlineData("SHA256", Sha256Key, 1111111109L, "68084774")]
    [InlineData("SHA512", Sha512Key, 1111111109L, "25091201")]
    public void TryGetCode_MatchesRfc6238Vectors(
        string algorithm,
        string asciiKey,
        long unixSeconds,
        string expected)
    {
        var secret = Base32Encoding.ToString(Encoding.ASCII.GetBytes(asciiKey));
        var uri = $"otpauth://totp/test?secret={secret}&algorithm={algorithm}&digits=8";
        var time = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        Assert.True(TotpHelper.TryGetCode(uri, time, out var code, out _, out var period));
        Assert.Equal(expected, code);
        Assert.Equal(30, period);
    }

    [Fact]
    public void TryGetCode_BareSecret_UsesDefaults()
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime;

        Assert.True(TotpHelper.TryGetCode(
            "jbsw y3dp-ehpk3pxp", time, out var code, out var remaining, out var period));

        Assert.Equal(6, code.Length);
        Assert.Equal(30, period);
        Assert.Equal(1, remaining);
    }

    [Fact]
    public void TryGetCode_CustomPeriod_ReportsPeriodAndRemainingTime()
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(90).UtcDateTime;

        Assert.True(TotpHelper.TryGetCode(
            "otpauth://totp/test?secret=JBSWY3DPEHPK3PXP&period=60",
            time,
            out var code,
            out var remaining,
            out var period));

        Assert.Equal("996554", code);
        Assert.Equal(60, period);
        Assert.Equal(30, remaining);
    }

    [Fact]
    public void TryParse_AcceptsInputAtMaximumLength()
    {
        var input = new string('A', TotpHelper.MaxInputLength);

        var ok = TotpHelper.TryParse(input, out var parameters, out var error);

        Assert.True(ok, error);
        Assert.NotNull(parameters);
        Assert.Equal(TotpHelper.MaxInputLength, parameters.Secret.Length);
    }

    [Fact]
    public void TryParse_RejectsInputAboveMaximumLength()
    {
        var input = new string('A', TotpHelper.MaxInputLength + 1);

        var ok = TotpHelper.TryParse(input, out var parameters, out var error);

        Assert.False(ok);
        Assert.Null(parameters);
        Assert.Equal("The TOTP secret is too long.", error);
    }

    [Theory]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&digits=5")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&digits=9")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&digits=abc")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&period=0")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&period=301")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&encoder=steam")]
    [InlineData("otpauth://hotp/t?secret=JBSWY3DPEHPK3PXP&counter=1")]
    [InlineData("otpauth://totp/t?issuer=x")]
    [InlineData("otpauth://totp/t?secret=JBSWY3DPEHPK3PXP&digits=6&digits=8")]
    [InlineData("not base32 !!")]
    [InlineData("---")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_RejectsUnsupportedOrInvalidInput(string input)
    {
        Assert.False(TotpHelper.TryParse(input, out var parameters, out var error));
        Assert.Null(parameters);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(TotpHelper.IsValidSecret(input));
    }

    [Fact]
    public void TryParse_DefaultParameters_AreStoredAsBareSecret()
    {
        Assert.True(TotpHelper.TryParse(
            "otpauth://totp/Example:alice?secret=jbsw%20y3dp&issuer=Example&algorithm=sha1&digits=6&period=30",
            out var parameters,
            out _));

        Assert.True(parameters!.IsDefault);
        Assert.Equal("JBSWY3DP", parameters.ToStorageString());
    }

    [Fact]
    public void ToStorageString_NonDefaultParameters_RoundTrip()
    {
        Assert.True(TotpHelper.TryParse(
            "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&algorithm=SHA256&digits=8&period=60",
            out var original,
            out _));

        var stored = original!.ToStorageString();

        Assert.StartsWith("otpauth://totp/PejPass?secret=", stored);
        Assert.True(TotpHelper.TryParse(stored, out var reparsed, out _));
        Assert.Equal(original, reparsed);
    }

    [Theory]
    [InlineData("123456", "123 456")]
    [InlineData("1234567", "123 4567")]
    [InlineData("12345678", "1234 5678")]
    public void FormatForDisplay_GroupsDigits(string code, string expected)
    {
        Assert.Equal(expected, TotpHelper.FormatForDisplay(code));
    }
}
