using System.Runtime.InteropServices;
using System.Windows;
using PejPass.Application.Interfaces;

namespace PejPass.Infrastructure.Clipboard;

/// <summary>
/// Copies text to clipboard and automatically clears it after a timeout.
/// Attempts to reduce exposure in Windows clipboard history.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private CancellationTokenSource? _cts;

    public void CopyWithTimeout(string text, TimeSpan timeout)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        System.Windows.Clipboard.SetText(text);

        // Fire-and-forget clear after timeout
        _ = ClearAfterAsync(timeout, _cts.Token);
    }

    public void Clear()
    {
        _cts?.Cancel();
        try
        {
            System.Windows.Clipboard.Clear();
        }
        catch
        {
            // Clipboard may be locked by another process; ignore.
        }
    }

    private async Task ClearAfterAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await Task.Delay(timeout, ct);
            if (!ct.IsCancellationRequested)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try { System.Windows.Clipboard.Clear(); }
                    catch { /* ignore */ }
                });
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when a new copy arrives.
        }
    }
}
