using System.Windows;

namespace PejPass.Wpf.Services;

/// <summary>
/// Ensures only one PejPass process runs. A second launch signals the first
/// instance to bring its top visible window to the foreground, then exits.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\PejPass.SingleInstance.Mutex";
    private const string EventName = @"Local\PejPass.SingleInstance.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _listenCts;

    /// <summary>
    /// Try to become the sole instance.
    /// Returns false if another instance already owns the mutex (and was signaled).
    /// </summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);

        if (!createdNew)
        {
            // Ask the running instance to focus its window
            try
            {
                using var existing = EventWaitHandle.OpenExisting(EventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // First instance is shutting down or event not ready yet
            }
            catch (UnauthorizedAccessException)
            {
                // Rare; still refuse a second UI instance
            }

            try
            {
                _mutex.Dispose();
            }
            catch
            {
                // ignore
            }

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

                // Timeout so we can notice cancellation
                if (!_activateEvent.WaitOne(500))
                    continue;

                var app = System.Windows.Application.Current;
                if (app is null)
                    continue;

                app.Dispatcher.Invoke(BringToFront);
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

    /// <summary>Restore and activate the most appropriate open window.</summary>
    public static void BringToFront()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        Window? target = null;

        // Prefer Application.MainWindow when visible
        if (app.MainWindow is { IsVisible: true } main)
            target = main;

        // Otherwise last visible window (dialogs stack on top)
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

        // Brief Topmost toggle helps steal focus from another process on Windows
        target.Topmost = true;
        target.Topmost = false;
        target.Focus();
    }

    public static void Release()
    {
        try
        {
            _listenCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _activateEvent?.Dispose();
        }
        catch
        {
            // ignore
        }

        _activateEvent = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // not owned
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }

            try
            {
                _mutex.Dispose();
            }
            catch
            {
                // ignore
            }

            _mutex = null;
        }

        _listenCts?.Dispose();
        _listenCts = null;
    }
}
