using PejPass.Wpf.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace PejPass.Wpf.Controls;

public partial class AppTitleBar : UserControl
{
    private const int WmNcRButtonUp = 0x00A5;
    private const int WmNcRButtonDown = 0x00A4;
    private const int HtCaption = 2;

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

    public static readonly DependencyProperty ShowFileActionsProperty =
        DependencyProperty.Register(
            nameof(ShowFileActions),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(false));

    public bool ShowFileActions
    {
        get => (bool)GetValue(ShowFileActionsProperty);
        set => SetValue(ShowFileActionsProperty, value);
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
    }

    private static void OnFilePathChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not AppTitleBar titleBar)
            return;

        titleBar.UpdateFileActionsState();
    }

    private void UpdateFileActionsState()
    {
        if (OpenFileLocationMenuItem is null)
            return;

        OpenFileLocationMenuItem.IsEnabled =
            !string.IsNullOrWhiteSpace(FilePath) &&
            File.Exists(FilePath);
    }

    private static void OnChromeFlagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppTitleBar bar)
            bar.ApplyChromeFlags();
    }

    private Window? Host => Window.GetWindow(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        if (Host is { } w)
            w.StateChanged -= OnHostStateChanged;

        DetachCaptionMenuHook();
    }

    private void OnHostStateChanged(object? sender, EventArgs e) => UpdateMaxIcon();

    private void AttachCaptionMenuHook(Window window)
    {
        DetachCaptionMenuHook();

        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);
    }

    private void DetachCaptionMenuHook()
    {
        if (_hwndSource is null)
            return;

        _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Caption right-click → themed menu (block system menu).
        // Only open on UP to avoid double-open / jitter; still mark DOWN handled.
        if (msg is WmNcRButtonUp or WmNcRButtonDown)
        {
            if (wParam.ToInt32() == HtCaption)
            {
                if (msg == WmNcRButtonUp)
                {
                    // Defer until after the NC message finishes so placement is stable.
                    Dispatcher.BeginInvoke(OpenTitleBarMenuAtCursor);
                }

                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void OpenTitleBarMenuAtCursor()
    {
        if (TitleBarContextMenu is null)
            return;

        // MousePoint aligns the menu origin with the cursor (DPI-safe, no manual offset).
        TitleBarContextMenu.CustomPopupPlacementCallback = null;
        TitleBarContextMenu.PlacementTarget = this;
        TitleBarContextMenu.Placement = PlacementMode.MousePoint;
        TitleBarContextMenu.HorizontalOffset = 0;
        TitleBarContextMenu.VerticalOffset = 0;
        TitleBarContextMenu.IsOpen = true;
    }

    private void ApplyChromeFlags()
    {
        MinButton.Visibility = ShowMinimize ? Visibility.Visible : Visibility.Collapsed;
        MaxButton.Visibility = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
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

    private void TitleBarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var w = Host;
        if (w is null)
            return;

        var canMinimize = ShowMinimize && w.ResizeMode is ResizeMode.CanMinimize or ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        var canMaximize = ShowMaximize && w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        var noFilePath = string.IsNullOrWhiteSpace(FilePath);

        RestoreMenuItem.Visibility =
            canMaximize && w.WindowState == WindowState.Maximized
                ? Visibility.Visible
                : Visibility.Collapsed;

        MaximizeMenuItem.Visibility =
            canMaximize && w.WindowState != WindowState.Maximized
                ? Visibility.Visible
                : Visibility.Collapsed;

        MinimizeMenuItem.Visibility =
            canMinimize && w.WindowState != WindowState.Minimized
                ? Visibility.Visible
                : Visibility.Collapsed;

        CopyFilePathMenuItem.Visibility =
            noFilePath ? Visibility.Collapsed : Visibility.Visible;

        OpenFileLocationMenuItem.Visibility =
            noFilePath ? Visibility.Collapsed : Visibility.Visible;

        FileActionsSeparator.Visibility =
            noFilePath ? Visibility.Collapsed : Visibility.Visible;

        TitleBarSeparator.Visibility =
            RestoreMenuItem.Visibility == Visibility.Visible ||
            MinimizeMenuItem.Visibility == Visibility.Visible ||
            MaximizeMenuItem.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateFileActionsState();
    }

    private void RestoreMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
        {
            SystemCommands.RestoreWindow(w);
            UpdateMaxIcon();
        }
    }

    private void MinimizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            SystemCommands.MinimizeWindow(w);
    }

    private void MaximizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
        {
            SystemCommands.MaximizeWindow(w);
            UpdateMaxIcon();
        }
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
            Process.Start("explorer.exe", $"/select,\"{FilePath}\"");
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
