using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Reflection;
using System.Windows;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class LoginWindowPasswordClearTests
{
    [Fact(Timeout = 15000)]
    public void WindowsSecurityLock_ClearsTypedLoginPasswordInputs()
    {
        WpfTestHost.Run(host =>
        {
            var app = Application.Current!;
            var login = host.CreateLoginWindow();
            app.MainWindow = login;
            login.Show();

            var passwordBox = GetPasswordBox(login, "MasterPasswordBox");
            var confirmPasswordBox = GetPasswordBox(login, "ConfirmMasterPasswordBox");

            SetPassword(passwordBox, "partial-master-password");
            SetPassword(confirmPasswordBox, "partial-confirm-password");

            var viewModel = Assert.IsType<LoginViewModel>(login.DataContext);
            Assert.Equal("partial-master-password", viewModel.MasterPassword);
            Assert.Equal("partial-confirm-password", viewModel.ConfirmMasterPassword);

            App.LockForWindowsSecurity();

            Assert.Equal(string.Empty, GetPassword(passwordBox));
            Assert.Equal(string.Empty, GetPassword(confirmPasswordBox));
            Assert.Equal(string.Empty, viewModel.MasterPassword);
            Assert.Equal(string.Empty, viewModel.ConfirmMasterPassword);
        }, TestContext.Current.CancellationToken);
    }

    private static object GetPasswordBox(LoginWindow window, string name)
    {
        var field = typeof(LoginWindow).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(field);
        return field!.GetValue(window)!;
    }

    private static void SetPassword(object control, string value) =>
        control.GetType().GetProperty("Password")!.SetValue(control, value);

    private static string GetPassword(object control) =>
        (string)control.GetType().GetProperty("Password")!.GetValue(control)!;
}
