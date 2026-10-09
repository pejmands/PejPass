using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace PejPass.Wpf.Services;

/// <summary>
/// Transfers a vault path from a secondary process to the primary process through a current-user-only named pipe.
/// </summary>
internal static class PendingVaultOpen
{
    private const int MaxMessageBytes = 128 * 1024;
    private static readonly string PipeName =
        $"PejPass.PendingVaultOpen.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static CancellationTokenSource? _serverCts;
    private static Thread? _serverThread;
    private static string? _pendingPath;

    public static void Start()
    {
        if (_serverCts is not null)
            return;

        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;
        _serverThread = new Thread(() =>
        {
            try
            {
                RunServerAsync(token).GetAwaiter().GetResult();
            }
            catch
            {
                // IPC failure must not crash the primary application.
            }
        })
        {
            IsBackground = true,
            Name = "PejPass.PendingVaultOpen.Pipe"
        };
        _serverThread.Start();
    }

    public static bool Write(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return false;

        byte[] payload;

        try
        {
            payload = StrictUtf8.GetBytes(vaultPath.Trim());
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (payload.Length is 0 or > MaxMessageBytes)
            return false;

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.None,
                    TokenImpersonationLevel.Identification);

                client.Connect(200);
                client.Write(length);
                client.Write(payload);
                client.Flush();

                var acknowledgement = new byte[1];
                client.ReadExactly(acknowledgement);
                return acknowledgement[0] == 1;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    public static string? ReadAndClear() =>
        Interlocked.Exchange(ref _pendingPath, null);

    public static void Stop()
    {
        var cts = Interlocked.Exchange(ref _serverCts, null);
        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch
        {
        }

        try
        {
            _serverThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _serverThread = null;
        cts.Dispose();
        Interlocked.Exchange(ref _pendingPath, null);
    }

    private static async Task RunServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(ct);

                using var connectionCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectionCts.CancelAfter(TimeSpan.FromSeconds(2));

                var lengthBytes = new byte[sizeof(int)];
                await server.ReadExactlyAsync(lengthBytes, connectionCts.Token);

                var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length is <= 0 or > MaxMessageBytes)
                    continue;

                var payload = new byte[length];
                await server.ReadExactlyAsync(payload, connectionCts.Token);

                string path;

                try
                {
                    path = StrictUtf8.GetString(payload).Trim();
                }
                catch (DecoderFallbackException)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    Interlocked.Exchange(ref _pendingPath, path);
                    await server.WriteAsync(new byte[] { 1 }, connectionCts.Token);
                }
                else
                {
                    await server.WriteAsync(new byte[] { 0 }, connectionCts.Token);
                }

                await server.FlushAsync(connectionCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Drop incomplete client messages and continue listening.
            }
            catch (IOException)
            {
                if (!ct.IsCancellationRequested)
                    await Task.Delay(50).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                if (!ct.IsCancellationRequested)
                    await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }
}
