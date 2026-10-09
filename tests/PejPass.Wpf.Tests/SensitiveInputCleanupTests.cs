using PejPass.Domain.Entities;
using PejPass.Wpf.Controls;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows.Controls;

namespace PejPass.Wpf.Tests;

public sealed class SensitiveInputCleanupTests
{
    [Fact(Timeout = 15000)]
    public void LoginWindow_CloseClearsPasswordInputsAndViewModel()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.CloseToSystemTray = true;

            var window = host.CreateLoginWindow();
            var viewModel = Assert.IsType<LoginViewModel>(window.DataContext);
            var passwordBox = Assert.IsType<PasswordRevealBox>(window.FindName("MasterPasswordBox"));
            var confirmPasswordBox = Assert.IsType<PasswordRevealBox>(window.FindName("ConfirmMasterPasswordBox"));

            passwordBox.Password = "master-password";
            confirmPasswordBox.Password = "confirm-password";
            window.Show();
            window.Close();

            Assert.Equal(string.Empty, passwordBox.Password);
            Assert.Equal(string.Empty, confirmPasswordBox.Password);
            Assert.Equal(string.Empty, viewModel.MasterPassword);
            Assert.Equal(string.Empty, viewModel.ConfirmMasterPassword);
            Assert.False(passwordBox.IsRevealed);
            Assert.False(confirmPasswordBox.IsRevealed);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void ChangeMasterPasswordWindow_CloseClearsControlsAndViewModel()
    {
        WpfTestHost.Run(_ =>
        {
            var viewModel = new ChangeMasterPasswordViewModel(null!, null!, null!);
            var window = new ChangeMasterPasswordWindow(viewModel);
            var currentPasswordBox = Assert.IsType<PasswordBox>(window.FindName("CurrentPasswordBox"));
            var newPasswordBox = Assert.IsType<PasswordRevealBox>(window.FindName("NewPasswordBox"));
            var confirmPasswordBox = Assert.IsType<PasswordRevealBox>(window.FindName("ConfirmPasswordBox"));

            currentPasswordBox.Password = "current-password";
            newPasswordBox.Password = "new-password";
            confirmPasswordBox.Password = "confirm-password";
            window.Show();
            window.Close();

            Assert.Equal(string.Empty, currentPasswordBox.Password);
            Assert.Equal(string.Empty, newPasswordBox.Password);
            Assert.Equal(string.Empty, confirmPasswordBox.Password);
            Assert.Equal(string.Empty, viewModel.CurrentPassword);
            Assert.Equal(string.Empty, viewModel.NewPassword);
            Assert.Equal(string.Empty, viewModel.ConfirmPassword);
            Assert.Equal(string.Empty, viewModel.NewPasswordStrengthLabel);
            Assert.Equal(0, viewModel.NewPasswordStrengthProgress);
            Assert.Equal(0, viewModel.NewPasswordStrengthLevel);
            Assert.False(viewModel.ShowNewPasswordStrength);
            Assert.Null(window.DataContext);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void EntryEditor_CloseClearsControlsButPreservesDataUntilSensitiveAudit()
    {
        WpfTestHost.Run(_ =>
        {
            var existing = new VaultEntry
            {
                Title = "Example",
                Password = "old-password",
                TotpSecret = "JBSWY3DPEHPK3PXP",
                CustomFields =
                [
                    new CustomField
                    {
                        Name = "API token",
                        Value = "old-token",
                        IsSecret = true
                    },
                    new CustomField
                    {
                        Name = "Reference",
                        Value = "non-secret-value",
                        IsSecret = false
                    }
                ]
            };
            var viewModel = new EntryEditorViewModel(existing, []);
            var window = new EntryEditorWindow(viewModel);
            var passwordBox = Assert.IsType<PasswordRevealBox>(window.FindName("PasswordBox"));
            var totpSecretBox = Assert.IsType<PasswordRevealBox>(window.FindName("TotpSecretBox"));

            viewModel.Password = "new-password";
            viewModel.TotpSecret = "KRSXG5DSNFXGOIDB";
            viewModel.CustomFields[0].Value = "new-token";

            window.Show();
            window.Close();

            Assert.Equal(string.Empty, passwordBox.Password);
            Assert.Equal(string.Empty, totpSecretBox.Password);
            Assert.Null(window.DataContext);

            Assert.Equal("new-password", viewModel.Password);
            Assert.Equal("KRSXG5DSNFXGOIDB", viewModel.TotpSecret);
            Assert.Equal("new-token", viewModel.CustomFields[0].Value);

            var changes = viewModel.GetSensitiveChanges();
            Assert.Contains(changes, change => change.FieldName == "Password");
            Assert.Contains(changes, change => change.FieldName == "TOTP Secret");
            Assert.Contains(changes, change => change.FieldName == "Custom field \"API token\"");

            viewModel.ClearSensitiveInputs();

            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Equal(string.Empty, viewModel.TotpSecret);
            Assert.Equal(string.Empty, viewModel.CustomFields[0].Value);
            Assert.Equal("non-secret-value", viewModel.CustomFields[1].Value);
            Assert.Null(viewModel.Original);
        }, TestContext.Current.CancellationToken);
    }
}
