using System.Windows.Threading;

namespace PejPass.Wpf.Services;

public sealed class SecretRevealTimer : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private Action? _onElapsed;

    public SecretRevealTimer()
    {
        _timer.Tick += OnTick;
    }

    public void Start(int seconds, Action onElapsed)
    {
        Stop();

        if (seconds <= 0)
            return;

        _onElapsed = onElapsed;
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _onElapsed = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var callback = _onElapsed;
        Stop();
        callback?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}
