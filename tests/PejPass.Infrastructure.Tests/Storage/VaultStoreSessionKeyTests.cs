using PejPass.Application.Security;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
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
            Assert.False(originalNonce.SequenceEqual(after.Skip(36).Take(12)));

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
    public async Task SaveWithNewPassword_RotatesKeyMaterialAndRejectsOldMaterial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            using var session = await store.CreateSessionAsync(
                path,
                "old-password",
                new Vault { Name = "Rotate key" },
                cancellationToken);

            var oldSalt = session.KeyMaterial.CopySalt();
            using var newMaterial = await store.SaveWithNewPasswordAsync(
                path,
                "new-password",
                session.Vault,
                session.KeyMaterial.KdfParameters,
                cancellationToken);

            try
            {
                Assert.False(oldSalt.SequenceEqual(newMaterial.Salt.ToArray()));

                await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
                    store.OpenWithKeyAsync(path, session.KeyMaterial, cancellationToken));

                var reopened = await store.OpenWithKeyAsync(path, newMaterial, cancellationToken);
                Assert.Equal("Rotate key", reopened.Name);

                using var passwordOpened = await store.OpenSessionAsync(
                    path,
                    "new-password",
                    cancellationToken);
                Assert.Equal("Rotate key", passwordOpened.Vault.Name);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(oldSalt);
            }

            await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
                store.OpenAsync(path, "old-password", cancellationToken));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ChangeMasterPassword_UpgradesKdfWhenDefaultsDominateCurrentParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        var olderParameters = new Argon2Parameters(32768, 2, 2);

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            using var session = await store.CreateSessionAsync(
                path,
                "old-password",
                new Vault { Name = "KDF upgrade", KdfParameters = olderParameters },
                cancellationToken);

            using var newMaterial = await new VaultService(store).ChangeMasterPasswordAsync(
                path,
                "old-password",
                "vQ7!nT4#zP9@kR2$wX6",
                session.Vault,
                session.KeyMaterial,
                cancellationToken);

            Assert.Equal(Argon2Parameters.Default, session.Vault.KdfParameters);
            Assert.Equal(Argon2Parameters.Default, newMaterial.KdfParameters);

            using var reopened = await store.OpenSessionAsync(
                path,
                "vQ7!nT4#zP9@kR2$wX6",
                cancellationToken);
            Assert.Equal(Argon2Parameters.Default, reopened.KeyMaterial.KdfParameters);
            Assert.Equal(Argon2Parameters.Default, reopened.Vault.KdfParameters);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ChangeMasterPassword_DoesNotDowngradeAnyHigherKdfParameter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        var strongerParameters = new Argon2Parameters(65536, 3, 8);

        try
        {
            var store = new VaultStore(new CryptoService(), new FileMover());
            using var session = await store.CreateSessionAsync(
                path,
                "old-password",
                new Vault { Name = "Preserve KDF", KdfParameters = strongerParameters },
                cancellationToken);

            using var newMaterial = await new VaultService(store).ChangeMasterPasswordAsync(
                path,
                "old-password",
                "vQ7!nT4#zP9@kR2$wX6",
                session.Vault,
                session.KeyMaterial,
                cancellationToken);

            Assert.Equal(strongerParameters, session.Vault.KdfParameters);
            Assert.Equal(strongerParameters, newMaterial.KdfParameters);
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
