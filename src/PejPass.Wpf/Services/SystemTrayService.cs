using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using PejPass.Domain.Settings;
using Application = System.Windows.Application;

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
    private Icon? _ownedIcon;
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

        _ownedIcon = LoadAppIcon();

        // Created in code (not in the visual tree) — must ForceCreate so the native
        // shell icon is registered. Loaded never fires without a parent.
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "PejPass",
            Icon = _ownedIcon,
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            Visibility = Visibility.Visible
        };

        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);

        _taskbarIcon.TrayLeftMouseUp += (_, _) =>
            ShowRequested?.Invoke(this, EventArgs.Empty);

        _taskbarIcon.TrayMouseDoubleClick += (_, _) =>
            ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        if (!IsEnabled)
            return;

        // Must run on the UI thread (Closing/StateChanged already are).
        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.Invoke(Show);
            return;
        }

        Initialize();
        if (_taskbarIcon is not null)
        {
            _taskbarIcon.Visibility = Visibility.Visible;
            // Re-assert native icon in case shell dropped it after hide.
            _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
        }
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

    /// <summary>
    /// Load the app .ico as System.Drawing.Icon (what the shell tray expects).
    /// BitmapImage + IconSource is unreliable for .ico in pure code paths.
    /// </summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/PejPass.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream is not null)
            {
                // Icon must outlive the stream — clone after reading.
                using var ms = new MemoryStream();
                streamInfo.Stream.CopyTo(ms);
                ms.Position = 0;
                using var temp = new Icon(ms);
                return (Icon)temp.Clone();
            }
        }
        catch
        {
            // Fall through.
        }

        try
        {
            // Fallback: executable icon / system default.
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // Fall through.
        }

        return SystemIcons.Application;
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

        _ownedIcon?.Dispose();
        _ownedIcon = null;
    }
}
