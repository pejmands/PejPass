using PejPass.Domain.Entities;
using PejPass.Wpf.Controls;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows.Controls;
using System.Windows.Threading;

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
            Assert.Equal(string.Empty, viewModel.MasterPasswordStrengthLabel);
            Assert.Equal(0d, viewModel.MasterPasswordStrengthProgress);
            Assert.Equal(0, viewModel.MasterPasswordStrengthLevel);
            Assert.False(viewModel.ShowMasterPasswordStrength);
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
            Assert.Equal(0d, viewModel.NewPasswordStrengthProgress);
            Assert.Equal(0, viewModel.NewPasswordStrengthLevel);
            Assert.False(viewModel.ShowNewPasswordStrength);
            Assert.Null(window.DataContext);
        }, TestContext.Current.CancellationToken);
    }


    [Fact(Timeout = 15000)]
    public void PasswordGeneratorWindow_AcceptPreservesResultButClearsViewModel()
    {
        WpfTestHost.Run(_ =>
        {
            var viewModel = new PasswordGeneratorViewModel();
            var expectedPassword = viewModel.Preview;
            var window = new PasswordGeneratorWindow(viewModel);

            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => viewModel.UseCommand.Execute(null)));

            var dialogResult = window.ShowDialog();

            Assert.True(dialogResult);
            Assert.Equal(expectedPassword, window.TakeGeneratedPassword());
            Assert.Null(window.GeneratedPassword);
            Assert.Null(window.DataContext);
            Assert.Equal(string.Empty, viewModel.Preview);
            Assert.Null(viewModel.Result);
            Assert.Null(viewModel.Error);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void PasswordGeneratorWindow_CloseWithoutAcceptingClearsViewModel()
    {
        WpfTestHost.Run(_ =>
        {
            var viewModel = new PasswordGeneratorViewModel();
            var window = new PasswordGeneratorWindow(viewModel);

            window.Show();
            window.Close();

            Assert.Null(window.DataContext);
            Assert.Equal(string.Empty, viewModel.Preview);
            Assert.Null(viewModel.Result);
            Assert.Null(viewModel.Error);
        }, TestContext.Current.CancellationToken);
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
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
            Assert.All(
                FindVisualChildren<PasswordRevealBox>(window),
                control => Assert.Equal(string.Empty, control.Password));
            Assert.Null(window.DataContext);

            Assert.Equal("new-password", viewModel.Password);
            Assert.Equal("KRSXG5DSNFXGOIDB", viewModel.TotpSecret);
            Assert.Equal("new-token", viewModel.CustomFields[0].Value);

            var changes = viewModel.GetSensitiveChanges();
            Assert.Contains(changes, change => change.FieldName == "Password");
            Assert.Contains(changes, change => change.FieldName == "TOTP Secret");
            Assert.Contains(changes, change => change.FieldName == "Custom field \"API token\"");

            viewModel.ClearSensitiveInputs();

            Assert.Equal(string.Empty, viewModel.Title);
            Assert.Equal(string.Empty, viewModel.Username);
            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Equal(string.Empty, viewModel.Url);
            Assert.Equal(string.Empty, viewModel.TotpSecret);
            Assert.Equal(string.Empty, viewModel.Notes);
            Assert.Equal(string.Empty, viewModel.TagsText);
            Assert.False(viewModel.IsFavorite);
            Assert.Null(viewModel.TitleErrorMessage);
            Assert.Null(viewModel.UrlErrorMessage);
            Assert.Null(viewModel.TotpErrorMessage);
            Assert.Equal(string.Empty, viewModel.PasswordStrengthLabel);
            Assert.Equal(0d, viewModel.PasswordStrengthProgress);
            Assert.Equal(0, viewModel.PasswordStrengthLevel);
            Assert.All(viewModel.CustomFields, field =>
            {
                Assert.Equal(string.Empty, field.Name);
                Assert.Equal(string.Empty, field.Value);
                Assert.False(field.IsSecret);
                Assert.Null(field.ErrorMessage);
            });
            Assert.Null(viewModel.Original);
        }, TestContext.Current.CancellationToken);
    }
}
