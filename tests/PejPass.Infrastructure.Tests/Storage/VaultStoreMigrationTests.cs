using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using System.IO;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreMigrationTests
{
    [Fact]
    public async Task OpenAsync_WhenVaultIsV1_MigratesItToV2()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(
                new CryptoService(),
                new FileMover());

            var vault = new Vault();

            await CreateLegacyV1VaultAsync(
                path,
                "password",
                vault);

            var opened = await store.OpenAsync(
                path,
                "password");

            Assert.NotNull(opened);

            var data = await File.ReadAllBytesAsync(path);

            Assert.Equal(
                2,
                data[4]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenVaultIsV2_DoesNotNeedMigration()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(
                new CryptoService(),
                new FileMover());

            await store.CreateAsync(
                path,
                "password",
                new Vault());

            var data = await File.ReadAllBytesAsync(path);

            Assert.Equal(
                2,
                data[4]);

            var opened = await store.OpenAsync(
                path,
                "password");

            Assert.NotNull(opened);

            var migratedData = await File.ReadAllBytesAsync(path);

            Assert.Equal(
                2,
                migratedData[4]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static async Task CreateLegacyV1VaultAsync(
        string path,
        string password,
        Vault vault)
    {
        const int saltLength = 16;

        var crypto = new CryptoService();
        var salt = crypto.GenerateSalt(saltLength);
        var key = crypto.DeriveKey(password, salt);

        try
        {
            var json = System.Text.Json.JsonSerializer
                .SerializeToUtf8Bytes(vault);

            var (ciphertext, nonce, tag) =
                crypto.Encrypt(
                    json,
                    key);

            await using var fs = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            await fs.WriteAsync(
                "PEJP"u8.ToArray());

            await fs.WriteAsync(
                new byte[] { 1 });

            await fs.WriteAsync(
                BitConverter.GetBytes(
                    (ushort)salt.Length));

            await fs.WriteAsync(salt);
            await fs.WriteAsync(nonce);
            await fs.WriteAsync(tag);
            await fs.WriteAsync(ciphertext);
        }
        finally
        {
            crypto.ZeroMemory(key);
        }
    }
}
