using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreSessionKeyTests
{
    [Fact]
    public async Task SaveWithSessionKey_ReusesSaltAndCreatesFreshNonce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            using var session = await store.CreateSessionAsync(
                path,
                "master-password",
                new Vault { Name = "Before save" },
                cancellationToken);

            var before = await File.ReadAllBytesAsync(path, cancellationToken);
            var originalSalt = before.AsSpan(20, 16).ToArray();
            var originalNonce = before.AsSpan(36, 12).ToArray();

            session.Vault.Name = "After save";
            await store.SaveAsync(path, session.KeyMaterial, session.Vault, cancellationToken);

            var after = await File.ReadAllBytesAsync(path, cancellationToken);
            Assert.Equal(originalSalt, after.AsSpan(20, 16).ToArray());
            Assert.False(originalNonce.AsSpan().SequenceEqual(after.AsSpan(36, 12)));

            var reopened = await store.OpenWithKeyAsync(path, session.KeyMaterial, cancellationToken);
            Assert.Equal("After save", reopened.Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenWithKey_RejectsKeyMaterialForDifferentSalt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            using var session = await store.CreateSessionAsync(
                path,
                "master-password",
                new Vault(),
                cancellationToken);

            var key = session.KeyMaterial.CopyKey();
            var salt = session.KeyMaterial.CopySalt();
            salt[0] ^= 0x01;

            try
            {
                using var wrongMaterial = new VaultKeyMaterial(
                    key,
                    salt,
                    session.KeyMaterial.KdfParameters);

                await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
                    store.OpenWithKeyAsync(path, wrongMaterial, cancellationToken));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(salt);
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
