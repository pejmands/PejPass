using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Import;
using PejPass.Infrastructure.Storage;
using PejPass.Wpf.Controls;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace PejPass.Wpf;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var launchVaultPath = ParseVaultPathArg(e.Args);

        if (!SingleInstance.TryAcquire(launchVaultPath))
        {
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyCustomChrome));

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        SingleInstance.Activated += OnSecondInstanceActivated;

        UiPolish.Register();

        VaultFileAssociation.EnsureRegistered();

        var settings = SettingsStore.Load();
        var themeService = new ThemeService(settings);

        themeService.Apply();

        var services = new ServiceCollection();

        services.AddSingleton(settings);
        services.AddSingleton(themeService);

        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IVaultStore, VaultStore>();
        services.AddSingleton<IBrowserImportService, BrowserImportService>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<VaultService>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<EntryEditorViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<LoginWindow>();
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();

        Services = services.BuildServiceProvider();

        var login = Services.GetRequiredService<LoginWindow>();
        MainWindow = login;

        if (!string.IsNullOrWhiteSpace(launchVaultPath) &&
            login.DataContext is LoginViewModel loginVm)
        {
            loginVm.ApplyExternalVaultPath(launchVaultPath);
        }

        login.Show();
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

        if (LoginViewModel.CurrentVault is not null)
        {
            var current = LoginViewModel.CurrentVaultPath;
            if (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(current), path, StringComparison.OrdinalIgnoreCase))
                        return;
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

    private static void ApplyCustomChrome(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (WindowChrome.GetWindowChrome(window) is null)
        {
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 40,
                ResizeBorderThickness = new Thickness(5),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
        }

        if (FindVisualChild<AppTitleBar>(window) is null)
            InjectTitleBar(window);
    }

    private static void InjectTitleBar(Window window)
    {
        if (window.Content is not UIElement body)
            return;

        window.Content = null;

        var bar = new AppTitleBar
        {
            Title = window.Title,
            ShowMinimize = window.ResizeMode is not ResizeMode.NoResize,
            ShowMaximize = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip
        };

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(body);
        window.Content = dock;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
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
        SingleInstance.Release();
        PendingVaultOpen.ReadAndClear();
        base.OnExit(e);
    }
}
