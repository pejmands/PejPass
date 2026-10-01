using System.Windows;
using System.Windows.Media.Animation;

namespace PejPass.Wpf.Services;

/// <summary>
/// Subtle per-window polish: fade-in on open.
/// </summary>
public static class UiPolish
{
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(160));
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
}
