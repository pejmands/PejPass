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
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private string? _copiedText;

    public void CopyWithTimeout(string text, TimeSpan timeout)
    {
        CancellationTokenSource cts;

        lock (_sync)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            cts = new CancellationTokenSource();
            _cts = cts;
            _copiedText = text;
        }

        try
        {
            _clipboard.SetText(text);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _copiedText = null;
                    _cts = null;
                }
            }

            cts.Dispose();
            throw;
        }

        _ = ClearAfterAsync(timeout, text, cts);
    }

    public void ClearIfOwned()
    {
        string? copiedText;
        CancellationTokenSource? cts;

        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            copiedText = _copiedText;
            _copiedText = null;
            cts?.Cancel();
        }

        cts?.Dispose();

        try
        {
            if (copiedText is not null &&
                _clipboard.ContainsText() &&
                _clipboard.GetText() == copiedText)
            {
                _clipboard.Clear();
            }
        }
        catch
        {
            // Clipboard may be locked by another process or unavailable during shutdown.
        }
    }

    private async Task ClearAfterAsync(
        TimeSpan timeout,
        string copiedText,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(timeout, cts.Token).ConfigureAwait(false);

            _dispatcher.Invoke(() =>
            {
                lock (_sync)
                {
                    if (!ReferenceEquals(_cts, cts) ||
                        cts.IsCancellationRequested ||
                        !string.Equals(_copiedText, copiedText, StringComparison.Ordinal))
                    {
                        return;
                    }

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
                        // Clipboard may be locked by another process or unavailable during shutdown.
                    }
                    finally
                    {
                        _copiedText = null;
                        _cts = null;
                    }
                }
            });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Expected when a new copy replaces the current clipboard value.
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or TaskCanceledException)
        {
            // The dispatcher may be shutting down; do not let a background task fault.
            lock (_sync)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _copiedText = null;
                    _cts = null;
                }
            }
        }
        finally
        {
            cts.Dispose();
        }
    }
}
