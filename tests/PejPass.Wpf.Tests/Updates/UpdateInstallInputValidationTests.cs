using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using System.IO;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateInstallInputValidationTests
{
    [Fact]
    public async Task ApplyPortableUpdateAndRestartAsync_RejectsInvalidTargetVersion()
    {
        var packagePath = CreateTemporaryPackage();

        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                UpdateService.ApplyPortableUpdateAndRestartAsync(
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
    public async Task ApplyPortableUpdateAndRestartAsync_RejectsInvalidExpectedHash()
    {
        var packagePath = CreateTemporaryPackage();

        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                UpdateService.ApplyPortableUpdateAndRestartAsync(
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
    public async Task ApplyPortableUpdateAndRestartAsync_WhenPackageIsMissing_ThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"PejPass-missing-{Guid.NewGuid():N}.zip");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            UpdateService.ApplyPortableUpdateAndRestartAsync(
                missing,
                "1.2.3",
                new string('a', 64)));
    }

    [Fact]
    public void BuildApplyScript_UsesSafePowerShellLiteralsAndCoordinatesBrowserHostShutdown()
    {
        var script = UpdateService.BuildApplyScript(
            appDir: @"C:\Apps\100%\O'Brien\PejPass",
            sourceDir: @"C:\Temp\stage",
            exePath: @"C:\Apps\100%\O'Brien\PejPass\PejPass.exe",
            stageRoot: @"C:\Temp\stage",
            pendingPath: @"C:\Users\u\pending.txt",
            backupDir: @"C:\Temp\backup",
            processId: 4242);

        Assert.Contains(@"$AppDir = 'C:\Apps\100%\O''Brien\PejPass'", script);
        Assert.Contains("$AppProcessId = 4242", script);
        Assert.Contains("[System.IO.FileShare]::None", script);
        Assert.Contains("Get-Process -Id $AppProcessId", script);
        Assert.Contains("/MIR", script);
        Assert.Contains("if ($RollbackSucceeded)", script);
        Assert.Contains("Backup preserved at:", script);
    }

    [Fact]
    public void BrowserHostUpdateLock_ExcludesUpdaterAndNewHostDuringInstallation()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), $"PejPass-lock-{Guid.NewGuid():N}.lock");
        File.WriteAllBytes(lockPath, []);

        try
        {
            using (var hostLock = new FileStream(
                lockPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() =>
                {
                    using var updaterLock = new FileStream(
                        lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                });
            }

            using (var updaterLock = new FileStream(
                lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<IOException>(() =>
                {
                    using var newHostLock = new FileStream(
                        lockPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                });
            }
        }
        finally
        {
            File.Delete(lockPath);
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
