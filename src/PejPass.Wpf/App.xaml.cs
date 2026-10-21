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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace PejPass.Wpf;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static AppSettings? _settings;
    private static WindowsSecurityService? _windowsSecurityService;
    private static SecurityLockCoordinator? _securityLock;
    private static PasswordGeneratorWindow? _passwordGeneratorWindow;
    private static int _handlingUnhandledException;
    private static int _unobservedTaskExceptionPending;

    private const double WindowCornerRadius = 10;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SessionPasswordCache>();

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
        services.AddSingleton<BrowserStatusPipeService>();
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
        Services.GetRequiredService<BrowserStatusPipeService>().Start();

        // Keep the application alive independently of the minimize-to-tray preference.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var tray = Services.GetRequiredService<SystemTrayService>();
        tray.ShowRequested += OnTrayShowRequested;
        tray.LockRequested += OnTrayLockRequested;
        tray.PasswordGeneratorRequested += OnTrayPasswordGeneratorRequested;

        tray.Show();

        _windowsSecurityService = Services.GetRequiredService<WindowsSecurityService>();
        _securityLock = new SecurityLockCoordinator(
            clearCache: () => Services.GetRequiredService<SessionPasswordCache>().Clear(),
            lockVault: LockForWindowsSecurity,
            dispatchToUi: DispatchToUi);
        _windowsSecurityService.SecurityLockRequested += OnWindowsSecurityLockRequested;
        _windowsSecurityService.Start();

        var login = Services.GetRequiredService<LoginWindow>();
        MainWindow = login;

        PrepareCustomChrome(login);
        FaviconService.StartDiskCacheCleanup();
        _ = Task.Run(() => PlaintextTempJournal.Default.DeleteLeftovers());

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

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleUnexpectedException(e.Exception);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();

        if (Interlocked.CompareExchange(ref _unobservedTaskExceptionPending, 1, 0) != 0)
            return;

        try
        {
            var dispatcher = Dispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                try
                {
                    Services?.GetService<SessionPasswordCache>()?.Clear();
                }
                catch
                {
                    // The dispatcher is unavailable; best-effort cache cleanup only.
                }

                Volatile.Write(ref _unobservedTaskExceptionPending, 0);
                return;
            }

            dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() =>
                {
                    try
                    {
                        HandleUnexpectedException(e.Exception);
                    }
                    finally
                    {
                        Volatile.Write(ref _unobservedTaskExceptionPending, 0);
                    }
                }));
        }
        catch
        {
            Volatile.Write(ref _unobservedTaskExceptionPending, 0);

            try
            {
                Services?.GetService<SessionPasswordCache>()?.Clear();
            }
            catch
            {
                // Keep the task marked as observed even if the dispatcher is unavailable.
            }
        }
    }

    internal static void HandleUnexpectedException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Interlocked.CompareExchange(ref _handlingUnhandledException, 1, 0) != 0)
            return;

        try
        {
            // Avoid logging exception messages or stack traces that could contain sensitive values.
            try
            {
                Trace.TraceError(
                    "An unexpected PejPass exception was intercepted ({0}).",
                    exception.GetType().FullName ?? exception.GetType().Name);
            }
            catch
            {
                // Diagnostics must never interfere with safe exception handling.
            }

            try
            {
                LockVaultForUnexpectedException();
            }
            catch
            {
                // Continue to the generic error message even if emergency locking fails.
            }

            try
            {
                MessageBox.Show(
                    "An unexpected error occurred. PejPass closed any open vault session to protect your data. Reopen the vault to continue.",
                    "PejPass",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // The UI may already be shutting down; do not throw from the exception handler.
            }
        }
        finally
        {
            Volatile.Write(ref _handlingUnhandledException, 0);
        }
    }

    private static void LockVaultForUnexpectedException()
    {
        var application = Current;
        MainWindow? mainWindow = null;

        try
        {
            Services?.GetService<SessionPasswordCache>()?.Clear();
        }
        catch
        {
            // Continue with the vault lock even if cache cleanup fails.
        }

        try
        {
            mainWindow = application?.Windows
                .OfType<MainWindow>()
                .FirstOrDefault(window => window.DataContext is MainViewModel);

            if (mainWindow?.DataContext is MainViewModel viewModel)
                viewModel.Lock(showLogin: true);
        }
        catch
        {
            // Continue with direct UI cleanup if the normal lock path fails.
        }

        try
        {
            if (mainWindow?.DataContext is MainViewModel viewModel)
                ClearSensitiveUiBestEffort(viewModel);
        }
        catch
        {
            // Continue with the session clear if view-model cleanup fails.
        }

        try
        {
            Services?.GetService<VaultSession>()?.Clear();
        }
        catch
        {
            // The login window must still be shown even if session cleanup fails.
        }

        try
        {
            if (mainWindow is { IsVisible: true })
                mainWindow.Hide();
        }
        catch
        {
            // Hiding the former vault window is best-effort cleanup.
        }

        try
        {
            if (application is null || Services is null)
                return;

            var loginWindow = application.Windows
                .OfType<LoginWindow>()
                .OrderByDescending(window => window.IsVisible)
                .FirstOrDefault();

            if (loginWindow is null)
            {
                loginWindow = Services.GetRequiredService<LoginWindow>();
                PrepareCustomChrome(loginWindow);
            }

            application.MainWindow = loginWindow;

            if (!loginWindow.IsVisible)
                loginWindow.Show();

            loginWindow.Activate();

            foreach (var window in application.Windows
                         .OfType<Window>()
                         .Where(window => !ReferenceEquals(window, loginWindow) &&
                                          !SystemTrayService.IsHostWindow(window))
                         .ToArray())
            {
                try
                {
                    window.Close();
                }
                catch
                {
                    try { window.Hide(); } catch { }
                }
            }
        }
        catch
        {
            // Keep the exception handler itself from becoming another unhandled exception.
        }
    }

    private static void ClearSensitiveUiBestEffort(MainViewModel viewModel)
    {
        try { viewModel.StopBackgroundTimers(); } catch { }

        try
        {
            foreach (var item in viewModel.DisplayCustomFields.ToArray())
            {
                try { item.Dispose(); } catch { }
            }
        }
        catch
        {
            // Continue clearing the remaining sensitive view-model state.
        }

        try { viewModel.DisplayCustomFields.Clear(); } catch { }
        try { viewModel.SelectedEntry = null; } catch { }
        try { viewModel.FilteredEntries.Clear(); } catch { }
        try { viewModel.Entries.Clear(); } catch { }
        try { viewModel.SearchText = string.Empty; } catch { }
        try { viewModel.VaultName = string.Empty; } catch { }
        try { viewModel.DisplayPassword = string.Empty; } catch { }
        try { viewModel.TotpCode = string.Empty; } catch { }
        try { viewModel.TotpCodeFormatted = string.Empty; } catch { }
        try { viewModel.TotpRemainingSeconds = 0; } catch { }
        try { viewModel.TotpProgress = 0; } catch { }
        try { viewModel.HasSelection = false; } catch { }
        try { viewModel.HasUrl = false; } catch { }
        try { viewModel.HasTotp = false; } catch { }
        try { viewModel.HasNotes = false; } catch { }
        try { viewModel.HasCustomFields = false; } catch { }
        try { viewModel.HasTags = false; } catch { }
        try { viewModel.IsFavoriteSelected = false; } catch { }
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

    private static void OnTrayPasswordGeneratorRequested(object? sender, EventArgs e)
    {
        Current.Dispatcher.Invoke(() =>
        {
            if (_passwordGeneratorWindow is not null)
            {
                if (_passwordGeneratorWindow.WindowState == WindowState.Minimized)
                    _passwordGeneratorWindow.WindowState = WindowState.Normal;

                _passwordGeneratorWindow.Activate();
                _passwordGeneratorWindow.Focus();
                return;
            }

            var clipboard = Services.GetRequiredService<IClipboardService>();
            var settings = Services.GetRequiredService<AppSettings>();
            var window = new PasswordGeneratorWindow(
                new PasswordGeneratorViewModel(),
                standalone: true,
                clipboard: clipboard,
                clipboardTimeout: TimeSpan.FromSeconds(settings.ClipboardClearSeconds));

            _passwordGeneratorWindow = window;
            window.Closed += (_, _) => _passwordGeneratorWindow = null;

            window.Show();
            window.Activate();
            window.Focus();
        });
    }

    private static void OnTrayLockRequested(object? sender, EventArgs e)
    {
        Current.Dispatcher.Invoke(LockMainWindowIfOpen);
    }

    private static void OnWindowsSecurityLockRequested(object? sender, SecurityLockReason reason)
    {
        _securityLock?.Handle(waitForUi: reason == SecurityLockReason.Suspend);
    }

    internal static void LockForWindowsSecurity()
    {
        foreach (var loginWindow in Current.Windows.OfType<LoginWindow>())
            loginWindow.ClearPasswordInputs();

        LockMainWindowIfOpen();
    }

    /// <summary>Must run on the UI thread. Preserves the current visibility of the main window.</summary>
    private static void LockMainWindowIfOpen()
    {
        if (Current.MainWindow is MainWindow main &&
            main.DataContext is MainViewModel vm)
        {
            vm.Lock(
                main.IsVisible &&
                main.WindowState != WindowState.Minimized);
        }
    }

    private static bool DispatchToUi(Action action, TimeSpan? wait)
    {
        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null ||
            dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished)
        {
            return false;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return true;
        }

        var operation = dispatcher.InvokeAsync(action, DispatcherPriority.Send);

        return !(wait is { } timeout) || operation.Wait(timeout) == DispatcherOperationStatus.Completed;
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
        if (!ExternalVaultPathValidator.TryValidate(path, out var validatedPath))
            return;

        path = validatedPath;

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
        var scale = FontScale.ScaleFor(mode);

        foreach (var baseSize in FontScale.Steps)
            Current.Resources[FontScale.ResourceKey(baseSize)] = baseSize * scale;

        Current.Resources["AppFontSize"] = FontScale.BaseSize * scale;
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

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Services?.GetService<SystemTrayService>()?.PrepareForExit();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        SingleInstance.Activated -= OnSecondInstanceActivated;
        try { Services.GetRequiredService<BrowserStatusPipeService>().Dispose(); }
        catch { /* Best-effort shutdown of the browser status endpoint. */ }
        try { Services.GetRequiredService<SessionPasswordCache>().Clear(); }
        catch { /* Best-effort cleanup on exit. */ }
        if (_windowsSecurityService is not null)
        {
            _windowsSecurityService.SecurityLockRequested -= OnWindowsSecurityLockRequested;
            _windowsSecurityService.Dispose();
            _windowsSecurityService = null;
        }
        ZoomBehavior.GlobalZoomChanged -= OnGlobalZoomChanged;
        SingleInstance.Release();

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
