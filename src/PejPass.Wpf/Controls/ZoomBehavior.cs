using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf.Controls;

public static class ZoomBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ZoomBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.RegisterAttached(
            "Zoom",
            typeof(double),
            typeof(ZoomBehavior),
            new PropertyMetadata(1d, OnZoomChanged));

    public static readonly double[] ZoomLevels =
    [
        0.8,
        0.9,
        1.0,
        1.1,
        1.2,
        1.3,
        1.4
    ];

    private static readonly DependencyProperty WindowProperty =
        DependencyProperty.RegisterAttached(
            "Window",
            typeof(Window),
            typeof(ZoomBehavior),
            new PropertyMetadata(null));

    private static readonly HashSet<FrameworkElement> Targets = [];

    private static double _globalZoom = 1d;

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static double GetZoom(DependencyObject element) =>
        (double)element.GetValue(ZoomProperty);

    public static void SetZoom(DependencyObject element, double value) =>
        element.SetValue(ZoomProperty, value);

    public static double GlobalZoom => _globalZoom;

    public static event EventHandler<double>? GlobalZoomChanged;

    public static void SetGlobalZoom(double zoom)
    {
        var scale = ClampZoom(zoom);
        if (Math.Abs(scale - _globalZoom) < 0.001)
            return;

        _globalZoom = scale;

        foreach (var target in Targets.ToArray())
            SetZoom(target, scale);

        GlobalZoomChanged?.Invoke(null, scale);
    }

    private static Window? GetWindow(DependencyObject element) =>
        (Window?)element.GetValue(WindowProperty);

    private static void SetWindow(DependencyObject element, Window? value) =>
        element.SetValue(WindowProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            RegisterTarget(element);
        }
        else
        {
            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;
            UnregisterTarget(element);
            DetachWindow(element);
            element.LayoutTransform = null;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        RegisterTarget(element);

        var window = Window.GetWindow(element);
        if (window is null)
            return;

        SetWindow(element, window);
        window.PreviewKeyDown -= OnPreviewKeyDown;
        window.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UnregisterTarget(element);
            DetachWindow(element);
        }
    }

    private static void RegisterTarget(FrameworkElement element)
    {
        Targets.Add(element);
        SetZoom(element, _globalZoom);
    }

    private static void UnregisterTarget(FrameworkElement element)
    {
        Targets.Remove(element);
    }

    private static void DetachWindow(FrameworkElement element)
    {
        var window = GetWindow(element);
        if (window is not null)
            window.PreviewKeyDown -= OnPreviewKeyDown;

        SetWindow(element, null);
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
            ApplyZoom(element, (double)e.NewValue);
    }

    private static void ApplyZoom(FrameworkElement element, double zoom)
    {
        var scale = ClampZoom(zoom);
        element.LayoutTransform = scale == 1d
            ? null
            : new ScaleTransform(scale, scale);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Window window ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        if (e.Key is Key.Add or Key.OemPlus)
        {
            SetGlobalZoom(GetNextZoom(_globalZoom));
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Subtract or Key.OemMinus)
        {
            SetGlobalZoom(GetPreviousZoom(_globalZoom));
            e.Handled = true;
            return;
        }

        if (e.Key is Key.D0 or Key.NumPad0)
        {
            SetGlobalZoom(1d);
            e.Handled = true;
        }
    }

    private static double ClampZoom(double zoom) =>
        Math.Clamp(zoom, ZoomLevels[0], ZoomLevels[^1]);

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
