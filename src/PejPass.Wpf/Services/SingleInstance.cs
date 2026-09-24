using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PejPass.Wpf.Services;

/// <summary>
/// Ensures only one PejPass process runs. A second launch signals the first
/// instance to bring its window to the foreground (and optionally open a vault path).
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\PejPass.SingleInstance.Mutex";
    private const string EventName = @"Local\PejPass.SingleInstance.Activate";

    private const int SwRestore = 9;

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _listenCts;
    private static Thread? _listenThread;

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
                PendingVaultOpen.Write(vaultPathToForward!);

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

        PendingVaultOpen.ReadAndClear();

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _listenCts = new CancellationTokenSource();

        _listenThread = new Thread(() => ListenForActivation(_listenCts.Token))
        {
            IsBackground = true,
            Name = "PejPass.SingleInstance.Listen"
        };
        _listenThread.Start();
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

                var app = System.Windows.Application.Current;
                if (app is null)
                    continue;

                var path = PendingVaultOpen.ReadAndClear();

                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        BringToFront();
                        Activated?.Invoke(path);
                    }
                    catch
                    {
                    }
                }));
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    public static void BringToFront()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        var target = FindActivationTarget(app);
        if (target is null)
            return;

        RestoreAndFocus(target);
    }

    private static Window? FindActivationTarget(System.Windows.Application app)
    {
        if (app.MainWindow is { IsLoaded: true } main)
            return main;

        Window? fallback = null;
        foreach (Window w in app.Windows)
        {
            if (!w.IsLoaded)
                continue;

            if (w.IsVisible && w.WindowState != WindowState.Minimized)
                return w;

            fallback = w;
        }

        return fallback;
    }

    private static void RestoreAndFocus(Window target)
    {
        if (!target.IsVisible)
            target.Show();

        if (target.WindowState == WindowState.Minimized)
            target.WindowState = WindowState.Normal;

        try
        {
            var hwnd = new WindowInteropHelper(target).EnsureHandle();
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
            }
        }
        catch
        {
        }

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
        _listenThread = null;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
