using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests;

public sealed class WindowTrayBehaviorTests
{
    [Fact(Timeout = 15000)]
    public void SystemTray_ShowHide_ChangesTrayVisibility(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Tray.Show();
            Assert.True(host.Tray.IsVisible);

            host.Tray.Hide();
            Assert.False(host.Tray.IsVisible);

            host.Tray.Show();
            Assert.True(host.Tray.IsVisible);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_CloseToSystemTray_HidesInsteadOfClosing(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.CloseToSystemTray = true;

            var window = host.CreateMainWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            window.Close();

            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);
            Assert.True(host.Tray.IsVisible);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_MinimizeToSystemTray_HidesWhenEnabled(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = true;

            var window = host.CreateMainWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            window.WindowState = System.Windows.WindowState.Minimized;

            Assert.Equal(
                System.Windows.WindowState.Minimized,
                window.WindowState);
            Assert.False(window.IsVisible);
            Assert.True(host.Tray.IsVisible);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_MinimizeToSystemTrayFalse_RemainsVisibleWhenMinimized(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = false;

            var window = host.CreateMainWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            window.WindowState = System.Windows.WindowState.Minimized;

            Assert.Equal(
                System.Windows.WindowState.Minimized,
                window.WindowState);
            Assert.True(window.IsVisible);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_CloseToSystemTray_HidesInsteadOfClosing(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.CloseToSystemTray = true;

            var window = host.CreateLoginWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            window.Close();

            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);
            Assert.True(host.Tray.IsVisible);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_MinimizeToSystemTray_HidesWhenEnabled(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = true;

            var window = host.CreateLoginWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            window.WindowState = System.Windows.WindowState.Minimized;

            Assert.Equal(
                System.Windows.WindowState.Minimized,
                window.WindowState);
            Assert.False(window.IsVisible);
            Assert.True(host.Tray.IsVisible);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_MinimizeToSystemTrayFalse_RemainsVisibleWhenMinimized(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = false;

            var window = host.CreateLoginWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            window.WindowState = System.Windows.WindowState.Minimized;

            Assert.Equal(
                System.Windows.WindowState.Minimized,
                window.WindowState);
            Assert.True(window.IsVisible);
        }, cancellationToken);
    }
    [Fact(Timeout = 15000)]
    public void AutoLock_MinimizedMainWindowStillRequestsLoginWhenTrayMinimizeIsDisabled(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = false;

            var window = host.CreateMainWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();
            window.WindowState = System.Windows.WindowState.Minimized;

            var method = typeof(MainViewModel).GetMethod(
                "ShouldShowLoginAfterAutoLock",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            var showLogin = (bool)method!.Invoke(null, null)!;

            Assert.True(showLogin);
        }, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void AutoLock_HiddenMainWindowDoesNotRequestLogin(CancellationToken cancellationToken)
    {
        WpfTestHost.Run(host =>
        {
            var window = host.CreateMainWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();
            window.Hide();

            var method = typeof(MainViewModel).GetMethod(
                "ShouldShowLoginAfterAutoLock",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            var showLogin = (bool)method!.Invoke(null, null)!;

            Assert.False(showLogin);
        }, cancellationToken);
    }

}
