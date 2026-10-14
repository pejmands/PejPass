using PejPass.Application.Interfaces;
using PejPass.Application.Security;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class LoginViewModelRecoveryNoticeTests
{
    [Fact]
    public async Task SubmitAsync_WhenPasswordIsRejectedForPendingVault_ShowsRecoveryHint()
    {
        var path = CreateVaultFile();

        try
        {
            var settings = new AppSettings { RecentVaultPaths = [path] };
            using var session = new VaultSession();
            using var cache = new SessionPasswordCache(settings, TimeProvider.System);
            var pendingNotice = new PendingMasterPasswordChangeNotice();
            pendingNotice.Mark(path);

            var viewModel = new LoginViewModel(
                new VaultService(new AuthenticationFailingVaultStore()),
                settings,
                session,
                cache,
                pendingNotice)
            {
                MasterPassword = "previous-password"
            };

            await viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.Equal(
                VaultErrorMessages.ForUnlock(authenticationFailed: true),
                viewModel.PasswordError);
            Assert.Equal(
                PendingMasterPasswordChangeNotice.RecoveryHint,
                viewModel.StatusMessage);
            Assert.True(pendingNotice.IsPendingFor(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenPendingNoticeBelongsToAnotherVault_DoesNotShowRecoveryHint()
    {
        var path = CreateVaultFile();
        var otherPath = path + ".other";

        try
        {
            var settings = new AppSettings { RecentVaultPaths = [path] };
            using var session = new VaultSession();
            using var cache = new SessionPasswordCache(settings, TimeProvider.System);
            var pendingNotice = new PendingMasterPasswordChangeNotice();
            pendingNotice.Mark(otherPath);

            var viewModel = new LoginViewModel(
                new VaultService(new AuthenticationFailingVaultStore()),
                settings,
                session,
                cache,
                pendingNotice)
            {
                MasterPassword = "incorrect-password"
            };

            await viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.Equal(
                VaultErrorMessages.ForUnlock(authenticationFailed: true),
                viewModel.PasswordError);
            Assert.Equal(string.Empty, viewModel.StatusMessage);
            Assert.True(pendingNotice.IsPendingFor(otherPath));
            Assert.False(pendingNotice.IsPendingFor(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateVaultFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.pejpass");

        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private sealed class AuthenticationFailingVaultStore : IVaultStore
    {
        public Task<VaultSessionData> OpenSessionAsync(
            string path,
            string masterPassword,
            CancellationToken ct = default) =>
            throw new AuthenticationTagMismatchException();

        public bool Exists(string path) => File.Exists(path);

        public Task<VaultKeyMaterial> SaveWithNewPasswordAsync(
            string path,
            string masterPassword,
            Vault vault,
            Argon2Parameters kdfParameters,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CreateAsync(
            string path,
            string masterPassword,
            Vault vault,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<VaultSessionData> CreateSessionAsync(
            string path,
            string masterPassword,
            Vault vault,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Vault> OpenWithKeyAsync(
            string path,
            VaultKeyMaterial keyMaterial,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            string path,
            VaultKeyMaterial keyMaterial,
            Vault vault,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Vault> OpenAsync(
            string path,
            string masterPassword,
            CancellationToken ct = default,
            bool migrateLegacy = true) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            string path,
            string masterPassword,
            Vault vault,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureWritableAsync(
            string path,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
