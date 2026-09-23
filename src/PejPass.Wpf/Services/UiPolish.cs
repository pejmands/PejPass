using System.Windows;
using System.Windows.Media.Animation;

namespace PejPass.Wpf.Services;

/// <summary>
/// Subtle per-window polish: fade-in on open, soft pulse when theme changes.
/// Kept intentionally minimal — no heavy transitions.
/// </summary>
public static class UiPolish
{
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(160));
    private static readonly Duration ThemePulseHalf = new(TimeSpan.FromMilliseconds(90));
    private static bool _registered;

    /// <summary>Call once at app startup so every Window gets fade-in on Loaded.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        // Class handler fires for the window; ignore bubbled noise
        if (e.OriginalSource is not Window)
            return;

        FadeIn(window);
    }

    public static void FadeIn(Window window)
    {
        if (window is null) return;

        window.Opacity = 0;

        var anim = new DoubleAnimation(0, 1, FadeInDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        anim.Completed += (_, _) =>
        {
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;
        };

        window.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>
    /// Soft opacity pulse on all open windows after Dark/Light brushes swap,
    /// so the change feels intentional rather than an instant hard cut.
    /// </summary>
    public static void OnThemeApplied()
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        foreach (Window window in app.Windows)
        {
            if (!window.IsLoaded || !window.IsVisible)
                continue;

            Pulse(window);
        }
    }

    private static void Pulse(Window window)
    {
        var storyboard = new Storyboard();

        var down = new DoubleAnimation(1, 0.97, ThemePulseHalf)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(down, window);
        Storyboard.SetTargetProperty(down, new PropertyPath(UIElement.OpacityProperty));

        var up = new DoubleAnimation(0.97, 1, ThemePulseHalf)
        {
            BeginTime = ThemePulseHalf.TimeSpan,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(up, window);
        Storyboard.SetTargetProperty(up, new PropertyPath(UIElement.OpacityProperty));

        storyboard.Children.Add(down);
        storyboard.Children.Add(up);
        storyboard.Completed += (_, _) =>
        {
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;
        };
        storyboard.Begin();
    }
}
