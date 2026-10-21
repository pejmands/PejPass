using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace PejPass.BrowserHost;

internal static class Program
{
    private const int MaximumMessageLength = 1024 * 1024;
    private const int ProtocolVersion = 1;
    private const string PipeName = "PejPass.BrowserStatus.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main()
    {
        try
        {
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();

            var header = new byte[sizeof(int)];
            var firstByte = input.ReadByte();

            if (firstByte < 0)
                return 0;

            header[0] = (byte)firstByte;
            await input.ReadExactlyAsync(header.AsMemory(1)).ConfigureAwait(false);

            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(header);

            if (messageLength <= 0 || messageLength > MaximumMessageLength)
                return 1;

            var messageBytes = new byte[messageLength];
            await input.ReadExactlyAsync(messageBytes).ConfigureAwait(false);

            var response = await CreateResponseAsync(messageBytes).ConfigureAwait(false);
            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);

            BinaryPrimitives.WriteInt32LittleEndian(header, responseBytes.Length);
            await output.WriteAsync(header).ConfigureAwait(false);
            await output.WriteAsync(responseBytes).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static async Task<HostResponse> CreateResponseAsync(byte[] messageBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(messageBytes);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("protocolVersion", out var versionElement) ||
                !versionElement.TryGetInt32(out var version) ||
                version != ProtocolVersion)
            {
                return Error("unsupported_request");
            }

            return typeElement.GetString() switch
            {
                "ping" => new HostResponse("pong", "PejPass", ProtocolVersion, null, null),
                "getVaultStatus" => await GetVaultStatusAsync().ConfigureAwait(false),
                _ => Error("unsupported_request")
            };
        }
        catch (JsonException)
        {
            return Error("invalid_message");
        }
    }

    private static async Task<HostResponse> GetVaultStatusAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);

            await pipe.ConnectAsync(750, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new HostRequest("getVaultStatus", ProtocolVersion), JsonOptions)).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || line.Length > 4096)
                return Error("invalid_app_response");

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !HasString(root, "type", "vaultStatus") ||
                !HasString(root, "app", "PejPass") ||
                !HasVersion(root) ||
                !root.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String)
            {
                return Error("invalid_app_response");
            }

            var status = statusElement.GetString();
            if (status is not ("locked" or "unlocked"))
                return Error("invalid_app_response");

            return new HostResponse("vaultStatus", "PejPass", ProtocolVersion, status, null);
        }
        catch (TimeoutException)
        {
            return Error("app_unavailable");
        }
        catch (OperationCanceledException)
        {
            return Error("app_unavailable");
        }
        catch (IOException)
        {
            return Error("app_unavailable");
        }
        catch (JsonException)
        {
            return Error("invalid_app_response");
        }
    }

    private static bool HasString(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() == expected;

    private static bool HasVersion(JsonElement root) =>
        root.TryGetProperty("protocolVersion", out var value) &&
        value.TryGetInt32(out var version) &&
        version == ProtocolVersion;

    private static HostResponse Error(string code) =>
        new("error", "PejPass", ProtocolVersion, null, code);

    private sealed record HostRequest(string Type, int ProtocolVersion);

    private sealed record HostResponse(
        string Type,
        string App,
        int ProtocolVersion,
        string? Status,
        string? Code);
}
