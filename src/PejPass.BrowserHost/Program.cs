using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
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
                "launchApp" => LaunchDesktopApp(),
                "unlockVault" => await UnlockVaultAsync(root).ConfigureAwait(false),
                "getVaultStatus" => await GetVaultStatusAsync().ConfigureAwait(false),
                "getCredentials" => await GetCredentialsAsync(root).ConfigureAwait(false),
                "fillCredential" => await FillCredentialAsync(root).ConfigureAwait(false),
                "saveCredential" => await SaveCredentialAsync(root).ConfigureAwait(false),
                _ => Error("unsupported_request")
            };
        }
        catch (JsonException) { return Error("invalid_message"); }
    }

    private static HostResponse LaunchDesktopApp()
    {
        var appPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "PejPass.Wpf", "bin", "x64", "Release",
            "net10.0-windows10.0.19041.0", "PejPass.exe"));
        if (!File.Exists(appPath))
            return Error("app_unavailable");

        try
        {
            Process.Start(new ProcessStartInfo { FileName = appPath, UseShellExecute = true });
            return new HostResponse("appLaunched", "PejPass", ProtocolVersion, null, null);
        }
        catch
        {
            return Error("app_unavailable");
        }
    }

    private static async Task<HostResponse> UnlockVaultAsync(JsonElement request)
    {
        if (!TryGetBoundedString(request, "masterPassword", 4096, out var masterPassword) ||
            string.IsNullOrEmpty(masterPassword))
            return Error("invalid_request");

        var hostRequest = new HostRequest("unlockVault", ProtocolVersion, MasterPassword: masterPassword);
        var response = await SendPipeRequestAsync(hostRequest).ConfigureAwait(false);
        if (response.Error == "app_unavailable")
        {
            var launchResponse = LaunchDesktopApp();
            if (launchResponse.Type == "error")
                return launchResponse;

            for (var attempt = 0; attempt < 25; attempt++)
            {
                await Task.Delay(400).ConfigureAwait(false);
                response = await SendPipeRequestAsync(hostRequest).ConfigureAwait(false);
                if (response.Error != "app_unavailable")
                    break;
            }
        }

        if (response.Error is not null)
            return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "vaultUnlocked") ||
            !HasString(root, "app", "PejPass") ||
            !HasVersion(root) ||
            !HasString(root, "status", "unlocked"))
            return Error("invalid_app_response");

        return new HostResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null);
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
        if (!TryGetDomain(request, out var domain)) return Error("invalid_domain");

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

    private static async Task<HostResponse> FillCredentialAsync(JsonElement request)
    {
        if (!TryGetDomain(request, out var domain) ||
            !TryGetBoundedString(request, "id", 64, out var id) || !Guid.TryParse(id, out _))
            return Error("invalid_request");

        var response = await SendPipeRequestAsync(
            new HostRequest("fillCredential", ProtocolVersion, domain, id)).ConfigureAwait(false);
        if (response.Error is not null) return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "credentialForFill") || !HasString(root, "app", "PejPass") ||
            !HasVersion(root) || !HasString(root, "domain", domain) || !HasString(root, "id", id) ||
            !TryGetBoundedString(root, "username", 256, out var username) ||
            !TryGetBoundedString(root, "password", 4096, out var password) ||
            !TryGetOptionalBoundedString(root, "totpCode", 8, out var totpCode) ||
            (totpCode.Length > 0 && !totpCode.All(char.IsAsciiDigit)))
            return Error("invalid_app_response");

        return new HostResponse("credentialForFill", "PejPass", ProtocolVersion,
            null, null, domain, null, id, username, password, totpCode);
    }

    private static async Task<HostResponse> SaveCredentialAsync(JsonElement request)
    {
        if (!TryGetDomain(request, out var domain) ||
            !TryGetBoundedString(request, "url", 2048, out var url) ||
            !TryGetBoundedString(request, "title", 200, out var title) ||
            !TryGetBoundedString(request, "username", 256, out var username) ||
            !TryGetBoundedString(request, "password", 4096, out var password) ||
            string.IsNullOrWhiteSpace(password) || !TryGetWebUrl(url, domain))
            return Error("invalid_request");

        var response = await SendPipeRequestAsync(
            new HostRequest("saveCredential", ProtocolVersion, domain, null, url, title, username, password))
            .ConfigureAwait(false);
        if (response.Error is not null) return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "credentialSaved") || !HasString(root, "app", "PejPass") ||
            !HasVersion(root) || !HasString(root, "domain", domain) ||
            !TryGetBoundedString(root, "id", 64, out var id) || !Guid.TryParse(id, out _) ||
            !TryGetBoundedString(root, "result", 32, out var result) ||
            result is not ("created" or "updated" or "unchanged"))
            return Error("invalid_app_response");

        return new HostResponse("credentialSaved", "PejPass", ProtocolVersion,
            null, null, domain, null, id, null, null, null, result);
    }

    private static bool TryGetWebUrl(string value, string expectedDomain)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            TryNormalizeDomain(uri.IdnHost, out var host) &&
            host.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDomain(JsonElement request, out string domain)
    {
        domain = string.Empty;
        return request.TryGetProperty("domain", out var domainElement) &&
               domainElement.ValueKind == JsonValueKind.String &&
               TryNormalizeDomain(domainElement.GetString(), out domain);
    }

    private static async Task<PipeResponse> SendPipeRequestAsync(HostRequest request)
    {
        try
        {
            using var timeout = new CancellationTokenSource(
                request.Type == "unlockVault" ? TimeSpan.FromSeconds(45) : TimeSpan.FromSeconds(1));
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

    private static bool TryGetOptionalBoundedString(JsonElement root, string name, int maximumLength, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element))
            return true;
        if (element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { } candidate || candidate.Length > maximumLength)
            return false;
        value = candidate;
        return true;
    }

    private static bool IsSafeErrorCode(string? code) =>
        code is "invalid_domain" or "invalid_request" or "credential_not_found" or "vault_locked" or
            "lookup_unavailable" or "app_unavailable" or "invalid_app_response" or
            "unsupported_request" or "invalid_message" or "save_unavailable" or "vault_changed" or
            "multiple_matching_accounts" or "unlock_failed";

    private static bool HasString(JsonElement root, string name, string expected) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool HasVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("protocolVersion", out var value) &&
        value.TryGetInt32(out var version) && version == ProtocolVersion;

    private static HostResponse Error(string code) => new("error", "PejPass", ProtocolVersion, null, code);
    private sealed record HostRequest(
        string Type, int ProtocolVersion, string? Domain = null, string? Id = null,
        string? Url = null, string? Title = null, string? Username = null, string? Password = null,
        string? MasterPassword = null);
    private sealed record CredentialSummary(string Id, string Title, string Username, string Url);
    private sealed record HostResponse(
        string Type, string App, int ProtocolVersion, string? Status, string? Code,
        string? Domain = null, IReadOnlyList<CredentialSummary>? Credentials = null,
        string? Id = null, string? Username = null, string? Password = null,
        string? TotpCode = null, string? Result = null);
    private readonly record struct PipeResponse(JsonElement Root, string? Error);
}
