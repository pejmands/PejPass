using System.IO;
using System.Security.Cryptography;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreAtomicSaveTests : IDisposable
{
    private readonly string _directory;
    private readonly string _vaultPath;

    public VaultStoreAtomicSaveTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "PejPass.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);

        _vaultPath = Path.Combine(_directory, "test.pejpass");
    }

    [Fact]
    public async Task SaveAsync_WhenEncryptionFails_KeepsPreviousVaultIntact()
    {
        var originalPassword = "OriginalPassword1!";
        var originalVault = CreateVault("Original");

        var crypto = new TestCryptoService();
        var store = new VaultStore(crypto, new FileMover());

        await store.CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault);

        crypto.FailEncryption = true;

        var updatedVault = CreateVault("Updated");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(
                _vaultPath,
                originalPassword,
                updatedVault));

        crypto.FailEncryption = false;

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword);

        Assert.Equal("Original", restoredVault.Name);
        Assert.Single(restoredVault.Entries);
        Assert.Equal(
            originalVault.Entries[0].Id,
            restoredVault.Entries[0].Id);
    }

    [Fact]
    public async Task SaveAsync_WhenFileMoveFails_KeepsPreviousVaultIntact()
    {
        var originalPassword = "OriginalPassword1!";
        var originalVault = CreateVault("Original");

        var crypto = new TestCryptoService();
        var fileMover = new FailingFileMover();
        var store = new VaultStore(crypto, fileMover);

        await store.CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault);

        var updatedVault = CreateVault("Updated");

        await Assert.ThrowsAsync<IOException>(() =>
            store.SaveAsync(
                _vaultPath,
                originalPassword,
                updatedVault));

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword);

        Assert.Equal("Original", restoredVault.Name);
        Assert.Single(restoredVault.Entries);
        Assert.Equal(
            originalVault.Entries[0].Id,
            restoredVault.Entries[0].Id);
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenEncryptionFails_KeepsPreviousVaultAndPasswordIntact()
    {
        var originalPassword = "OriginalPassword1!";
        var newPassword = "NewPassword2@";
        var originalVault = CreateVault("Original");

        var crypto = new TestCryptoService();
        var store = new VaultStore(crypto, new FileMover());
        var vaultService = new VaultService(store);

        await store.CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault);

        crypto.FailEncryption = true;

        var updatedVault = CreateVault("Updated");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vaultService.ChangeMasterPasswordAsync(
                _vaultPath,
                originalPassword,
                newPassword,
                updatedVault));

        crypto.FailEncryption = false;

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword);

        Assert.Equal("Original", restoredVault.Name);
        Assert.Single(restoredVault.Entries);
        Assert.Equal(
            originalVault.Entries[0].Id,
            restoredVault.Entries[0].Id);

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            store.OpenAsync(
                _vaultPath,
                newPassword));
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenFileMoveFails_KeepsPreviousVaultAndPasswordIntact()
    {
        var originalPassword = "OriginalPassword1!";
        var newPassword = "NewPassword2@";
        var originalVault = CreateVault("Original");

        var crypto = new TestCryptoService();
        var fileMover = new FailingFileMover();
        var store = new VaultStore(crypto, fileMover);
        var vaultService = new VaultService(store);

        await store.CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault);

        var updatedVault = CreateVault("Updated");

        await Assert.ThrowsAsync<IOException>(() =>
            vaultService.ChangeMasterPasswordAsync(
                _vaultPath,
                originalPassword,
                newPassword,
                updatedVault));

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword);

        Assert.Equal("Original", restoredVault.Name);
        Assert.Single(restoredVault.Entries);
        Assert.Equal(
            originalVault.Entries[0].Id,
            restoredVault.Entries[0].Id);

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            store.OpenAsync(
                _vaultPath,
                newPassword));
    }

    private static Vault CreateVault(string name)
    {
        var vault = new Vault
        {
            Name = name
        };

        vault.AddEntry(new VaultEntry
        {
            Title = "Test Entry",
            Username = "user@example.com",
            Password = "Secret123!"
        });

        return vault;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(
                    _directory,
                    recursive: true);
        }
        catch
        {
        }
    }

    private sealed class TestCryptoService : ICryptoService
    {
        private readonly ICryptoService _inner = new CryptoService();

        public bool FailEncryption { get; set; }

        public ICryptoService Inner => _inner;

        public byte[] DeriveKey(
            string masterPassword,
            byte[] salt) =>
            Inner.DeriveKey(
                masterPassword,
                salt);

        public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
            byte[] plaintext,
            byte[] key)
        {
            if (FailEncryption)
                throw new InvalidOperationException(
                    "Simulated encryption failure.");

            return Inner.Encrypt(
                plaintext,
                key);
        }

        public byte[] Decrypt(
            byte[] ciphertext,
            byte[] nonce,
            byte[] tag,
            byte[] key) =>
            Inner.Decrypt(
                ciphertext,
                nonce,
                tag,
                key);

        public byte[] GenerateSalt(int length = 16) =>
            Inner.GenerateSalt(length);

        public void ZeroMemory(byte[] data) =>
            Inner.ZeroMemory(data);
    }

    private sealed class FailingFileMover : IFileMover
    {
        public void Move(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            throw new IOException(
                "Simulated file move failure.");
        }
    }
}
