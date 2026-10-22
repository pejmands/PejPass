using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace PejPass.Wpf.Services;

internal sealed partial class BrowserStatusPipeService
{
    internal const string EventsPipeName = "PejPass.BrowserEvents.v1";
    private const int MaximumSubscribers = 4;

    private readonly object _eventsSync = new();
    private readonly List<EventSubscriber> _subscribers = [];
    private string _lastStatus = "locked";
    private Task? _eventsTask;

    private string CurrentStatus() => _vaultSession.IsActive ? "unlocked" : "locked";

    private static string BuildEventLine(string status) =>
        JsonSerializer.Serialize(
            new StatusResponse("vaultStateChanged", "PejPass", ProtocolVersion, status, null),
            JsonOptions);

    private void OnVaultStateChanged(object? sender, EventArgs e)
    {
        lock (_eventsSync)
            RefreshStatusLocked();
    }

    /// <summary>
    /// Publishes only actual lock-state changes. VaultSession raises StateChanged outside its
    /// internal lock, so reading IsActive while holding _eventsSync does not invert lock order.
    /// </summary>
    private void RefreshStatusLocked()
    {
        var status = CurrentStatus();
        if (status == _lastStatus)
            return;

        _lastStatus = status;
        var line = BuildEventLine(status);

        foreach (var subscriber in _subscribers)
            subscriber.Enqueue(line);
    }

    private async Task RunEventsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;

            try
            {
                pipe = new NamedPipeServerStream(
                    EventsPipeName,
                    PipeDirection.InOut,
                    MaximumSubscribers + 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var connected = pipe;
                pipe = null;
                _ = ServeSubscriberAsync(connected, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (pipe is not null)
                    await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ServeSubscriberAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using var owned = pipe;
        var subscriber = new EventSubscriber();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var registered = false;

        try
        {
            lock (_eventsSync)
            {
                if (_subscribers.Count >= MaximumSubscribers)
                    return;

                // The snapshot and registration are atomic relative to subsequent state changes.
                RefreshStatusLocked();
                subscriber.Enqueue(BuildEventLine(_lastStatus));
                _subscribers.Add(subscriber);
                registered = true;
            }

            _ = WatchForClientCloseAsync(pipe, linked);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 256, leaveOpen: true)
            {
                AutoFlush = true
            };

            await foreach (var line in subscriber.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
                await writer.WriteLineAsync(line.AsMemory(), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            if (registered)
            {
                lock (_eventsSync)
                    _subscribers.Remove(subscriber);
            }

            subscriber.Complete();
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Writes alone do not reliably detect a disconnected idle client; reading detects EOF.
    /// The event channel is one-way, so any client-to-server bytes are ignored.
    /// </summary>
    private static async Task WatchForClientCloseAsync(Stream pipe, CancellationTokenSource cts)
    {
        try
        {
            var buffer = new byte[1];
            while (await pipe.ReadAsync(buffer, cts.Token).ConfigureAwait(false) > 0)
            {
                // Ignore input; this pipe only carries desktop-to-host events.
            }
        }
        catch (Exception) { }

        try { cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    private sealed class EventSubscriber
    {
        private readonly Channel<string> _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        public ChannelReader<string> Reader => _channel.Reader;
        public void Enqueue(string line) => _channel.Writer.TryWrite(line);
        public void Complete() => _channel.Writer.TryComplete();
    }
}
