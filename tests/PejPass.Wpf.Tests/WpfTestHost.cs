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
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests;

internal sealed class WpfTestHost : IDisposable
{
    private static readonly Lock SyncRoot = new();
    private static readonly ManualResetEventSlim Ready = new(false);
    private static Thread? _uiThread;
    private static Dispatcher? _dispatcher;
    private static App? _app;

    private readonly IServiceProvider _services;
    private readonly AppSettings _settings;
    private bool _disposed;

    private WpfTestHost()
    {
        _app = GetApplication();

        _settings = new AppSettings
        {
            AutoLockMinutes = 0,
            MinimizeToSystemTray = false,
            CloseToSystemTray = false
        };

        var themeService = new ThemeService(_settings);
        themeService.Apply();

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
            themeService,
            vaultSession);

        var services = new ServiceCollection();

        services.AddSingleton(_settings);
        services.AddSingleton(themeService);
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

    public MainWindow CreateMainWindow() =>
        _services.GetRequiredService<MainWindow>();

    public LoginWindow CreateLoginWindow() =>
        _services.GetRequiredService<LoginWindow>();

    public static void Run(
        Action<WpfTestHost> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureApplication();

        Exception? failure = null;

        _dispatcher!.Invoke(() =>
        {
            try
            {
                using var host = new WpfTestHost();
                cancellationToken.ThrowIfCancellationRequested();
                action(host);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void EnsureApplication()
    {
        lock (SyncRoot)
        {
            if (_dispatcher is not null && _app is not null)
                return;

            _uiThread = new Thread(() =>
            {
                _app = new App();
                _app.InitializeComponent();
                _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                _dispatcher = _app.Dispatcher;
                Ready.Set();

                Dispatcher.Run();
            })
            {
                IsBackground = true
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
        }

        Ready.Wait();
    }

    private static App GetApplication() =>
        _app ?? throw new InvalidOperationException(
            "The WPF test application has not been initialized.");

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _settings.MinimizeToSystemTray = false;
        _settings.CloseToSystemTray = false;

        try
        {
            foreach (var window in _app!.Windows.OfType<MainWindow>())
            {
                if (window.DataContext is MainViewModel vm)
                    vm.StopBackgroundTimers();
            }

            foreach (var window in _app.Windows.OfType<Window>())
                window.Hide();
        }
        catch
        {
        }

        try
        {
            if (_services is IDisposable disposable)
                disposable.Dispose();
        }
        catch
        {
        }

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
