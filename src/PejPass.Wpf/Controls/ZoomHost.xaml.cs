using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PejPass.Wpf.Controls;

public partial class ZoomHost : UserControl
{
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(ZoomHost),
            new PropertyMetadata(1d));

    private static readonly double[] ZoomLevels =
    [
        0.8,
        0.9,
        1.0,
        1.1,
        1.2,
        1.3,
        1.4
    ];

    private Window? _window;

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public ZoomHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
            return;

        _window = Window.GetWindow(this);
        if (_window is not null)
            _window.PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.PreviewKeyDown -= OnPreviewKeyDown;
            _window = null;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        if (e.Key is Key.Add or Key.OemPlus)
        {
            SetZoom(GetNextZoom(Zoom));
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Subtract or Key.OemMinus)
        {
            SetZoom(GetPreviousZoom(Zoom));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D0)
        {
            SetZoom(1d);
            e.Handled = true;
        }
    }

    private void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, ZoomLevels[0], ZoomLevels[^1]);
    }

    private static double GetNextZoom(double zoom)
    {
        foreach (var level in ZoomLevels)
        {
            if (level > zoom + 0.001)
                return level;
        }

        return ZoomLevels[^1];
    }

    private static double GetPreviousZoom(double zoom)
    {
        for (var i = ZoomLevels.Length - 1; i >= 0; i--)
        {
            if (ZoomLevels[i] < zoom - 0.001)
                return ZoomLevels[i];
        }

        return ZoomLevels[0];
    }
}
