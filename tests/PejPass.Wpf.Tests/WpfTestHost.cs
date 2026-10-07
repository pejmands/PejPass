using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Settings;
using PejPass.Infrastructure.Crypto;
using PejPass.Infrastructure.Import;
using PejPass.Infrastructure.Storage;
using PejPass.Wpf;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Reflection;
using System.Windows;

namespace PejPass.Wpf.Tests;

internal sealed class WpfTestHost : IDisposable
{
    private readonly App _app;
    private readonly IServiceProvider _services;
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private bool _disposed;

    private WpfTestHost()
    {
        _app = new App();
        _app.InitializeComponent();
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = new AppSettings
        {
            AutoLockMinutes = 0,
            MinimizeToSystemTray = false,
            CloseToSystemTray = false
        };

        _themeService = new ThemeService(_settings);
        _themeService.Apply();

        var vaultSession = new VaultSession();
        var crypto = new CryptoService();
        var fileMover = new FileMover();
        var vaultStore = new VaultStore(crypto, fileMover);
        var vaultService = new VaultService(vaultStore);
        var clipboardProvider = new WpfClipboardProvider();
        var uiDispatcher = new WpfUiDispatcher(_app.Dispatcher);
        var clipboardService = new ClipboardService(
            clipboardProvider,
            uiDispatcher);
        var importService = new BrowserImportService();
        var tray = new SystemTrayService(
            _themeService,
            vaultSession);

        var services = new ServiceCollection();

        services.AddSingleton(_settings);
        services.AddSingleton(_themeService);
        services.AddSingleton(vaultSession);
        services.AddSingleton(crypto);
        services.AddSingleton(fileMover);
        services.AddSingleton<IVaultStore>(vaultStore);
        services.AddSingleton(vaultService);
        services.AddSingleton<IBrowserImportService>(importService);
        services.AddSingleton<IClipboardProvider>(clipboardProvider);
        services.AddSingleton<IUiDispatcher>(uiDispatcher);
        services.AddSingleton<IClipboardService>(clipboardService);
        services.AddSingleton<SystemTrayService>(tray);

        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        _services = services.BuildServiceProvider();
        SetAppServices(_services);
    }

    public AppSettings Settings => _settings;

    public SystemTrayService Tray =>
        _services.GetRequiredService<SystemTrayService>();

    public MainWindow CreateMainWindow()
    {
        return _services.GetRequiredService<MainWindow>();
    }

    public LoginWindow CreateLoginWindow()
    {
        return _services.GetRequiredService<LoginWindow>();
    }

    public static void Run(Action<WpfTestHost> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var host = new WpfTestHost();
                action(host);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("The WPF test thread did not finish within 15 seconds.");

        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _settings.MinimizeToSystemTray = false;
        _settings.CloseToSystemTray = false;

        try
        {
            foreach (var window in _app.Windows.OfType<MainWindow>())
            {
                if (window.DataContext is MainViewModel vm)
                    vm.StopBackgroundTimers();
            }

            foreach (var window in _app.Windows.OfType<Window>())
                window.Hide();

            _app.Shutdown();
        }
        catch
        {
        }

        try
        {
            _services.Dispose();
        }
        catch
        {
        }

        _themeService.Dispose();

        SetAppServices(null);
    }

    private static void SetAppServices(IServiceProvider? services)
    {
        var property = typeof(App).GetProperty(
            nameof(App.Services),
            BindingFlags.Public | BindingFlags.Static);

        var setter = property?.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("App.Services setter was not found.");

        setter.Invoke(null, [services]);
    }
}
