using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Import;
using PejPass.Infrastructure.Storage;
using PejPass.Wpf.Controls;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace PejPass.Wpf;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static AppSettings? _settings;
    private static WindowsSecurityService? _windowsSecurityService;

    private const double WindowCornerRadius = 10;

    protected override void OnStartup(StartupEventArgs e)
    {
        var launchVaultPath = ParseVaultPathArg(e.Args);
        var startInBackground = IsStartupLaunch(e.Args) &&
            string.IsNullOrWhiteSpace(launchVaultPath);

        if (!SingleInstance.TryAcquire(launchVaultPath))
        {
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        SingleInstance.Activated += OnSecondInstanceActivated;

        UiPolish.Register();

        VaultFileAssociation.EnsureRegistered();

        var settings = SettingsStore.Load();
        _settings = settings;
        WindowsStartupService.TrySetEnabled(settings.StartWithWindows, out _);
        FaviconService.ConfigureOnlineFetching(settings.OnlineFaviconFetchingEnabled);
        ZoomBehavior.GlobalZoomChanged += OnGlobalZoomChanged;

        var themeService = new ThemeService(settings);

        themeService.Apply();
        ApplyFontSize(settings.FontSize);
        ZoomBehavior.SetGlobalZoom(settings.Zoom);

        var services = new ServiceCollection();

        services.AddSingleton(settings);
        services.AddSingleton(themeService);

        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IFileMover, FileMover>();
        services.AddSingleton<IVaultStore, VaultStore>();
        services.AddSingleton<IBrowserImportService, BrowserImportService>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<IClipboardProvider, WpfClipboardProvider>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(Current.Dispatcher));
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<VaultService>();
        services.AddSingleton<VaultSession>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<SystemTrayService>();
        services.AddSingleton<WindowsSecurityService>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<EntryEditorViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<AboutViewModel>();

        services.AddTransient<LoginWindow>();
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<HistoryWindow>();
        services.AddTransient<AboutWindow>();

        Services = services.BuildServiceProvider();

        // Keep the application alive independently of the minimize-to-tray preference.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var tray = Services.GetRequiredService<SystemTrayService>();
        tray.ShowRequested += OnTrayShowRequested;
        tray.LockRequested += OnTrayLockRequested;

        tray.Show();

        _windowsSecurityService = Services.GetRequiredService<WindowsSecurityService>();
        _windowsSecurityService.SecurityLockRequested += OnWindowsSecurityLockRequested;
        _windowsSecurityService.Start();

        var login = Services.GetRequiredService<LoginWindow>();
        MainWindow = login;

        PrepareCustomChrome(login);
        FaviconService.StartDiskCacheCleanup();

        if (!string.IsNullOrWhiteSpace(launchVaultPath) &&
            login.DataContext is LoginViewModel loginVm)
        {
            loginVm.ApplyExternalVaultPath(launchVaultPath);
        }

        if (!startInBackground)
            login.Show();

        if (settings.AutoCheckForUpdates)
            _ = CheckForUpdatesInBackgroundAsync();
    }

    private static void OnTrayShowRequested(object? sender, EventArgs e)
    {
        Current.Dispatcher.Invoke(() =>
        {
            var win = Current.MainWindow;
            if (win is null)
                return;

            if (!win.IsVisible)
                win.Show();

            if (win.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;

            win.Activate();
            win.Focus();
        });
    }

    private static void OnTrayLockRequested(object? sender, EventArgs e)
    {
        Current.Dispatcher.Invoke(() =>
        {
            // Preserve the current visibility of the main window.
            if (Current.MainWindow is MainWindow main &&
                main.DataContext is MainViewModel vm)
            {
                vm.Lock(
                    main.IsVisible &&
                    main.WindowState != WindowState.Minimized);
            }
        });
    }

    private static void OnWindowsSecurityLockRequested(object? sender, EventArgs e)
    {
        Current.Dispatcher.Invoke(() =>
        {
            // Lock immediately when Windows locks or suspends the system.
            // A minimized or hidden main window keeps the login screen hidden.
            if (Current.MainWindow is MainWindow main &&
                main.DataContext is MainViewModel vm)
            {
                vm.Lock(
                    main.IsVisible &&
                    main.WindowState != WindowState.Minimized);
            }
        });
    }

    private static async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var updateService = Services.GetRequiredService<UpdateService>();
            var result = await updateService
                .CheckForUpdatesAsync()
                .ConfigureAwait(false);

            var hasUpdate = result.Status == Records.UpdateCheckStatus.UpdateAvailable;

            await Current.Dispatcher.InvokeAsync(() =>
                UpdateAvailability.Set(hasUpdate));
        }
        catch
        {
            // Silent — startup must not fail because of update check.
        }
    }

    private static void OnGlobalZoomChanged(object? sender, double zoom)
    {
        if (_settings is null)
            return;

        if (Current.Windows.OfType<SettingsWindow>().Any())
            return;

        _settings.Zoom = zoom;
        if (!SettingsStore.TrySave(_settings))
            SnackbarService.Show("Failed to save zoom setting.");
    }

    private static void OnSecondInstanceActivated(string? vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return;

        HandleExternalVaultPath(vaultPath);
    }

    public static void HandleExternalVaultPath(string path)
    {
        try
        {
            path = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return;
        }

        var vaultSession = Services.GetRequiredService<VaultSession>();

        if (vaultSession.Vault is not null)
        {
            var current = vaultSession.VaultPath;

            if (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if (string.Equals(
                        Path.GetFullPath(current),
                        path,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                catch
                {
                }
            }

            DialogService.Info(
                "A vault is already open.\n\nLock it first if you want to open a different .pejpass file.",
                "PejPass");

            return;
        }

        foreach (Window w in Current.Windows)
        {
            if (w is LoginWindow { DataContext: LoginViewModel vm })
            {
                vm.ApplyExternalVaultPath(path);
                return;
            }
        }
    }

    private static bool IsStartupLaunch(string[] args) =>
        args.Any(arg => string.Equals(
            arg.Trim(),
            "--startup",
            StringComparison.OrdinalIgnoreCase));

    private static string? ParseVaultPathArg(string[] args)
    {
        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var p = raw.Trim().Trim('"');

            if (!p.EndsWith(".pejpass", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                return Path.GetFullPath(p);
            }
            catch
            {
                return p;
            }
        }

        return null;
    }

    public static void ApplyFontSize(FontSizeMode mode)
    {
        var scale = mode switch
        {
            FontSizeMode.Small => 12d / 13d,
            FontSizeMode.Large => 15d / 13d,
            _ => 1d
        };

        foreach (var baseSize in new[] { 10d, 11d, 12d, 13d, 14d, 15d, 16d, 18d, 20d, 22d, 28d, 30d })
            Current.Resources[$"AppFontSize{baseSize:0}"] = baseSize * scale;

        Current.Resources["AppFontSize"] = 13d * scale;
    }

    public static void PrepareCustomChrome(Window window)
    {
        if (window.Content is Border { Tag: "ChromeRoot" })
            return;

        ApplyWindowChrome(
            window,
            window.WindowState == WindowState.Maximized
                ? 0
                : WindowCornerRadius);

        EnsureChromeShell(window);

        window.StateChanged -= OnWindowStateChangedForChrome;
        window.StateChanged += OnWindowStateChangedForChrome;
    }

    public static void SetCustomWindowTitle(Window window, string title, string? filePath = null)
    {
        if (window.Content is DependencyObject content &&
            FindVisualChild<AppTitleBar>(content) is AppTitleBar titleBar)
        {
            titleBar.Title = title;
            titleBar.FilePath = filePath ?? string.Empty;
            titleBar.ShowFilePathToolTip =
                window is MainWindow && !string.IsNullOrWhiteSpace(filePath);
        }
    }

    private static void OnWindowStateChangedForChrome(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        var radius = window.WindowState == WindowState.Maximized
            ? 0
            : WindowCornerRadius;

        ApplyWindowChrome(window, radius);

        if (window.Content is Border root &&
            Equals(root.Tag, "ChromeRoot"))
        {
            root.CornerRadius = new CornerRadius(radius);
        }
    }

    private static void ApplyWindowChrome(Window window, double radius)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip
                ? new Thickness(6)
                : new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(radius),
            UseAeroCaptionButtons = false
        });

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            StripNativeSystemMenu(hwnd);
        else
            window.SourceInitialized += (_, _) =>
                StripNativeSystemMenu(new System.Windows.Interop.WindowInteropHelper(window).Handle);
    }

    private const int GwlStyle = -16;
    private const int WsSysMenu = 0x00080000;

    private static void StripNativeSystemMenu(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, checked((IntPtr)(style & ~WsSysMenu)));
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static partial IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrA")]
    private static partial IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static partial IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrA")]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);

    private static void EnsureChromeShell(Window window)
    {
        if (window.Content is not UIElement body)
            return;

        window.Content = null;

        if (body is Panel panel &&
            panel.Children.OfType<AppTitleBar>().Any())
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is not AppTitleBar)
                    ZoomBehavior.SetIsEnabled(child, true);
            }
        }
        else
        {
            ZoomBehavior.SetIsEnabled(body, true);
        }

        UIElement content = body;

        if (FindVisualChild<AppTitleBar>(body) is null)
        {
            var bar = new AppTitleBar
            {
                Title = window.Title,
                ShowMainActions = window is MainWindow,
                ShowFileActions = window is MainWindow,
                ShowMinimize = window is MainWindow && window.ResizeMode is not ResizeMode.NoResize,
                ShowMaximize = window.ResizeMode is ResizeMode.CanResize
                    or ResizeMode.CanResizeWithGrip
            };

            var dock = new DockPanel();

            DockPanel.SetDock(bar, Dock.Top);

            dock.Children.Add(bar);
            dock.Children.Add(body);

            content = dock;
        }

        var radius = window.WindowState == WindowState.Maximized
            ? 0
            : WindowCornerRadius;

        var root = new Border
        {
            Tag = "ChromeRoot",
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = content
        };

        root.SetResourceReference(
            Border.BorderBrushProperty,
            "BorderBrush");

        root.SetResourceReference(
            Border.BackgroundProperty,
            "BgBrush");

        window.SetResourceReference(
            Control.BackgroundProperty,
            "BgBrush");

        window.Content = root;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);

            if (nested is not null)
                return nested;
        }

        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Activated -= OnSecondInstanceActivated;
        if (_windowsSecurityService is not null)
        {
            _windowsSecurityService.SecurityLockRequested -= OnWindowsSecurityLockRequested;
            _windowsSecurityService.Dispose();
            _windowsSecurityService = null;
        }
        ZoomBehavior.GlobalZoomChanged -= OnGlobalZoomChanged;
        SingleInstance.Release();
        PendingVaultOpen.ReadAndClear();

        try
        {
            Services?.GetRequiredService<IClipboardService>().ClearIfOwned();
            Services?.GetRequiredService<SystemTrayService>().Dispose();
        }
        catch
        {
            // Best-effort cleanup on exit.
        }

        base.OnExit(e);
    }
}
