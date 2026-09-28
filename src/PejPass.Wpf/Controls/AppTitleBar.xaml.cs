using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PejPass.Wpf.Controls;

public partial class AppTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AppTitleBar),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(AppTitleBar),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowMinimizeProperty =
        DependencyProperty.Register(nameof(ShowMinimize), typeof(bool), typeof(AppTitleBar),
            new PropertyMetadata(true, OnChromeFlagsChanged));

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(AppTitleBar),
            new PropertyMetadata(true, OnChromeFlagsChanged));

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

            if (args.ClickCount == 1 && Host is { } window)
            {
                try
                {
                    window.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The drag can be interrupted when the window state changes.
                }
            }
        };
    }

    private void ApplyChromeFlags()
    {
        if (MinButton is not null)
            MinButton.Visibility = ShowMinimize ? Visibility.Visible : Visibility.Collapsed;
        if (MaxButton is not null)
            MaxButton.Visibility = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMaxIcon()
    {
        var w = Host;
        if (w is null) return;

        MaxButton.ApplyTemplate();
        if (MaxButton.Template?.FindName("MaxIcon", MaxButton) is not Path path)
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
        }
        catch
        {
            // Clipboard access can fail temporarily when another process owns it.
        }
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Host?.Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Host?.Close();
}
