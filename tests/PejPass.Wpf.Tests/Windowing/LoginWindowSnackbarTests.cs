using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.Windows;
using System.Windows.Controls;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class LoginWindowSnackbarTests
{
    [Fact(Timeout = 15000)]
    public void SnackbarService_ShowDisplaysMessageOnlyWhileLoginWindowIsVisible()
    {
        WpfTestHost.Run(host =>
        {
            var window = host.CreateLoginWindow();
            var border = Assert.IsType<Border>(window.FindName("SnackbarBorder"));
            var message = Assert.IsType<TextBlock>(window.FindName("SnackbarText"));

            window.Show();

            SnackbarService.Show(
                "Login notification",
                SnackbarKind.Warning,
                TimeSpan.FromSeconds(5));

            Assert.Equal("Login notification", message.Text);
            Assert.Equal(Visibility.Visible, border.Visibility);

            window.Hide();

            SnackbarService.Show(
                "Hidden login notification",
                SnackbarKind.Error,
                TimeSpan.FromSeconds(5));

            Assert.Equal("Login notification", message.Text);
            Assert.Equal(Visibility.Collapsed, border.Visibility);
        }, TestContext.Current.CancellationToken);
    }
}
