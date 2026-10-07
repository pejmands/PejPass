using PejPass.Wpf.Services;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PejPass.Wpf.Controls;

public partial class AppTitleBar : UserControl
{
    private const int WmNcRButtonUp = 0x00A5;
    private const int WmNcRButtonDown = 0x00A4;
    private const int WmSysCommand = 0x0112;
    private const int WmContextMenu = 0x007B;
    private const int WmNcHitTest = 0x0084;
    private const int ScKeyMenu = 0xF100;
    private const int VkSpace = 0x20;
    private const int ScMouseMenu = 0xF090;
    private const int HtCaption = 2;
    private const int GwlStyle = -16;
    private const int WsSysMenu = 0x00080000;

    private HwndSource? _hwndSource;

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AppTitleBar),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(
            nameof(FilePath),
            typeof(string),
            typeof(AppTitleBar),
            new PropertyMetadata(
                string.Empty,
                OnFilePathChanged));

    public static readonly DependencyProperty ShowMinimizeProperty =
        DependencyProperty.Register(nameof(ShowMinimize), typeof(bool), typeof(AppTitleBar),
            new PropertyMetadata(true, OnChromeFlagsChanged));

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(AppTitleBar),
            new PropertyMetadata(true, OnChromeFlagsChanged));

    public static readonly DependencyProperty ShowMainActionsProperty =
        DependencyProperty.Register(
            nameof(ShowMainActions),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(false, OnChromeFlagsChanged));

    public static readonly DependencyProperty ShowLoginActionsProperty =
        DependencyProperty.Register(
            nameof(ShowLoginActions),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(false, OnChromeFlagsChanged));

    public static readonly DependencyProperty ShowFileActionsProperty =
        DependencyProperty.Register(
            nameof(ShowFileActions),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowFilePathToolTipProperty =
        DependencyProperty.Register(
            nameof(ShowFilePathToolTip),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(false));

    public bool ShowMainActions
    {
        get => (bool)GetValue(ShowMainActionsProperty);
        set => SetValue(ShowMainActionsProperty, value);
    }

    public bool ShowLoginActions
    {
        get => (bool)GetValue(ShowLoginActionsProperty);
        set => SetValue(ShowLoginActionsProperty, value);
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? WhatsNewRequested;

    public bool ShowFileActions
    {
        get => (bool)GetValue(ShowFileActionsProperty);
        set => SetValue(ShowFileActionsProperty, value);
    }

    public bool ShowFilePathToolTip
    {
        get => (bool)GetValue(ShowFilePathToolTipProperty);
        set => SetValue(ShowFilePathToolTipProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public bool ShowMinimize
    {
        get => (bool)GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    public AppTitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        TitleBarContextMenu.Closed += (_, _) =>
        {
            TitleBarContextMenu.Placement = PlacementMode.MousePoint;
            TitleBarContextMenu.HorizontalOffset = 0;
            TitleBarContextMenu.VerticalOffset = 0;
            TitleBarContextMenu.PlacementTarget = null;
        };
    }

    private static void OnFilePathChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is AppTitleBar)
            UpdateFileActionsVisibility();
    }

    private static void OnChromeFlagsChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is AppTitleBar bar)
            bar.ApplyChromeFlags();
    }

    private Window? Host => Window.GetWindow(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAvailability.Changed += OnUpdateAvailabilityChanged;
        ApplyUpdateBadge();

        var w = Host;
        if (w is null) return;

        if (string.IsNullOrEmpty(Title))
            Title = w.Title;

        if (w.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
            ShowMaximize = false;
        if (w.ResizeMode is ResizeMode.NoResize)
            ShowMinimize = false;

        ApplyChromeFlags();

        w.StateChanged -= OnHostStateChanged;
        w.StateChanged += OnHostStateChanged;
        UpdateMaxIcon();

        // Caption drag / double-click maximize: owned by WindowChrome (CaptionHeight).
        // Intercept caption right-click so we show the themed custom menu instead of the system menu.
        AttachCaptionMenuHook(w);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UpdateAvailability.Changed -= OnUpdateAvailabilityChanged;

        if (Host is { } window)
        {
            window.StateChanged -= OnHostStateChanged;
            window.SourceInitialized -= OnSourceInitialized;
        }

        DetachHook();
    }

    private void AttachCaptionMenuHook(Window window)
    {
        DetachCaptionMenuHook();

        void Attach()
        {
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            // Remove WS_SYSMENU so the OS never shows the native system menu
            // (fixes Login / PasswordPrompt where NC messages alone were not enough).
            StripSystemMenu(helper.Handle);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Attach();
        else
            window.SourceInitialized += (_, _) => Attach();
    }

    private static void StripSystemMenu(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, checked((IntPtr)(style & ~WsSysMenu)));
    }

    private void DetachCaptionMenuHook()
    {
        if (_hwndSource is null)
            return;

        _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private void OnUpdateAvailabilityChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ApplyUpdateBadge);
            return;
        }

        ApplyUpdateBadge();
    }

    private void ApplyUpdateBadge()
    {
        var visible = UpdateAvailability.IsUpdateAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;

        MoreActionsUpdateBadge?.Visibility = visible;

        LoginMoreUpdateBadge?.Visibility = visible;

        var aboutHeader = UpdateAvailability.IsUpdateAvailable
            ? "About PejPass  ●"
            : "About PejPass";

        MoreActionsAboutMenuItem?.Header = aboutHeader;

        MoreActionsPopupAboutMenuItem?.Header = aboutHeader;

        LoginAboutMenuItem?.Header = aboutHeader;

        if (UpdateAvailability.IsUpdateAvailable)
        {
            MoreActionsButton?.ToolTip = "More actions — update available";
            LoginMoreButton?.ToolTip = "More — update available";
        }
        else
        {
            MoreActionsButton?.ToolTip = "More actions";
            LoginMoreButton?.ToolTip = "More";
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (Host is { } window)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle != IntPtr.Zero)
                AttachHook(helper.Handle);
        }
    }

    private void AttachHook(IntPtr hwnd)
    {
        DetachHook();
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    private void DetachHook()
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Caption right-click → themed menu only (never the system menu).
        if (msg is WmNcRButtonUp or WmNcRButtonDown)
        {
            if (wParam.ToInt32() == HtCaption)
            {
                if (msg == WmNcRButtonUp)
                {
                    GetCursorPos(out var pt);
                    var screenX = pt.X;
                    var screenY = pt.Y;
                    Dispatcher.BeginInvoke(() => OpenTitleBarMenuAtScreenPoint(screenX, screenY));
                }

                handled = true;
                return IntPtr.Zero;
            }
        }

        // Alt+Space → themed menu at window top-left (like the classic system menu).
        // SC_MOUSEMENU → themed menu at cursor.
        if (msg == WmSysCommand)
        {
            var cmd = wParam.ToInt32() & 0xFFF0;
            if (cmd == ScKeyMenu && (lParam.ToInt64() & 0xFFFF) == VkSpace)
            {
                Dispatcher.BeginInvoke(OpenTitleBarMenuAtWindowTopLeft);
                handled = true;
                return IntPtr.Zero;
            }

            if (cmd == ScMouseMenu)
            {
                GetCursorPos(out var pt);
                var screenX = pt.X;
                var screenY = pt.Y;
                Dispatcher.BeginInvoke(() => OpenTitleBarMenuAtScreenPoint(screenX, screenY));
                handled = true;
                return IntPtr.Zero;
            }
        }

        // Context menu only when the hit-test is the caption (do not steal client menus).
        if (msg == WmContextMenu)
        {
            GetCursorPos(out var pt);
            var hit = (int)SendMessage(
                hwnd,
                WmNcHitTest,
                IntPtr.Zero,
                MakeLParam(pt.X, pt.Y));

            if (hit == HtCaption)
            {
                Dispatcher.BeginInvoke(() => OpenTitleBarMenuAtScreenPoint(pt.X, pt.Y));
                handled = true;
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr MakeLParam(int lo, int hi) =>
        (hi << 16) | (lo & 0xFFFF);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out PointNative lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    private void OpenTitleBarMenuAtWindowTopLeft()
    {
        if (TitleBarContextMenu is null || Host is not { } w)
            return;

        if (TitleBarContextMenu.IsOpen)
            TitleBarContextMenu.IsOpen = false;

        // Anchor at the window's top-left (classic system-menu position), not under the cursor.
        TitleBarContextMenu.CustomPopupPlacementCallback = null;
        TitleBarContextMenu.PlacementTarget = w;
        TitleBarContextMenu.Placement = PlacementMode.Relative;
        TitleBarContextMenu.HorizontalOffset = 0;
        TitleBarContextMenu.VerticalOffset = 0;
        TitleBarContextMenu.IsOpen = true;
    }

    private void OpenTitleBarMenuAtScreenPoint(int screenX, int screenY)
    {
        if (TitleBarContextMenu is null)
            return;

        if (TitleBarContextMenu.IsOpen)
            TitleBarContextMenu.IsOpen = false;

        var dip = DevicePixelsToDip(screenX, screenY);

        TitleBarContextMenu.CustomPopupPlacementCallback = null;
        TitleBarContextMenu.PlacementTarget = null;
        TitleBarContextMenu.Placement = PlacementMode.Absolute;
        TitleBarContextMenu.HorizontalOffset = dip.X;
        TitleBarContextMenu.VerticalOffset = dip.Y;
        TitleBarContextMenu.IsOpen = true;
    }

    private Point DevicePixelsToDip(int screenX, int screenY)
    {
        var source = PresentationSource.FromVisual(this)
                     ?? (Host is { } w ? PresentationSource.FromVisual(w) : null);

        if (source?.CompositionTarget is { } ct)
            return ct.TransformFromDevice.Transform(new Point(screenX, screenY));

        // Fallback: assume 96 DPI
        return new Point(screenX, screenY);
    }

    private void TitleBarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        UpdateSystemMenuItems();
    }

    private void UpdateSystemMenuItems()
    {
        var w = Host;
        if (w is null) return;

        var maximized = w.WindowState == WindowState.Maximized;
        RestoreMenuItem.IsEnabled = maximized;
        MaximizeMenuItem.IsEnabled = !maximized && w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        MinimizeMenuItem.IsEnabled = w.ResizeMode != ResizeMode.NoResize;

        var hasPath = !string.IsNullOrWhiteSpace(FilePath) && ShowFileActions;
        CopyFilePathMenuItem.Visibility = hasPath ? Visibility.Visible : Visibility.Collapsed;
        OpenFileLocationMenuItem.Visibility = hasPath ? Visibility.Visible : Visibility.Collapsed;
        FileActionsSeparator.Visibility = hasPath ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void UpdateFileActionsVisibility()
    {
    }

    private void ApplyChromeFlags()
    {
        MinButton.Visibility = ShowMinimize ? Visibility.Visible : Visibility.Collapsed;
        MaxButton.Visibility = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;

        MainActionsPanel.Visibility = ShowMainActions
            ? Visibility.Visible
            : Visibility.Collapsed;

        LoginActionsPanel.Visibility = ShowLoginActions
            ? Visibility.Visible
            : Visibility.Collapsed;

        MainActionsSeparator.Visibility = ShowMainActions || ShowLoginActions
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void LoginMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (LoginMoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = LoginMoreButton;
            menu.Placement = PlacementMode.Bottom;
            menu.VerticalOffset = 6;
            menu.IsOpen = true;
        }
        e.Handled = true;
    }

    private void LoginMoreButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void LoginSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void LoginAbout_Click(object sender, RoutedEventArgs e)
    {
        AboutRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void LoginWhatsNew_Click(object sender, RoutedEventArgs e)
    {
        WhatsNewRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void TitleText_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void TitleText_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        GetCursorPos(out var pt);
        OpenTitleBarMenuAtScreenPoint(pt.X, pt.Y);
        e.Handled = true;
    }

    private void TitleText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Host is not { } window)
            return;

        if (e.ClickCount == 2)
        {
            if (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            {
                if (window.WindowState == WindowState.Maximized)
                    SystemCommands.RestoreWindow(window);
                else
                    SystemCommands.MaximizeWindow(window);

                UpdateMaxIcon();
            }

            e.Handled = true;
            return;
        }

        if (e.ClickCount == 1)
        {
            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
            }

            e.Handled = true;
        }
    }

    private void MoreActionsButton_OnPreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreActionsButton;
            menu.Placement = PlacementMode.Bottom;
            menu.VerticalOffset = 6;
            menu.IsOpen = true;
        }
        e.Handled = true;
    }

    private void UpdateMaxIcon()
    {
        var w = Host;
        if (w is null) return;

        MaxButton.ApplyTemplate();
        if (MaxButton.Template?.FindName("MaxIcon", MaxButton) is not System.Windows.Shapes.Path path)
            return;

        if (w.WindowState == WindowState.Maximized)
        {
            path.Data = Geometry.Parse("M2,0 H10 V8 H2 Z M0,2 H8 V10 H0 Z");
            MaxButton.ToolTip = "Restore";
        }
        else
        {
            path.Data = Geometry.Parse("M0,0 H9 V9 H0 Z");
            MaxButton.ToolTip = "Maximize";
        }
    }

    private void OnHostStateChanged(object? sender, EventArgs e) => UpdateMaxIcon();

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.MinimizeWindow(w);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (Host is not { } w) return;

        if (w.WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(w);
        else
            SystemCommands.MaximizeWindow(w);

        UpdateMaxIcon();
    }

    private void RestoreMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.RestoreWindow(w);
    }

    private void MinimizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.MinimizeWindow(w);
    }

    private void MaximizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.MaximizeWindow(w);
    }

    private void CopyFilePathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return;

        try
        {
            Clipboard.SetText(FilePath);
            SnackbarService.Show("File path copied");
        }
        catch
        {
            SnackbarService.Show("Could not copy file path");
        }
    }

    private void OpenFileLocationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            SnackbarService.Show("Vault file not found");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{FilePath}\"",
                UseShellExecute = true
            });

            SnackbarService.Show("Opened file location");
        }
        catch
        {
            SnackbarService.Show("Could not open file location");
        }
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.CloseWindow(w);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.CloseWindow(w);
    }
}
