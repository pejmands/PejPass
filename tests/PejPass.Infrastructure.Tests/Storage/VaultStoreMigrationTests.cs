using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using System.IO;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreMigrationTests
{
    [Fact]
    public async Task OpenSessionAsync_WhenVaultIsV1_MigratesToV3AndPreservesSessionKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            var vault = new Vault { Name = "Legacy V1" };

            await CreateLegacyV1VaultAsync(path, "password", vault, cancellationToken);

            using var session = await store.OpenSessionAsync(path, "password", cancellationToken);
            Assert.Equal("Legacy V1", session.Vault.Name);

            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            Assert.Equal(3, data[4]);

            var reopened = await store.OpenWithKeyAsync(path, session.KeyMaterial, cancellationToken);
            Assert.Equal("Legacy V1", reopened.Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenSessionAsync_WhenVaultIsV2_MigratesToV3AndPreservesSessionKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            var vault = new Vault { Name = "Legacy V2" };

            await CreateLegacyV2VaultAsync(path, "password", vault, cancellationToken);

            var originalData = await File.ReadAllBytesAsync(path, cancellationToken);
            Assert.Equal(2, originalData[4]);

            using var session = await store.OpenSessionAsync(path, "password", cancellationToken);
            Assert.Equal("Legacy V2", session.Vault.Name);

            var migratedData = await File.ReadAllBytesAsync(path, cancellationToken);
            Assert.Equal(3, migratedData[4]);

            var reopened = await store.OpenWithKeyAsync(path, session.KeyMaterial, cancellationToken);
            Assert.Equal("Legacy V2", reopened.Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenVaultUsesCustomArgon2Parameters_PreservesThemAcrossSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        var parameters = new Argon2Parameters(32768, 2, 2);

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            await store.CreateAsync(path, "password", new Vault { KdfParameters = parameters }, cancellationToken);

            var opened = await store.OpenAsync(path, "password", cancellationToken);
            Assert.Equal(parameters, opened.KdfParameters);

            opened.Name = "Updated";
            await store.SaveAsync(path, "password", opened, cancellationToken);

            var reopened = await store.OpenAsync(path, "password", cancellationToken);
            Assert.Equal(parameters, reopened.KdfParameters);
            Assert.Equal("Updated", reopened.Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static async Task CreateLegacyV2VaultAsync(
        string path,
        string password,
        Vault vault,
        CancellationToken cancellationToken)
    {
        const int saltLength = 16;
        const byte version = 2;
        var crypto = new CryptoService();
        var salt = crypto.GenerateSalt(saltLength);
        var key = crypto.DeriveKey(password, salt);

        try
        {
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(vault);
            using var associatedDataStream = new MemoryStream();
            associatedDataStream.Write("PEJP"u8);
            associatedDataStream.WriteByte(version);
            associatedDataStream.Write(BitConverter.GetBytes((ushort)salt.Length));
            associatedDataStream.Write(salt);

            var (ciphertext, nonce, tag) = crypto.Encrypt(json, key, associatedDataStream.ToArray());

            await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await fs.WriteAsync("PEJP"u8.ToArray(), cancellationToken);
            await fs.WriteAsync(new byte[] { version }, cancellationToken);
            await fs.WriteAsync(BitConverter.GetBytes((ushort)salt.Length), cancellationToken);
            await fs.WriteAsync(salt, cancellationToken);
            await fs.WriteAsync(nonce, cancellationToken);
            await fs.WriteAsync(tag, cancellationToken);
            await fs.WriteAsync(ciphertext, cancellationToken);
        }
        finally
        {
            crypto.ZeroMemory(key);
        }
    }

    private static async Task CreateLegacyV1VaultAsync(
        string path,
        string password,
        Vault vault,
        CancellationToken cancellationToken)
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
                "PEJP"u8.ToArray(),
                cancellationToken);

            await fs.WriteAsync(
                new byte[] { 1 },
                cancellationToken);

            await fs.WriteAsync(
                BitConverter.GetBytes(
                    (ushort)salt.Length),
                cancellationToken);

            await fs.WriteAsync(salt, cancellationToken);
            await fs.WriteAsync(nonce, cancellationToken);
            await fs.WriteAsync(tag, cancellationToken);
            await fs.WriteAsync(ciphertext, cancellationToken);
        }
        finally
        {
            crypto.ZeroMemory(key);
        }
    }
}
