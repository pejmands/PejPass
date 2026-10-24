using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class WindowTrayBehaviorTests
{
    [Fact(Timeout = 15000)]
    public void SystemTray_ShowHide_ChangesTrayVisibility()
    {
        WpfTestHost.Run(host =>
        {
            host.Tray.Show();
            Assert.True(host.Tray.IsVisible);

            host.Tray.Hide();
            Assert.False(host.Tray.IsVisible);

            host.Tray.Show();
            Assert.True(host.Tray.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void PrepareForExit_MakesTrayStopInterceptingClose()
    {
        WpfTestHost.Run(host =>
        {
            var window = host.CreateLoginWindow();
            host.Tray.Show();
            Assert.True(host.Tray.IsVisible);

            host.Tray.PrepareForExit();

            Assert.False(host.Tray.IsVisible);

            var args = new System.ComponentModel.CancelEventArgs();

            Assert.False(host.Tray.TryHideToTray(window, args));
            Assert.False(args.Cancel);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_CloseToSystemTray_LocksVaultAndRequiresTrayReopen()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.CloseToSystemTray = true;
            OpenTestVault(host);

            var app = System.Windows.Application.Current!;
            var window = host.CreateMainWindow();
            app.MainWindow = window;
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;
            window.Show();

            window.Close();

            Assert.False(window.IsVisible);
            Assert.False(wasClosed);
            Assert.True(host.VaultSession.IsActive);
            Assert.True(host.Tray.IsVisible);

            DrainDispatcher();

            Assert.False(host.VaultSession.IsActive);
            Assert.True(wasClosed);
            var login = Assert.IsType<LoginWindow>(app.MainWindow);
            Assert.False(login.IsVisible);

            ShowMainWindowFromTray();

            Assert.True(login.IsVisible);
            Assert.False(host.VaultSession.IsActive);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_MinimizeToSystemTray_LocksVault()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = true;
            OpenTestVault(host);

            var app = System.Windows.Application.Current!;
            var window = host.CreateMainWindow();
            app.MainWindow = window;
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;
            window.Show();

            window.WindowState = WindowState.Minimized;

            Assert.Equal(WindowState.Minimized, window.WindowState);
            Assert.False(window.IsVisible);
            Assert.True(host.VaultSession.IsActive);
            Assert.True(host.Tray.IsVisible);

            DrainDispatcher();

            Assert.False(host.VaultSession.IsActive);
            Assert.True(wasClosed);
            var login = Assert.IsType<LoginWindow>(app.MainWindow);
            Assert.False(login.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_CloseToSystemTray_DoesNotLockVaultWhenOptionIsDisabled()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.CloseToSystemTray = true;
            host.Settings.LockVaultWhenHiddenToTray = false;
            OpenTestVault(host);

            var app = System.Windows.Application.Current!;
            var window = host.CreateMainWindow();
            app.MainWindow = window;
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;
            window.Show();

            window.Close();
            DrainDispatcher();

            Assert.False(window.IsVisible);
            Assert.False(wasClosed);
            Assert.True(host.VaultSession.IsActive);
            Assert.True(host.Tray.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_MinimizeToSystemTray_DoesNotLockVaultWhenOptionIsDisabled()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.MinimizeToSystemTray = true;
            host.Settings.LockVaultWhenHiddenToTray = false;
            OpenTestVault(host);

            var app = System.Windows.Application.Current!;
            var window = host.CreateMainWindow();
            app.MainWindow = window;
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;
            window.Show();

            window.WindowState = WindowState.Minimized;
            DrainDispatcher();

            Assert.False(window.IsVisible);
            Assert.False(wasClosed);
            Assert.True(host.VaultSession.IsActive);
            Assert.True(host.Tray.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void SystemTray_ContextMenu_IsOwnedByTrayHostNotLoginWindow()
    {
        WpfTestHost.Run(host =>
        {
            host.Tray.Show();

            var app = System.Windows.Application.Current!;
            var login = host.CreateLoginWindow();
            app.MainWindow = login;
            login.Show();

            var menuField = typeof(SystemTrayService).GetField(
                "_contextMenu",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(menuField);

            var menu = Assert.IsType<ContextMenu>(menuField!.GetValue(host.Tray));
            var iconField = typeof(SystemTrayService).GetField(
                "_taskbarIcon",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(iconField);
            var trayIcon = Assert.IsType<FrameworkElement>(
                iconField!.GetValue(host.Tray), exactMatch: false);
            Assert.Equal("None", trayIcon.GetType()
                .GetProperty("MenuActivation")!.GetValue(trayIcon)!.ToString());

            var openMenu = typeof(SystemTrayService).GetMethod(
                "OpenContextMenu",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(openMenu);
            openMenu!.Invoke(host.Tray, null);

            Assert.True(menu.IsOpen);
            Assert.Same(trayIcon, menu.PlacementTarget);

            var owner = Window.GetWindow(trayIcon);
            Assert.NotNull(owner);
            Assert.Equal(SystemTrayService.HostWindowTag, owner!.Tag);
            Assert.NotSame(login, owner);
            menu.IsOpen = false;
        }, TestContext.Current.CancellationToken);
    }

    private static void OpenTestVault(WpfTestHost host)
    {
        using var keyMaterial = new VaultKeyMaterial(
            new byte[32],
            new byte[16],
            Argon2Parameters.Default);

        host.VaultSession.Open(
            new Vault { Name = "Tray test" },
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            keyMaterial);
    }

    private static void DrainDispatcher()
    {
        var dispatcher = System.Windows.Application.Current!.Dispatcher;
        var frame = new DispatcherFrame();

        dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));

        Dispatcher.PushFrame(frame);
    }

    private static void ShowMainWindowFromTray()
    {
        var handler = typeof(App).GetMethod(
            "OnTrayShowRequested",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(handler);
        handler!.Invoke(null, [null, EventArgs.Empty]);
    }

    [Fact(Timeout = 15000)]
    public void MainWindow_MinimizeToSystemTrayFalse_RemainsVisibleWhenMinimized()
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
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_BrowserLock_KeepsHiddenWindowHidden()
    {
        WpfTestHost.Run(host =>
        {
            var login = host.CreateLoginWindow();
            System.Windows.Application.Current!.MainWindow = login;
            login.Show();
            login.Hide();

            var method = typeof(LoginWindow).GetMethod(
                "MatchWindowStateForBrowserLock",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);
            method!.Invoke(login, [false, WindowState.Minimized]);

            Assert.False(login.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_CloseToSystemTray_HidesInsteadOfClosing()
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
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_MinimizeToSystemTray_HidesWhenEnabled()
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
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void LoginWindow_MinimizeToSystemTrayFalse_RemainsVisibleWhenMinimized()
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
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void AutoLock_MinimizedMainWindowStillRequestsLoginWhenTrayMinimizeIsDisabled()
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
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void AutoLock_HiddenMainWindowDoesNotRequestLogin()
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
        }, TestContext.Current.CancellationToken);
    }


    [Fact(Timeout = 15000)]
    public void FindActivationTarget_PrefersMainWindowOverTrayHost_EvenWhenNeverShown()
    {
        WpfTestHost.Run(host =>
        {
            host.Tray.Show();
            var login = host.CreateLoginWindow();
            System.Windows.Application.Current!.MainWindow = login;

            var target = SingleInstance.FindActivationTarget(
                System.Windows.Application.Current);

            Assert.Same(login, target);
        }, TestContext.Current.CancellationToken);
    }
}
