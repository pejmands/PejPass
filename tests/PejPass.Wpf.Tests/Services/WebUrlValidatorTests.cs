using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Services;

public class WebUrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("example.com", true)]
    [InlineData("  example.com  ", true)]
    [InlineData("example.com:8080", true)]
    [InlineData("localhost:5000", true)]
    [InlineData("router", true)]
    [InlineData("nas", true)]
    [InlineData("router:5000", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("data:text/html,test", false)]
    [InlineData("powershell:Start-Process", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("https://", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("user./l,,,\";:::example.com", false)]
    [InlineData("example.com/path", true)]
    [InlineData("https://-invalid.com", false)]
    [InlineData("https://example..com", false)]
    [InlineData("https://example.com:99999", false)]

    public void TryNormalize_ValidatesUrl(
        string url,
        bool expected)
    {
        var result = WebUrlValidator.TryNormalize(
            url,
            out var normalizedUrl);

        Assert.Equal(expected, result);

        if (expected)
        {
            Assert.True(
                Uri.TryCreate(
                    normalizedUrl,
                    UriKind.Absolute,
                    out var uri));

            Assert.True(
                uri!.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps);
        }
        else
        {
            Assert.Equal(string.Empty, normalizedUrl);
        }
    }

    [Theory]
    [InlineData("router", "https://router/")]
    [InlineData("nas:5000", "https://nas:5000/")]
    public void TryNormalize_PreservesSingleLabelLocalHostnames(string url, string expected)
    {
        Assert.True(WebUrlValidator.TryNormalize(url, out var normalizedUrl));
        Assert.Equal(expected, normalizedUrl);
    }

    [Fact]
    public void TryNormalize_AddsHttpsToDomain()
    {
        var result = WebUrlValidator.TryNormalize(
            "example.com",
            out var normalizedUrl);

        Assert.True(result);
        Assert.Equal("https://example.com/", normalizedUrl);
    }
}
