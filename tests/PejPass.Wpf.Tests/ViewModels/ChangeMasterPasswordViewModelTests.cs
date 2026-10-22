using PejPass.Application.Security;
using PejPass.Domain.Entities;
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
}
