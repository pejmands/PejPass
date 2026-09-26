using System.Windows;
using System.Windows.Threading;

namespace PejPass.Wpf.Services;

public static class SnackbarService
{
    public static event EventHandler<SnackbarEventArgs>? Shown;

    public static void Show(
        string message,
        SnackbarKind kind = SnackbarKind.Success,
        TimeSpan? duration = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        void Raise()
        {
            Shown?.Invoke(null, new SnackbarEventArgs(
                message,
                kind,
                duration ?? TimeSpan.FromSeconds(2.5)));
        }

        if (dispatcher.CheckAccess())
            Raise();
        else
            dispatcher.BeginInvoke(Raise, DispatcherPriority.Normal);
    }
}

public sealed class SnackbarEventArgs(
    string message,
    SnackbarKind kind,
    TimeSpan duration) : EventArgs
{
    public string Message { get; } = message;
    public SnackbarKind Kind { get; } = kind;
    public TimeSpan Duration { get; } = duration;
}

public enum SnackbarKind
{
    Success,
    Info,
    Warning,
    Error
}
