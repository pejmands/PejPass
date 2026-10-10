using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using System.IO;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateInstallInputValidationTests
{
    [Fact]
    public void ApplyPortableUpdateAndRestart_RejectsInvalidTargetVersion()
    {
        var packagePath = CreateTemporaryPackage();

        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                UpdateService.ApplyPortableUpdateAndRestart(
                    packagePath,
                    "latest",
                    new string('A', 64)));

            Assert.Equal("targetVersion", exception.ParamName);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public void ApplyPortableUpdateAndRestart_RejectsInvalidExpectedHash()
    {
        var packagePath = CreateTemporaryPackage();

        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                UpdateService.ApplyPortableUpdateAndRestart(
                    packagePath,
                    "1.2.3",
                    "not-a-hash"));

            Assert.Equal("expectedSha256", exception.ParamName);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public void ValidateUpdateManifest_RejectsInvalidVersion()
    {
        var manifest = new UpdateManifest
        {
            Version = "latest"
        };

        var exception = Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateManifest(manifest));

        Assert.Contains("invalid version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/PejPass.zip", true)]
    [InlineData("http://example.com/PejPass.zip", false)]
    [InlineData("https://user:password@example.com/PejPass.zip", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    public void TryValidateHttpsUrl_RejectsInvalidOrUnsafeUrls(string value, bool expected)
    {
        Assert.Equal(expected, UpdateService.TryValidateHttpsUrl(value));
    }

    [Fact]
    public void IsValidSha256_RequiresExactly64HexCharacters()
    {
        Assert.True(UpdateService.IsValidSha256(new string('A', 64)));
        Assert.False(UpdateService.IsValidSha256(new string('A', 63)));
        Assert.False(UpdateService.IsValidSha256(new string('G', 64)));
    }

    [Fact]
    public void EscapeCmd_ProtectsPercentSignsInInstallPaths()
    {
        Assert.Equal(@"C:\Apps\100%%\PejPass", UpdateService.EscapeCmd(@"C:\Apps\100%\PejPass"));
    }

    private static string CreateTemporaryPackage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PejPass-test-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, []);
        return path;
    }
}
