using H.NotifyIcon;
using H.NotifyIcon.Core;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using Media = System.Windows.Media;

namespace PejPass.Wpf.Services;

/// <summary>
/// System-tray via H.NotifyIcon.Wpf. The tray icon is always available.
/// </summary>
public sealed partial class SystemTrayService : IDisposable
{
    internal const string HostWindowTag = "PejPass.TrayHost";

    internal static bool IsHostWindow(Window window) =>
        window.Tag as string == HostWindowTag;

    private readonly ThemeService _themeService;
    private readonly VaultSession _vaultSession;
    private TaskbarIcon? _taskbarIcon;
    private Window? _hostWindow;
    private Icon? _unlockedIcon;
    private Icon? _lockedIcon;
    private ContextMenu? _contextMenu;
    private MenuItem? _lockMenuItem;
    private bool _isExiting;
    private bool _disposed;
    private IntPtr _previousForegroundWindow;
    private IntPtr _foregroundHostHandle;
    private bool _restoreForegroundOnMenuClosed;

    public bool IsVisible =>
        _taskbarIcon is not null &&
        _taskbarIcon.Visibility == Visibility.Visible;

    public event EventHandler? LockRequested;
    public event EventHandler? ShowRequested;
    public event EventHandler? PasswordGeneratorRequested;

    public SystemTrayService(ThemeService themeService, VaultSession vaultSession)
    {
        _themeService = themeService;
        _vaultSession = vaultSession;

        _themeService.ThemeChanged += OnThemeChanged;
        _vaultSession.StateChanged += OnVaultSessionStateChanged;
    }

    public void Initialize()
    {
        if (_taskbarIcon is not null)
            return;

        EnsureUiThread();

        _unlockedIcon = LoadAppIcon("Assets/PejPass.ico") ??
            LoadFallbackIcon();
        _lockedIcon = LoadAppIcon("Assets/PejPassLocked.ico") ??
            (Icon)_unlockedIcon.Clone();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = GetTrayToolTip(),
            Icon = CreateTrayIcon(),
            // H.NotifyIcon calls SetForegroundWindow after opening its menu,
            // which can activate Application.MainWindow. Open it ourselves instead.
            MenuActivation = PopupActivationMode.None,
            Visibility = Visibility.Visible
        };

        SetContextMenu(CreateContextMenu());

        // Parent into a zero-size host so Loaded/ForceCreate run reliably
        // (code-only TaskbarIcon never gets Loaded without a visual parent).
        _hostWindow = new Window
        {
            Tag = HostWindowTag,
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Media.Brushes.Transparent,
            Opacity = 0,
            ResizeMode = ResizeMode.NoResize,
            Title = "PejPass Tray Host",
            Content = _taskbarIcon
        };

