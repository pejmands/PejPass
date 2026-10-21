using System.IO.Pipes;
using System.Text.Json;

namespace PejPass.Wpf.Services;

internal sealed class BrowserStatusPipeService : IDisposable
{
    internal const string PipeName = "PejPass.BrowserStatus.v1";
    private const int ProtocolVersion = 1;
    private readonly VaultSession _vaultSession;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverTask;

    public BrowserStatusPipeService(VaultSession vaultSession) =>
        _vaultSession = vaultSession;

    public void Start()
    {
        if (_serverTask is not null)
            return;

        _serverTask = Task.Run(() => RunAsync(_shutdown.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var response = CreateResponse(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A disconnected client must not stop the local status server.
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Keep the status endpoint isolated from the desktop application's UI.
            }
        }
    }

    private StatusResponse CreateResponse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > 4096)
            return new StatusResponse("error", "PejPass", ProtocolVersion, null, "invalid_message");

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "getVaultStatus" ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) ||
                protocolVersion != ProtocolVersion)
            {
                return new StatusResponse("error", "PejPass", ProtocolVersion, null, "unsupported_request");
            }

            return new StatusResponse(
                "vaultStatus",
                "PejPass",
                ProtocolVersion,
                _vaultSession.IsActive ? "unlocked" : "locked",
                null);
        }
        catch (JsonException)
        {
            return new StatusResponse("error", "PejPass", ProtocolVersion, null, "invalid_message");
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _serverTask?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _shutdown.Dispose();
    }

    private sealed record StatusResponse(
        string Type,
        string App,
        int ProtocolVersion,
        string? Status,
        string? Code);
}
