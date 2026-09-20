using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Storage;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;

namespace PejPass.Wpf;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Settings (singleton)
        services.AddSingleton(new AppSettings());

        // Infrastructure
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IVaultStore, VaultStore>();

        // WPF-specific services
        services.AddSingleton<IClipboardService, ClipboardService>();

        // Application
        services.AddSingleton<VaultService>();

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<EntryEditorViewModel>();

        // Windows
        services.AddTransient<LoginWindow>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();

        // Start with Login window
        var login = Services.GetRequiredService<LoginWindow>();
        login.Show();
    }
}
