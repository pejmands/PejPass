using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using System.Security.Cryptography;

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
    public async Task CreateSessionAsync_WhenFileMoveFails_LeavesNoVaultAndNoTempFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new VaultStore(new TestCryptoService(), new FailingFileMover());

        await Assert.ThrowsAsync<IOException>(() =>
            store.CreateSessionAsync(
                _vaultPath,
                "Password123!",
                CreateVault("New"),
                cancellationToken));

        Assert.False(File.Exists(_vaultPath));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CreateSessionAsync_WhenFileAlreadyExists_DoesNotOverwriteItAndLeavesNoTempFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        byte[] existing = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(_vaultPath, existing, cancellationToken);

        var store = new VaultStore(new TestCryptoService(), new FileMover());

        await Assert.ThrowsAsync<IOException>(() =>
            store.CreateSessionAsync(
                _vaultPath,
                "Password123!",
                CreateVault("New"),
                cancellationToken));

        Assert.Equal(existing, await File.ReadAllBytesAsync(_vaultPath, cancellationToken));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CreateSessionAsync_OnSuccess_LeavesOnlyTheVaultFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new VaultStore(new TestCryptoService(), new FileMover());

        using var session = await store.CreateSessionAsync(
            _vaultPath,
            "Password123!",
            CreateVault("New"),
            cancellationToken);

        Assert.Equal(_vaultPath, Assert.Single(Directory.GetFiles(_directory)));

        using var reopened = await store.OpenSessionAsync(
            _vaultPath,
            "Password123!",
            cancellationToken);
        Assert.Equal("New", reopened.Vault.Name);
    }

    [Fact]
    public async Task OpenAsync_WhenVaultFileExceedsMaximumSize_RejectsBeforeReadingHeader()
    {
        var oversizedLength = 128L * 1024 * 1024 + 1;
        await using (var file = new FileStream(
            _vaultPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
            file.SetLength(oversizedLength);
        }

        var store = new VaultStore(
            new TestCryptoService(),
            new FileMover());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.OpenAsync(
                _vaultPath,
                "Password123!",
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_WhenEncryptionFails_KeepsPreviousVaultIntact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalPassword = "OriginalPassword1!";
        var originalVault = CreateVault("Original");

        var crypto = new TestCryptoService();
        var store = new VaultStore(crypto, new FileMover());

        await store.CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault,
            cancellationToken);

        crypto.FailEncryption = true;

        var updatedVault = CreateVault("Updated");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(
                _vaultPath,
                originalPassword,
                updatedVault,
                cancellationToken));

        crypto.FailEncryption = false;

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword,
            ct: cancellationToken);

        Assert.Equal("Original", restoredVault.Name);
        var restoredEntry = Assert.Single(restoredVault.Entries);
        Assert.Equal(originalVault.Entries[0].Id, restoredEntry.Id);
    }

    [Fact]
    public async Task SaveAsync_WhenFileMoveFails_KeepsPreviousVaultIntact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalPassword = "OriginalPassword1!";
        var originalVault = CreateVault("Original");

        var crypto = new TestCryptoService();

        await new VaultStore(crypto, new FileMover()).CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault,
            cancellationToken);

        var store = new VaultStore(crypto, new FailingFileMover());
        var updatedVault = CreateVault("Updated");

        await Assert.ThrowsAsync<IOException>(() =>
            store.SaveAsync(
                _vaultPath,
                originalPassword,
                updatedVault,
                cancellationToken));

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword,
            ct: cancellationToken);

        Assert.Equal("Original", restoredVault.Name);
        var restoredEntry = Assert.Single(restoredVault.Entries);
        Assert.Equal(originalVault.Entries[0].Id, restoredEntry.Id);
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenEncryptionFails_KeepsPreviousVaultAndPasswordIntact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalPassword = "OriginalPassword1!";
        var newPassword = "vQ7!nT4#zP9@kR2$wX6";
        var legacyParameters = new Argon2Parameters(32768, 2, 2);
        var originalVault = CreateVault("Original");
        originalVault.KdfParameters = legacyParameters;

        var crypto = new TestCryptoService();
        var store = new VaultStore(crypto, new FileMover());
        var vaultService = new VaultService(store);

        await store.CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault,
            cancellationToken);

        crypto.FailEncryption = true;

        var updatedVault = CreateVault("Updated");
        updatedVault.KdfParameters = legacyParameters;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vaultService.ChangeMasterPasswordAsync(
                _vaultPath,
                originalPassword,
                newPassword,
                updatedVault,
                cancellationToken));

        Assert.Equal(legacyParameters, updatedVault.KdfParameters);
        crypto.FailEncryption = false;

        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword,
            ct: cancellationToken);

        Assert.Equal("Original", restoredVault.Name);
        var restoredEntry = Assert.Single(restoredVault.Entries);
        Assert.Equal(originalVault.Entries[0].Id, restoredEntry.Id);

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            store.OpenAsync(
                _vaultPath,
                newPassword,
                ct: cancellationToken));
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenFileMoveFails_KeepsPreviousVaultAndPasswordIntact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalPassword = "OriginalPassword1!";
        var newPassword = "vQ7!nT4#zP9@kR2$wX6";
        var legacyParameters = new Argon2Parameters(32768, 2, 2);
        var originalVault = CreateVault("Original");
        originalVault.KdfParameters = legacyParameters;

        var crypto = new TestCryptoService();

        await new VaultStore(crypto, new FileMover()).CreateAsync(
            _vaultPath,
            originalPassword,
            originalVault,
            cancellationToken);

        var store = new VaultStore(crypto, new FailingFileMover());
        var vaultService = new VaultService(store);
        var updatedVault = CreateVault("Updated");
        updatedVault.KdfParameters = legacyParameters;

        await Assert.ThrowsAsync<IOException>(() =>
            vaultService.ChangeMasterPasswordAsync(
                _vaultPath,
                originalPassword,
                newPassword,
                updatedVault,
                cancellationToken));

        Assert.Equal(legacyParameters, updatedVault.KdfParameters);
        var restoredVault = await store.OpenAsync(
            _vaultPath,
            originalPassword,
            ct: cancellationToken);

        Assert.Equal("Original", restoredVault.Name);
        var restoredEntry = Assert.Single(restoredVault.Entries);
        Assert.Equal(originalVault.Entries[0].Id, restoredEntry.Id);

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            store.OpenAsync(
                _vaultPath,
                newPassword,
                ct: cancellationToken));
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
            byte[] salt,
            Argon2Parameters? parameters = null) =>
            Inner.DeriveKey(
                masterPassword,
                salt,
                parameters);

        public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
            byte[] plaintext,
            byte[] key,
            byte[]? associatedData = null)
        {
            if (FailEncryption)
                throw new InvalidOperationException(
                    "Simulated encryption failure.");

            return Inner.Encrypt(
                plaintext,
                key,
                associatedData);
        }

        public byte[] Decrypt(
            byte[] ciphertext,
            byte[] nonce,
            byte[] tag,
            byte[] key,
            byte[]? associatedData = null) =>
            Inner.Decrypt(
                ciphertext,
                nonce,
                tag,
                key,
                associatedData);

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
