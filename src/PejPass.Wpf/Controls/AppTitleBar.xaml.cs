using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PejPass.Wpf.Controls;

public partial class AppTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(AppTitleBar),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowMinimizeProperty =
        DependencyProperty.Register(
            nameof(ShowMinimize),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(
            nameof(ShowMaximize),
            typeof(bool),
            typeof(AppTitleBar),
            new PropertyMetadata(true));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
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

        w.StateChanged += (_, _) => UpdateMaxIcon();
        UpdateMaxIcon();

        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount == 2 && ShowMaximize)
            {
                Maximize_Click(this, args);
                args.Handled = true;
            }
        };
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Host?.Close();
    }
}
