using PejPass.Application.Interfaces;
using PejPass.Application.Security;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Sessions;

public sealed class AsyncVaultSessionRaceTests
{
    [Fact]
    public async Task FailedDelayedSaveDoesNotRollbackAfterSessionReplacement()
    {
        var store = new DelayedSaveVaultStore();
        var vaultService = new VaultService(store);
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var originalEntry = new VaultEntry { Title = "Original" };
        var originalVault = new Vault { Entries = [originalEntry] };
        var replacementVault = new Vault
        {
            Entries = [new VaultEntry { Title = "Replacement session" }]
        };
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");

        session.Open(originalVault, path, keyMaterial);
        var generation = session.Generation;
        var snapshot = originalVault.CreateSnapshot();
        originalEntry.Title = "Modified before save";

        using var viewModel = new HistoryViewModel(session, vaultService, new AppSettings());
        var saveMethod = typeof(HistoryViewModel).GetMethod(
            "SaveAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(saveMethod);

        var saveTask = (Task<bool>)saveMethod!.Invoke(
            viewModel,
            [originalVault, generation, snapshot, "Race test"])!;

        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        session.ReplaceVault(replacementVault);
        store.ReleaseSave.TrySetException(new IOException("Simulated delayed save failure"));

        Assert.False(await saveTask);
        Assert.Equal("Modified before save", originalEntry.Title);
        Assert.Same(replacementVault, session.Vault);
        Assert.Equal("Replacement session", session.Vault!.Entries[0].Title);
    }

    [Fact]
    public async Task FailedSaveRollsBackVaultAndNotifiesMainViewToRefresh()
    {
        var store = new DelayedSaveVaultStore();
        var vaultService = new VaultService(store);
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var originalEntry = new VaultEntry { Title = "Original" };
        var vault = new Vault { Entries = [originalEntry] };
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");

        session.Open(vault, path, keyMaterial);
        var generation = session.Generation;
        var snapshot = vault.CreateSnapshot();
        originalEntry.Title = "Modified before save";

        using var viewModel = new HistoryViewModel(
            session,
            vaultService,
            new AppSettings(),
            (_, _) => { });
        var rollbackNotifications = 0;
        viewModel.VaultRollbackCompleted += (_, _) => rollbackNotifications++;

        var saveMethod = typeof(HistoryViewModel).GetMethod(
            "SaveAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(saveMethod);

        var saveTask = (Task<bool>)saveMethod!.Invoke(
            viewModel,
            [vault, generation, snapshot, "Rollback test"])!;

        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        store.ReleaseSave.TrySetException(new IOException("Simulated save failure"));

        Assert.False(await saveTask);
        Assert.Equal("Original", vault.Entries[0].Title);
        Assert.NotSame(originalEntry, vault.Entries[0]);
        Assert.Equal(1, rollbackNotifications);
        Assert.Same(vault, session.Vault);
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

    private sealed class DelayedSaveVaultStore : IVaultStore
    {
        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SaveAsync(
            string path,
            VaultKeyMaterial keyMaterial,
            Vault vault,
            CancellationToken ct = default)
        {
            SaveStarted.TrySetResult();
            return ReleaseSave.Task;
        }

        public Task EnsureWritableAsync(string path, CancellationToken ct = default) =>
            Task.CompletedTask;

        public bool Exists(string path) => true;

        public Task CreateAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<VaultSessionData> CreateSessionAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<VaultSessionData> OpenSessionAsync(string path, string masterPassword, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Vault> OpenWithKeyAsync(string path, VaultKeyMaterial keyMaterial, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<VaultKeyMaterial> SaveWithNewPasswordAsync(string path, string masterPassword, Vault vault, Argon2Parameters kdfParameters, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Vault> OpenAsync(string path, string masterPassword, CancellationToken ct = default, bool migrateLegacy = true) =>
            throw new NotSupportedException();

        public Task SaveAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
