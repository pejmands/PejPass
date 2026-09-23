using System.Windows;

namespace PejPass.Wpf.Services;

/// <summary>
/// Ensures only one PejPass process runs. A second launch signals the first
/// instance to bring its window to the foreground (and optionally open a vault path).
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\PejPass.SingleInstance.Mutex";
    private const string EventName = @"Local\PejPass.SingleInstance.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _listenCts;

    /// <summary>
    /// Raised on the UI thread when a second instance asked us to activate
    /// (optional vault path from double-click / command line).
    /// </summary>
    public static event Action<string?>? Activated;

    /// <summary>
    /// Try to become the sole instance.
    /// Returns false if another instance already owns the mutex (and was signaled).
    /// </summary>
    public static bool TryAcquire(string? vaultPathToForward = null)
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);

        if (!createdNew)
        {
            if (!string.IsNullOrWhiteSpace(vaultPathToForward))
                PendingVaultOpen.Write(vaultPathToForward);

            try
            {
                using var existing = EventWaitHandle.OpenExisting(EventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            try { _mutex.Dispose(); } catch { /* ignore */ }
            _mutex = null;
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _listenCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenForActivation(_listenCts.Token));
        return true;
    }

    private static void ListenForActivation(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_activateEvent is null)
                    break;

                if (!_activateEvent.WaitOne(500))
                    continue;

                var app = Application.Current;
                if (app is null)
                    continue;

                var path = PendingVaultOpen.ReadAndClear();
                app.Dispatcher.Invoke(() =>
                {
                    Activated?.Invoke(path);
                    BringToFront();
                });
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public static void BringToFront()
    {
        var app = Application.Current;
        if (app is null)
            return;

        Window? target = null;

        if (app.MainWindow is { IsVisible: true } main)
            target = main;

        if (target is null)
        {
            foreach (Window w in app.Windows)
            {
                if (w.IsVisible)
                    target = w;
            }
        }

        if (target is null)
            return;

        if (target.WindowState == WindowState.Minimized)
            target.WindowState = WindowState.Normal;

        target.Show();
        target.Activate();
        target.Topmost = true;
        target.Topmost = false;
        target.Focus();
    }

    public static void Release()
    {
        try { _listenCts?.Cancel(); } catch { /* ignore */ }

        try { _activateEvent?.Dispose(); } catch { /* ignore */ }
        _activateEvent = null;

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            catch (ObjectDisposedException) { }

            try { _mutex.Dispose(); } catch { /* ignore */ }
            _mutex = null;
        }

        _listenCts?.Dispose();
        _listenCts = null;
    }
}
