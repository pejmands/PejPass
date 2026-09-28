using PejPass.Wpf.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PejPass.Wpf.Controls;

public partial class AppTitleBar : UserControl
{
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

        w.StateChanged += (_, _) => UpdateMaxIcon();
        UpdateMaxIcon();

        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount == 2 && ShowMaximize)
            {
                Maximize_Click(this, args);
                args.Handled = true;
                return;
            }

            if (Host is not { } window)
                return;

            if (window.WindowState == WindowState.Maximized)
            {
                var startPoint = args.GetPosition(window);

                MouseMove += StartRestoreDrag;

                void StartRestoreDrag(object? sender, System.Windows.Input.MouseEventArgs moveArgs)
                {
                    if (moveArgs.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                    {
                        MouseMove -= StartRestoreDrag;
                        return;
                    }

                    MouseMove -= StartRestoreDrag;

                    var restoreBounds = window.RestoreBounds;

                    window.WindowState = WindowState.Normal;

                    window.Left =
                        SystemParameters.WorkArea.Left +
                        startPoint.X -
                        (restoreBounds.Width / 2);

                    window.Top =
                        SystemParameters.WorkArea.Top;

                    window.DragMove();
                }
            }
            else
            {
                try
                {
                    window.DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
    }

    private void ApplyChromeFlags()
    {
        MinButton?.Visibility = ShowMinimize ? Visibility.Visible : Visibility.Collapsed;
        MaxButton?.Visibility = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
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
            w.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (Host is not { } w) return;
        w.WindowState = w.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaxIcon();
    }

    private void TitleBar_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TitleBarContextMenu is null)
            return;

        TitleBarContextMenu.PlacementTarget = this;
        TitleBarContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void TitleBarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var w = Host;
        if (w is null)
            return;

        var canMinimize = ShowMinimize && w.ResizeMode is ResizeMode.CanMinimize or ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        var canMaximize = ShowMaximize && w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        var hasFilePath = string.IsNullOrWhiteSpace(FilePath);

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
            hasFilePath
                ? Visibility.Collapsed
                : Visibility.Visible;

        OpenFileLocationMenuItem.Visibility =
            hasFilePath
                ? Visibility.Collapsed
                : Visibility.Visible;

        FileActionsSeparator.Visibility =
            hasFilePath
                ? Visibility.Collapsed
                : Visibility.Visible;

        TitleBarSeparator.Visibility =
            RestoreMenuItem.Visibility == Visibility.Visible ||
            MinimizeMenuItem.Visibility == Visibility.Visible ||
            MaximizeMenuItem.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void RestoreMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
        {
            w.WindowState = WindowState.Normal;
            UpdateMaxIcon();
        }
    }

    private void MinimizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            w.WindowState = WindowState.Minimized;
    }

    private void MaximizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Host is { } w)
        {
            w.WindowState = WindowState.Maximized;
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

    private void OpenFileLocationMenuItem_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePath) ||
            !File.Exists(FilePath))
        {
            SnackbarService.Show("Vault file not found");
            return;
        }

        try
        {
            Process.Start(
                "explorer.exe",
                $"/select,\"{FilePath}\"");

            SnackbarService.Show("Opened file location");
        }
        catch
        {
            SnackbarService.Show("Could not open file location");
        }
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Host?.Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Host?.Close();
}
