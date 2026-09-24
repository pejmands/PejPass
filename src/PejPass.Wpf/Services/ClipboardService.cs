using PejPass.Application.Interfaces;

namespace PejPass.Wpf.Services;

/// <summary>
/// Copies text to clipboard and automatically clears it after a timeout.
/// </summary>
public sealed class ClipboardService(
    IClipboardProvider clipboard,
    IUiDispatcher dispatcher) : IClipboardService
{
    private readonly IClipboardProvider _clipboard = clipboard;
    private readonly IUiDispatcher _dispatcher = dispatcher;
    private CancellationTokenSource? _cts;

    public void CopyWithTimeout(string text, TimeSpan timeout)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _clipboard.SetText(text);

        _ = ClearAfterAsync(
            timeout,
            text,
            _cts.Token);
    }

    public void Clear()
    {
        _cts?.Cancel();

        try
        {
            _clipboard.Clear();
        }
        catch
        {
            // Clipboard may be locked by another process; ignore.
        }
    }

    private async Task ClearAfterAsync(
        TimeSpan timeout,
        string copiedText,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(timeout, ct);

            if (!ct.IsCancellationRequested)
            {
                _dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (_clipboard.ContainsText() &&
                            _clipboard.GetText() == copiedText)
                        {
                            _clipboard.Clear();
                        }
                    }
                    catch
                    {
                        // Clipboard may be locked by another process; ignore.
                    }
                });
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when a new copy arrives.
        }
    }
}
