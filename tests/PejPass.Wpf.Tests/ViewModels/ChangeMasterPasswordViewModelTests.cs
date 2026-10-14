using PejPass.Application.Interfaces;
using PejPass.Application.Security;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Policies;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class ChangeMasterPasswordViewModelTests
{
    [Fact]
    public async Task ChangeAsync_WhenNewPasswordMatchesCurrentPassword_ShowsFieldErrorWithoutCallingServices()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();

        session.Open(
            new Vault(),
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            keyMaterial);

        var viewModel = new ChangeMasterPasswordViewModel(
            null!,
            session,
            null!)
        {
            CurrentPassword = "SamePassword!123",
            NewPassword = "SamePassword!123",
            ConfirmPassword = "SamePassword!123"
        };

        await viewModel.ChangeCommand.ExecuteAsync(null);

        Assert.Equal(
            "The new password must be different from the current password.",
            viewModel.NewPasswordError);
        Assert.Null(viewModel.CurrentPasswordError);
        Assert.Null(viewModel.ConfirmPasswordError);
        Assert.False(viewModel.Success);
        Assert.True(session.IsActive);
    }

    [Fact]
    public async Task ChangeAsync_WhenSessionIsInvalidatedAfterPasswordSave_ShowsRecoveryMessageAndClearsCache()
    {
        using var currentKeyMaterial = CreateMaterial();
        using var store = new DelayedPasswordChangeVaultStore(currentKeyMaterial);
        var vaultService = new VaultService(store);
        using var session = new VaultSession();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");
        var vault = new Vault();

        session.Open(vault, path, currentKeyMaterial);

        using var cache = new SessionPasswordCache(new AppSettings(), TimeProvider.System);
        cache.Store(path, currentKeyMaterial);
        Assert.True(cache.HasCacheFor(path));

        const string newPassword = "vQ7!nT4#zP9@kR2$wX6";
        Assert.True(MasterPasswordPolicy.Validate(newPassword).IsValid);

        var viewModel = new ChangeMasterPasswordViewModel(vaultService, session, cache)
        {
            CurrentPassword = "CurrentPassword!123",
            NewPassword = newPassword,
            ConfirmPassword = newPassword
        };

        var changeTask = viewModel.ChangeCommand.ExecuteAsync(null);
        var firstCompleted = await Task.WhenAny(store.SaveStarted.Task, changeTask)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Same(
            store.SaveStarted.Task,
            firstCompleted,
            "The password-change command ended before reaching the save operation.");

        session.Clear();
        store.ReleaseSave.TrySetResult();

        await changeTask;

        Assert.False(viewModel.Success);
        Assert.Equal(
            "The vault was locked while the password was changing. " +
            "The new password is already in effect; unlock with it.",
            viewModel.NewPasswordError);
        Assert.Null(viewModel.CurrentPasswordError);
        Assert.Null(viewModel.ConfirmPasswordError);
        Assert.False(cache.HasCacheFor(path));
        Assert.False(session.IsActive);
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = new byte[32];
        var salt = new byte[16];

        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(salt);

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    private sealed class DelayedPasswordChangeVaultStore(VaultKeyMaterial currentKeyMaterial)
        : IVaultStore, IDisposable
    {
        private readonly VaultKeyMaterial _currentKeyMaterial = currentKeyMaterial.Clone();

        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<VaultSessionData> OpenSessionAsync(
            string path,
            string masterPassword,
            CancellationToken ct = default) =>
            Task.FromResult(new VaultSessionData(new Vault(), _currentKeyMaterial.Clone()));

        public async Task<VaultKeyMaterial> SaveWithNewPasswordAsync(
            string path,
            string masterPassword,
            Vault vault,
            Argon2Parameters kdfParameters,
            CancellationToken ct = default)
        {
            SaveStarted.TrySetResult();
            await ReleaseSave.Task.WaitAsync(ct);
            return CreateMaterial();
        }

        public void Dispose() => _currentKeyMaterial.Dispose();

        public Task CreateAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<VaultSessionData> CreateSessionAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Vault> OpenWithKeyAsync(string path, VaultKeyMaterial keyMaterial, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(string path, VaultKeyMaterial keyMaterial, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Vault> OpenAsync(string path, string masterPassword, CancellationToken ct = default, bool migrateLegacy = true) =>
            throw new NotSupportedException();

        public Task SaveAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureWritableAsync(string path, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool Exists(string path) => true;
    }
}
