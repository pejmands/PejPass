using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace PejPass.BrowserHost;

internal static class Program
{
    private const int MaximumMessageLength = 1024 * 1024;
    private const int MaximumPipeLineLength = 65536;
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
            if (firstByte < 0) return 0;
            header[0] = (byte)firstByte;
            await input.ReadExactlyAsync(header.AsMemory(1)).ConfigureAwait(false);
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (messageLength <= 0 || messageLength > MaximumMessageLength) return 1;
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
        catch { return 1; }
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
                !versionElement.TryGetInt32(out var version) || version != ProtocolVersion)
                return Error("unsupported_request");

            return typeElement.GetString() switch
            {
                "ping" => new HostResponse("pong", "PejPass", ProtocolVersion, null, null),
                "getVaultStatus" => await GetVaultStatusAsync().ConfigureAwait(false),
                "getCredentials" => await GetCredentialsAsync(root).ConfigureAwait(false),
                _ => Error("unsupported_request")
            };
        }
        catch (JsonException) { return Error("invalid_message"); }
    }

    private static async Task<HostResponse> GetVaultStatusAsync()
    {
        var response = await SendPipeRequestAsync(new HostRequest("getVaultStatus", ProtocolVersion)).ConfigureAwait(false);
        if (response.Error is not null) return Error(response.Error);
        var root = response.Root;
        if (!HasString(root, "type", "vaultStatus") || !HasString(root, "app", "PejPass") ||
            !HasVersion(root) || !root.TryGetProperty("status", out var statusElement) ||
            statusElement.ValueKind != JsonValueKind.String)
            return Error("invalid_app_response");

        var status = statusElement.GetString();
        return status is "locked" or "unlocked"
            ? new HostResponse("vaultStatus", "PejPass", ProtocolVersion, status, null)
            : Error("invalid_app_response");
    }

    private static async Task<HostResponse> GetCredentialsAsync(JsonElement request)
    {
        if (!request.TryGetProperty("domain", out var domainElement) ||
            domainElement.ValueKind != JsonValueKind.String ||
            !TryNormalizeDomain(domainElement.GetString(), out var domain))
            return Error("invalid_domain");

        var response = await SendPipeRequestAsync(
            new HostRequest("getCredentials", ProtocolVersion, domain)).ConfigureAwait(false);
        if (response.Error is not null) return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "credentialMatches") || !HasString(root, "app", "PejPass") ||
            !HasVersion(root) || !HasString(root, "domain", domain) ||
            !root.TryGetProperty("credentials", out var credentialsElement) ||
            credentialsElement.ValueKind != JsonValueKind.Array || credentialsElement.GetArrayLength() > 25)
            return Error("invalid_app_response");

        var credentials = new List<CredentialSummary>();
        foreach (var item in credentialsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetBoundedString(item, "id", 64, out var id) || !Guid.TryParse(id, out _) ||
                !TryGetBoundedString(item, "title", 200, out var title) ||
                !TryGetBoundedString(item, "username", 256, out var username) ||
                !TryGetBoundedString(item, "url", 2048, out var url))
                return Error("invalid_app_response");

            credentials.Add(new CredentialSummary(id, title, username, url));
        }

        return new HostResponse("credentialMatches", "PejPass", ProtocolVersion,
            null, null, domain, credentials);
    }

    private static async Task<PipeResponse> SendPipeRequestAsync(HostRequest request)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await using var pipe = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(750, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumPipeLineLength)
                return new PipeResponse(default, "invalid_app_response");

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement.Clone();
            if (HasString(root, "type", "error"))
            {
                var code = root.TryGetProperty("code", out var codeElement) &&
                           codeElement.ValueKind == JsonValueKind.String ? codeElement.GetString() : null;
                return new PipeResponse(default, IsSafeErrorCode(code) ? code : "invalid_app_response");
            }
            return new PipeResponse(root, null);
        }
        catch (TimeoutException) { return new PipeResponse(default, "app_unavailable"); }
        catch (OperationCanceledException) { return new PipeResponse(default, "app_unavailable"); }
        catch (IOException) { return new PipeResponse(default, "app_unavailable"); }
        catch (JsonException) { return new PipeResponse(default, "invalid_app_response"); }
    }

    private static bool TryNormalizeDomain(string? value, out string domain)
    {
        domain = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 ||
            value.Any(char.IsWhiteSpace) || value.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0 ||
            !Uri.TryCreate("https://" + value, UriKind.Absolute, out var uri) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            Uri.CheckHostName(uri.IdnHost) == UriHostNameType.Unknown)
            return false;
        domain = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return domain.Length > 0;
    }

    private static bool TryGetBoundedString(JsonElement root, string name, int maximumLength, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { } candidate || candidate.Length > maximumLength)
            return false;
        value = candidate;
        return true;
    }

    private static bool IsSafeErrorCode(string? code) => code is
        "invalid_domain" or "vault_locked" or "lookup_unavailable" or "app_unavailable" or
        "invalid_app_response" or "unsupported_request" or "invalid_message";

    private static bool HasString(JsonElement root, string name, string expected) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool HasVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("protocolVersion", out var value) &&
        value.TryGetInt32(out var version) && version == ProtocolVersion;

    private static HostResponse Error(string code) => new("error", "PejPass", ProtocolVersion, null, code);
    private sealed record HostRequest(string Type, int ProtocolVersion, string? Domain = null);
    private sealed record CredentialSummary(string Id, string Title, string Username, string Url);
    private sealed record HostResponse(
        string Type, string App, int ProtocolVersion, string? Status, string? Code,
        string? Domain = null, IReadOnlyList<CredentialSummary>? Credentials = null);
    private readonly record struct PipeResponse(JsonElement Root, string? Error);
}
