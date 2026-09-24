using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreAuthenticationTests
{
    [Fact]
    public async Task CreateAndOpenAsync_WithValidVault_Succeeds()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = CreateStore();
            var vault = new Vault();

            await store.CreateAsync(
                path,
                "password",
                vault);

            var opened = await store.OpenAsync(
                path,
                "password");

            Assert.NotNull(opened);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenSaltIsModified_ThrowsAuthenticationException()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = CreateStore();
            var vault = new Vault();

            await store.CreateAsync(
                path,
                "password",
                vault);

            var data = await File.ReadAllBytesAsync(path);

            // PEJP (4) + version (1) + salt length (2)
            const int saltOffset = 7;

            data[saltOffset] ^= 0x01;

            await File.WriteAllBytesAsync(path, data);

            await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
                store.OpenAsync(path, "password"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static VaultStore CreateStore()
    {
        return new VaultStore(
            new CryptoService(),
            new FileMover());
    }
}
