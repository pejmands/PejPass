using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private const string EventsPipeName = "PejPass.BrowserEvents.v1";
    private const int MaximumEventLineLength = 512;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim PipeRequestGate = new(1, 1);

    private static async Task<int> Main()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var shutdown = new CancellationTokenSource();
        var writeLock = new SemaphoreSlim(1, 1);
        var activeTasks = new ConcurrentDictionary<long, Task>();
        long nextTaskId = 0;
        var watcher = ListenForVaultEventsAsync(output, writeLock, shutdown.Token);

        try
        {
            while (true)
            {
                var message = await ReadMessageAsync(input, shutdown.Token).ConfigureAwait(false);
                if (message is null) break;

                var taskId = Interlocked.Increment(ref nextTaskId);
                var task = HandleAsync(message, output, writeLock);
                activeTasks[taskId] = task;
                _ = task.ContinueWith(
                    completed => activeTasks.TryRemove(taskId, out var ignored),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            shutdown.Cancel();
            await Task.WhenAll(activeTasks.Values.ToArray()).ConfigureAwait(false);
            return 0;
        }
        catch (InvalidDataException)
        {
            shutdown.Cancel();
            try { await Task.WhenAll(activeTasks.Values.ToArray()).ConfigureAwait(false); } catch { }
            return 1;
        }
        catch (IOException)
        {
            shutdown.Cancel();
            try { await Task.WhenAll(activeTasks.Values.ToArray()).ConfigureAwait(false); } catch { }
            return 0;
        }
        finally
        {
            shutdown.Cancel();
            try { await watcher.ConfigureAwait(false); } catch { }
            writeLock.Dispose();
        }
    }

    private static async Task<byte[]?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        var read = await input.ReadAtLeastAsync(
            header.AsMemory(), header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        if (read == 0) return null;
        if (read < header.Length) throw new InvalidDataException("Truncated native messaging header.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumMessageLength)
            throw new InvalidDataException("Invalid native messaging message length.");

        var body = new byte[length];
        await input.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    private static async Task HandleAsync(byte[] messageBytes, Stream output, SemaphoreSlim writeLock)
    {
        var requestId = ExtractRequestId(messageBytes);
        HostResponse response;
        try
        {
            response = await CreateResponseAsync(messageBytes).ConfigureAwait(false);
        }
        catch
        {
            response = Error("invalid_message");
        }

        try
        {
            await WriteAsync(output, writeLock, response with { RequestId = requestId }).ConfigureAwait(false);
        }
        catch
        {
            // The browser may have closed the native messaging port.
        }
    }

    private static async Task WriteAsync(Stream output, SemaphoreSlim writeLock, HostResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        if (payload.Length > MaximumMessageLength)
            payload = JsonSerializer.SerializeToUtf8Bytes(Error("invalid_app_response") with
            {
                RequestId = response.RequestId
            }, JsonOptions);

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await output.WriteAsync(header).ConfigureAwait(false);
            await output.WriteAsync(payload).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static string? ExtractRequestId(byte[] messageBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(messageBytes);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("requestId", out var id) &&
                   id.ValueKind == JsonValueKind.String &&
                   id.GetString() is { Length: > 0 and <= 64 } value
                ? value
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Listens for desktop vault-state events. Reconnection attempts are periodic only while
    /// the desktop event pipe is unavailable; no status polling is performed while connected.
    /// </summary>
    private static async Task ListenForVaultEventsAsync(
        Stream output, SemaphoreSlim writeLock, CancellationToken cancellationToken)
    {
        string? lastSent = null;
        var retryDelay = TimeSpan.FromSeconds(1);

        async Task PublishAsync(string status)
        {
            if (status == lastSent) return;
            lastSent = status;
            await WriteAsync(output, writeLock, new HostResponse(
                "vaultStateChanged", "PejPass", ProtocolVersion, status, null)).ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".", EventsPipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    TokenImpersonationLevel.Identification);

                await pipe.ConnectAsync(500, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(1);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                while (await ReadBoundedEventLineAsync(reader, MaximumEventLineLength, cancellationToken)
                    .ConfigureAwait(false) is { } line)
                {
                    var status = ParseEventStatus(line);
                    if (status is not null)
                        await PublishAsync(status).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException
                or UnauthorizedAccessException or InvalidDataException)
            {
                // Retry when the desktop is unavailable or an event connection fails.
            }

            try { await PublishAsync("unavailable").ConfigureAwait(false); }
            catch (IOException) { break; }

            try { await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            if (retryDelay < TimeSpan.FromSeconds(3))
                retryDelay += TimeSpan.FromSeconds(1);
        }
    }

    private static async Task<string?> ReadBoundedEventLineAsync(
        TextReader reader, int maximumLength, CancellationToken cancellationToken)
    {
        var buffer = new char[1];
        var builder = new System.Text.StringBuilder(Math.Min(maximumLength, 128));

        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return builder.Length == 0 ? null : builder.ToString();

            if (buffer[0] == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                    builder.Length--;
                return builder.ToString();
            }

            if (builder.Length >= maximumLength)
                throw new InvalidDataException("The event pipe line exceeds the maximum length.");

            builder.Append(buffer[0]);
        }
    }

    private static string? ParseEventStatus(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!HasString(root, "type", "vaultStateChanged") ||
                !HasString(root, "app", "PejPass") ||
                !HasVersion(root) ||
                !root.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String)
                return null;

            var status = statusElement.GetString();
            return status is "locked" or "unlocked" ? status : null;
        }
        catch (JsonException) { return null; }
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
                "lockVault" => await LockVaultAsync().ConfigureAwait(false),
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

    private static async Task<HostResponse> LockVaultAsync()
    {
        var response = await SendPipeRequestAsync(
            new HostRequest("lockVault", ProtocolVersion)).ConfigureAwait(false);
        if (response.Error is not null)
            return Error(response.Error);

        var root = response.Root;
        return HasString(root, "type", "vaultLocked") &&
               HasString(root, "app", "PejPass") &&
               HasVersion(root) &&
               HasString(root, "status", "locked")
            ? new HostResponse("vaultLocked", "PejPass", ProtocolVersion, "locked", null)
            : Error("invalid_app_response");
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

    private static async Task<PipeResponse> SendPipeRequestAsync(
        HostRequest request, CancellationToken cancellationToken = default)
    {
        await PipeRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Type == "unlockVault"
                ? TimeSpan.FromSeconds(45)
                : TimeSpan.FromSeconds(1));
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new PipeResponse(default, "app_unavailable"); }
        catch (IOException) { return new PipeResponse(default, "app_unavailable"); }
        catch (JsonException) { return new PipeResponse(default, "invalid_app_response"); }
        finally { PipeRequestGate.Release(); }
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
        string? TotpCode = null, string? Result = null, string? RequestId = null);
    private readonly record struct PipeResponse(JsonElement Root, string? Error);
}
