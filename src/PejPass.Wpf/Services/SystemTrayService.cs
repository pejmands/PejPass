using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using PejPass.Domain.Settings;

namespace PejPass.Wpf.Services;

/// <summary>
/// Lightweight system-tray integration via H.NotifyIcon.Wpf (pure WPF, no WinForms).
/// Opt-in via <see cref="AppSettings.MinimizeToSystemTray"/>.
/// No sensitive actions from the tray menu.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private readonly AppSettings _settings;
    private TaskbarIcon? _taskbarIcon;
    private bool _isExiting;
    private bool _disposed;

    public SystemTrayService(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsEnabled => _settings.MinimizeToSystemTray;

    public bool IsVisible =>
        _taskbarIcon is not null &&
        _taskbarIcon.Visibility == Visibility.Visible;

    /// <summary>Raised when the user chooses Lock from the tray menu.</summary>
    public event EventHandler? LockRequested;

    /// <summary>Raised when the user chooses Show / activates the icon.</summary>
    public event EventHandler? ShowRequested;

    public void Initialize()
    {
        if (_taskbarIcon is not null)
            return;

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Show", () => ShowRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(CreateMenuItem("Lock vault", () => LockRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Exit", Exit));

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "PejPass",
            IconSource = LoadIconSource(),
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            Visibility = Visibility.Collapsed
        };

        _taskbarIcon.TrayLeftMouseUp += (_, _) =>
            ShowRequested?.Invoke(this, EventArgs.Empty);

        _taskbarIcon.TrayMouseDoubleClick += (_, _) =>
            ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        if (!IsEnabled)
            return;

        Initialize();
        if (_taskbarIcon is not null)
            _taskbarIcon.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        if (_taskbarIcon is not null)
            _taskbarIcon.Visibility = Visibility.Collapsed;
    }

    public void Exit()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        Hide();
        Dispose();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }

    /// <summary>
    /// Call from window Closing handlers.
    /// Returns true if the close should be cancelled (window hidden to tray instead).
    /// </summary>
    public bool TryMinimizeToTray(Window window, CancelEventArgs e)
    {
        if (_isExiting || !IsEnabled)
            return false;

        e.Cancel = true;
        window.Hide();
        Show();
        return true;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static ImageSource? LoadIconSource()
    {
        try
        {
            return new BitmapImage(
                new Uri("pack://application:,,,/Assets/PejPass.ico", UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_taskbarIcon is not null)
        {
            _taskbarIcon.Visibility = Visibility.Collapsed;
            _taskbarIcon.Dispose();
            _taskbarIcon = null;
        }
    }
}