        _hostWindow.Show();
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);

        // Single left click does nothing. Double left click shows the window.
        _taskbarIcon.TrayRightMouseUp += (_, _) => OpenContextMenu();
        _taskbarIcon.TrayMouseDoubleClick += (_, _) =>
            ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        var app = System.Windows.Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Show);
            return;
        }

        Initialize();

        _taskbarIcon?.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        _taskbarIcon?.Visibility = Visibility.Collapsed;
    }

    /// <summary>Stops the tray from intercepting window closes (update install, session end).</summary>
    public void PrepareForExit()
    {
        _isExiting = true;
        Hide();
    }

    public void Exit()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        Hide();
        Dispose();

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });
    }

    public bool TryHideToTray(Window window, CancelEventArgs e)
    {
        if (_isExiting)
            return false;

        e.Cancel = true;
        window.Hide();
        Show();
        return true;
    }

    private static void EnsureUiThread()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        if (!app.Dispatcher.CheckAccess())
            throw new InvalidOperationException("SystemTrayService must be used on the UI thread.");
    }

    private void OpenContextMenu()
    {
        if (_contextMenu is not { } menu || _disposed)
            return;

        menu.PlacementTarget = _taskbarIcon;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;

        // Activate the invisible tray host, not Application.MainWindow. WPF
        // needs a foreground owner for a tray ContextMenu to dismiss on an
        // outside click, but activating the app's main window can reveal Login.
        var hostHandle = _hostWindow is null
            ? IntPtr.Zero
            : new WindowInteropHelper(_hostWindow).Handle;
        var foregroundBefore = GetForegroundWindow();

        _restoreForegroundOnMenuClosed =
            hostHandle != IntPtr.Zero &&
            foregroundBefore != IntPtr.Zero &&
            foregroundBefore != hostHandle &&
            SetForegroundWindow(hostHandle);

        _previousForegroundWindow = _restoreForegroundOnMenuClosed
            ? foregroundBefore
            : IntPtr.Zero;
        _foregroundHostHandle = _restoreForegroundOnMenuClosed
            ? hostHandle
            : IntPtr.Zero;

        menu.IsOpen = true;
    }

    private void RestoreForegroundAfterMenuClosed()
    {
        if (!_restoreForegroundOnMenuClosed)
            return;

        _restoreForegroundOnMenuClosed = false;

        var previousHandle = _previousForegroundWindow;
        var hostHandle = _foregroundHostHandle;

        _previousForegroundWindow = IntPtr.Zero;
        _foregroundHostHandle = IntPtr.Zero;

        if (_disposed ||
            _isExiting ||
            previousHandle == IntPtr.Zero ||
            hostHandle == IntPtr.Zero)
        {
            return;
        }

        // If another app became foreground, leave it alone. Restore the prior
        // foreground window only when the menu left the invisible tray host active.
        if (GetForegroundWindow() == hostHandle)
            _ = SetForegroundWindow(previousHandle);
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = GetContextMenuStyle(),
            PlacementTarget = _taskbarIcon!,
            StaysOpen = false
        };

        menu.Items.Add(CreateMenuItem("Show", () => ShowRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(CreateMenuItem("Password Generator", () => PasswordGeneratorRequested?.Invoke(this, EventArgs.Empty)));

        _lockMenuItem = CreateMenuItem("Lock vault", () => LockRequested?.Invoke(this, EventArgs.Empty));
        _lockMenuItem.Visibility = _vaultSession.IsActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        menu.Items.Add(_lockMenuItem);

        menu.Items.Add(new Separator
        {
            Style = GetSeparatorStyle()
        });
        menu.Items.Add(CreateMenuItem("Exit", Exit));

        // H.NotifyIcon opens the menu with an absolute screen point.
        // Reapply mouse-point placement after WPF has created the popup so
        // the first opening uses WPF's actual cursor position as well.
        menu.Opened += (_, _) =>
        {
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
        };
        menu.Closed += (_, _) => RestoreForegroundAfterMenuClosed();

        return menu;
    }

    private void SetContextMenu(ContextMenu menu)
    {
        if (_taskbarIcon is null)
            return;

        var oldMenu = _contextMenu;
        _contextMenu = menu;
        _taskbarIcon.ContextMenu = menu;

        oldMenu?.IsOpen = false;
        oldMenu?.ClearValue(ContextMenu.PlacementTargetProperty);
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = GetMenuItemStyle()
        };

        item.Click += (_, _) => action();
        return item;
    }

    private void OnVaultSessionStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        var app = System.Windows.Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => OnVaultSessionStateChanged(this, EventArgs.Empty));
            return;
        }

        if (_taskbarIcon is null)
            return;

        _taskbarIcon.ToolTipText = GetTrayToolTip();
        _taskbarIcon.Icon = CreateTrayIcon();

        _lockMenuItem?.Visibility = _vaultSession.IsActive
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private string GetTrayToolTip() =>
        _vaultSession.IsActive
            ? "PejPass — Unlocked"
            : "PejPass — Locked";

    private Icon CreateTrayIcon() =>
        (Icon)(_vaultSession.IsActive
            ? _unlockedIcon!
            : _lockedIcon!).Clone();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        var app = System.Windows.Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => OnThemeChanged(this, EventArgs.Empty));
            return;
        }

        if (_taskbarIcon is null)
            return;

        _contextMenu?.IsOpen = false;

        SetContextMenu(CreateContextMenu());
    }

    private static Style? GetContextMenuStyle() =>
        System.Windows.Application.Current?.TryFindResource("PejPassContextMenu") as Style;

    private static Style? GetMenuItemStyle() =>
        System.Windows.Application.Current?.TryFindResource("PejPassContextMenuItem") as Style;

    private static Style? GetSeparatorStyle() =>
        System.Windows.Application.Current?.TryFindResource("PejPassContextMenuSeparator") as Style;

    private static Icon? LoadAppIcon(string resourcePath)
    {
        try
        {
            var uri = new Uri(
                $"pack://application:,,,/{resourcePath}",
                UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo?.Stream is not null)
            {
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
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null)
                    return extracted;
            }
        }
        catch
        {
            // Fall through.
        }

        return null;
    }

    private static Icon LoadFallbackIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null)
                    return extracted;
            }
        }
        catch
        {
            // Fall through.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _themeService.ThemeChanged -= OnThemeChanged;
        _vaultSession.StateChanged -= OnVaultSessionStateChanged;

        if (_taskbarIcon is not null)
        {
            try
            {
                _taskbarIcon.Visibility = Visibility.Collapsed;
                _taskbarIcon.Dispose();
            }
            catch
            {
                // ignore
            }
            _taskbarIcon = null;
        }

        _contextMenu = null;

        if (_hostWindow is not null)
        {
            try
            {
                _hostWindow.Close();
            }
            catch
            {
                // ignore
            }
            _hostWindow = null;
        }

        _unlockedIcon?.Dispose();
        _unlockedIcon = null;

        _lockedIcon?.Dispose();
        _lockedIcon = null;
    }
}
