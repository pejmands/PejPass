using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Import;
using PejPass.Infrastructure.Storage;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;

namespace PejPass.Wpf;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        login.Show();
    }
}
