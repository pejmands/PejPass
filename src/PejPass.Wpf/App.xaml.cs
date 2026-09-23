using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Import;
using PejPass.Infrastructure.Storage;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.Windows;

namespace PejPass.Wpf;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var launchVaultPath = ParseVaultPathArg(e.Args);

        // Single instance: second launch activates existing window (and forwards vault path)
        if (!SingleInstance.TryAcquire(launchVaultPath))
        {
            // Hard exit — do not keep a half-started WPF process around
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        SingleInstance.Activated += OnSecondInstanceActivated;

        UiPolish.Register();

        // So double-click on *.pejpass opens PejPass (per-user, no admin required)
        VaultFileAssociation.EnsureRegistered();

        var settings = SettingsStore.Load();
        var themeService = new ThemeService(settings);

        // Apply theme AFTER Application.Resources exist
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

    /// <summary>
    /// Apply a vault path from double-click / second instance.
    /// If a vault is already unlocked, only same-path is a no-op; otherwise ask to lock first.
    /// </summary>
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

        // Already unlocked?
        if (LoginViewModel.CurrentVault is not null)
        {
            var current = LoginViewModel.CurrentVaultPath;
            if (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(current), path, StringComparison.OrdinalIgnoreCase))
                        return; // same vault — already brought to front
                }
                catch
                {
                    // fall through to message
                }
            }

            DialogService.Info(
                "A vault is already open.\n\nLock it first if you want to open a different .pejpass file.",
                "PejPass");
            return;
        }

        // On login screen — point at the file
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

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Activated -= OnSecondInstanceActivated;
        SingleInstance.Release();
        PendingVaultOpen.ReadAndClear();
        base.OnExit(e);
    }
}
