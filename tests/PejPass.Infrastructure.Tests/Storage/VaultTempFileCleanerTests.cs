using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using System.Security.Cryptography;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultTempFileCleanerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "PejPass.TempCleanerTests",
        Guid.NewGuid().ToString("N"));

    private string VaultPath => Path.Combine(_directory, "vault.pejpass");

    public VaultTempFileCleanerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void DeleteStale_RemovesOldTempFileOfThisVault()
    {
        var temp = CreateTemp(".vault.pejpass", age: TimeSpan.FromHours(2));

        Assert.Equal(1, VaultTempFileCleaner.DeleteStale(VaultPath));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void DeleteStale_KeepsRecentTempFile()
    {
        var temp = CreateTemp(".vault.pejpass", age: TimeSpan.FromMinutes(5));

        Assert.Equal(0, VaultTempFileCleaner.DeleteStale(VaultPath));
        Assert.True(File.Exists(temp));
    }

    [Fact]
    public void DeleteStale_KeepsFilesThatDoNotMatchThePattern()
    {
        var otherVault = CreateTemp(".other.pejpass", age: TimeSpan.FromDays(3));
        var badGuid = Path.Combine(_directory, ".vault.pejpass." + new string('z', 32) + ".tmp");
        var shortGuid = Path.Combine(_directory, ".vault.pejpass.abc.tmp");
        var plainTmp = Path.Combine(_directory, "notes.tmp");

        foreach (var file in new[] { badGuid, shortGuid, plainTmp, VaultPath })
        {
            File.WriteAllBytes(file, [1]);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-3));
        }

        Assert.Equal(0, VaultTempFileCleaner.DeleteStale(VaultPath));

        Assert.True(File.Exists(otherVault));
        Assert.True(File.Exists(badGuid));
        Assert.True(File.Exists(shortGuid));
        Assert.True(File.Exists(plainTmp));
        Assert.True(File.Exists(VaultPath));
    }

    [Fact]
    public void DeleteStale_WhenDirectoryIsMissing_DoesNotThrow()
    {
        var missing = Path.Combine(_directory, "nope", "vault.pejpass");

        Assert.Equal(0, VaultTempFileCleaner.DeleteStale(missing));
    }

    [Fact]
    public async Task OpenSessionAsync_AfterSuccessfulUnlock_RemovesStaleTempFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new VaultStore(new CryptoService(), new FileMover());

        using (await store.CreateSessionAsync(
            VaultPath,
            "Password123!",
            new Vault(),
            cancellationToken))
        {
        }

        var stale = CreateTemp(".vault.pejpass", age: TimeSpan.FromHours(3));

        using var session = await store.OpenSessionAsync(
            VaultPath,
            "Password123!",
            cancellationToken);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(VaultPath));
    }

    [Fact]
    public async Task OpenSessionAsync_WithWrongPassword_DoesNotDeleteAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new VaultStore(new CryptoService(), new FileMover());

        using (await store.CreateSessionAsync(
            VaultPath,
            "Password123!",
            new Vault(),
            cancellationToken))
        {
        }

        var stale = CreateTemp(".vault.pejpass", age: TimeSpan.FromHours(3));

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            store.OpenSessionAsync(VaultPath, "wrong-password", cancellationToken));

        Assert.True(File.Exists(stale));
    }

    [Fact]
    public async Task OpenAsync_DoesNotRemoveStaleTempFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new VaultStore(new CryptoService(), new FileMover());

        using (await store.CreateSessionAsync(
            VaultPath,
            "Password123!",
            new Vault(),
            cancellationToken))
        {
        }

        var stale = CreateTemp(".vault.pejpass", age: TimeSpan.FromHours(3));

        var vault = await store.OpenAsync(
            VaultPath,
            "Password123!",
            cancellationToken);

        Assert.NotNull(vault);
        Assert.True(File.Exists(stale));
    }

    private string CreateTemp(string prefixName, TimeSpan age)
    {
        var path = Path.Combine(_directory, $"{prefixName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }
}
