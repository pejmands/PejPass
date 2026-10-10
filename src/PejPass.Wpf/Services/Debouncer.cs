using System.Windows.Threading;

namespace PejPass.Wpf.Services;

/// <summary>
/// Trailing-edge debounce on a dispatcher: the action runs once, after triggers have stopped
/// for the given delay. Must be created and used on the dispatcher's thread.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action _action;
    private bool _disposed;

    public Debouncer(
        TimeSpan delay,
        Action action,
        Dispatcher? dispatcher = null,
        DispatcherPriority priority = DispatcherPriority.Background)
    {
        ArgumentNullException.ThrowIfNull(action);

        _action = action;
        _timer = new DispatcherTimer(priority, dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = delay
        };
        _timer.Tick += OnTick;
    }

    /// <summary>(Re)starts the countdown.</summary>
    public void Trigger()
    {
        if (_disposed)
            return;

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Drops a pending invocation, if any.</summary>
    public void Cancel() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (!_disposed)
            _action();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
