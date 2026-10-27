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

    private const string UpdateMutexName = @"Local\PejPass.Update.v1";

    private static bool IsUpdateInProgress()
    {
        try
        {
            using var updateMutex = Mutex.OpenExisting(UpdateMutexName);
            try
            {
                if (!updateMutex.WaitOne(0))
                    return true;

                updateMutex.ReleaseMutex();
                return false;
            }
            catch (AbandonedMutexException)
            {
                updateMutex.ReleaseMutex();
                return false;
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static FileStream? TryAcquireUpdateLock()
    {
        try
        {
            if (IsUpdateInProgress())
                return null;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                return null;

            var lockDirectory = Path.Combine(localAppData, "PejPass");
            Directory.CreateDirectory(lockDirectory);

            var lockPath = Path.Combine(lockDirectory, "BrowserHost.update.lock");
            if (!File.Exists(lockPath))
            {
                try
                {
                    using var createdLockFile = new FileStream(
                        lockPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read);
                }
                catch (IOException) when (File.Exists(lockPath))
                {
                }
            }

            var updateLock = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            if (IsUpdateInProgress())
            {
                updateLock.Dispose();
                return null;
            }

            return updateLock;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<bool> WaitForUpdateRequestAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (true)
        {
            if (IsUpdateInProgress())
                return true;

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                return false;
        }
    }

    private static async Task<int> Main()
    {
        using var updateLock = TryAcquireUpdateLock();
        if (updateLock is null)
            return 0;

        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var shutdown = new CancellationTokenSource();
        using var updateRequestCancellation = new CancellationTokenSource();
        var updateRequestTask = WaitForUpdateRequestAsync(updateRequestCancellation.Token);
        var writeLock = new SemaphoreSlim(1, 1);
        var activeTasks = new ConcurrentDictionary<long, Task>();
        long nextTaskId = 0;
        var watcher = ListenForVaultEventsAsync(output, writeLock, shutdown.Token);

        try
        {
            while (true)
            {
                var readTask = ReadMessageAsync(input, shutdown.Token);
                var completedTask = await Task.WhenAny(readTask, updateRequestTask).ConfigureAwait(false);
                if (completedTask == updateRequestTask && await updateRequestTask.ConfigureAwait(false))
                {
                    shutdown.Cancel();
                    break;
                }

                var message = await readTask.ConfigureAwait(false);
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
            await Task.WhenAll([.. activeTasks.Values]).ConfigureAwait(false);
            return 0;
        }
        catch (InvalidDataException)
        {
            shutdown.Cancel();
            try { await Task.WhenAll([.. activeTasks.Values]).ConfigureAwait(false); } catch { }
            return 1;
        }
        catch (IOException)
        {
            shutdown.Cancel();
            try { await Task.WhenAll([.. activeTasks.Values]).ConfigureAwait(false); } catch { }
            return 0;
        }
        finally
        {
            shutdown.Cancel();
            updateRequestCancellation.Cancel();
            try { await updateRequestTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
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
                "unlockWithHello" => await UnlockWithHelloAsync().ConfigureAwait(false),
                "lockVault" => await LockVaultAsync().ConfigureAwait(false),
                "getVaultStatus" => await GetVaultStatusAsync().ConfigureAwait(false),
                "getCredentials" => await GetCredentialsAsync(root).ConfigureAwait(false),
                "searchCredentials" => await SearchCredentialsAsync(root).ConfigureAwait(false),
                "fillCredential" => await FillCredentialAsync(root).ConfigureAwait(false),
                "saveCredential" => await SaveCredentialAsync(root).ConfigureAwait(false),
                "copyToClipboard" => await CopyToClipboardAsync(root).ConfigureAwait(false),
                "generatePassword" => await GeneratePasswordAsync(root).ConfigureAwait(false),
                _ => Error("unsupported_request")
            };
        }
        catch (JsonException) { return Error("invalid_message"); }
    }

    private static HostResponse LaunchDesktopApp()
    {
        var appPath = GetConfiguredDesktopAppPath();
        if (appPath is null || !File.Exists(appPath))
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

    private static string? GetConfiguredDesktopAppPath()
    {
        var hostDirectory = AppContext.BaseDirectory;
        var configPath = Path.Combine(hostDirectory, "pejpass-host-config.json");
        var manifestPath = Path.Combine(hostDirectory, "com.pejpass.browserhost.json");

        // Firefox requires a standards-compliant native messaging manifest, so its
        // desktop launch path is stored in a separate host configuration file.
        var configuredPath = TryReadDesktopAppPath(configPath);
        if (configuredPath is not null)
            return configuredPath;

        // Keep compatibility with the Chromium host manifest used by Edge.
        return TryReadDesktopAppPath(manifestPath);
    }

    private static string? TryReadDesktopAppPath(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetBoundedString(root, "desktopAppPath", 32767, out var appPath) ||
                string.IsNullOrWhiteSpace(appPath) ||
                !Path.IsPathFullyQualified(appPath))
                return null;

            var fullPath = Path.GetFullPath(appPath);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
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
            return response.Error == "unlock_rate_limited"
                ? new HostResponse("error", "PejPass", ProtocolVersion, null, response.Error,
                    RetryAfterSeconds: response.RetryAfterSeconds)
                : Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "vaultUnlocked") ||
            !HasString(root, "app", "PejPass") ||
            !HasVersion(root) ||
            !HasString(root, "status", "unlocked"))
            return Error("invalid_app_response");

        return new HostResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null);
    }

    private static async Task<HostResponse> UnlockWithHelloAsync()
    {
        var request = new HostRequest("unlockWithHello", ProtocolVersion);
        var response = await SendPipeRequestAsync(request).ConfigureAwait(false);
        if (response.Error == "app_unavailable")
        {
            var launchResponse = LaunchDesktopApp();
            if (launchResponse.Type == "error")
                return launchResponse;

            for (var attempt = 0; attempt < 25; attempt++)
            {
                await Task.Delay(400).ConfigureAwait(false);
                response = await SendPipeRequestAsync(request).ConfigureAwait(false);
                if (response.Error != "app_unavailable")
                    break;
            }
        }

        if (response.Error is not null)
            return Error(response.Error);

        var root = response.Root;
        return HasString(root, "type", "vaultUnlocked") &&
               HasString(root, "app", "PejPass") &&
               HasVersion(root) &&
               HasString(root, "status", "unlocked")
            ? new HostResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null)
            : Error("invalid_app_response");
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
        if (status is not ("locked" or "unlocked"))
            return Error("invalid_app_response");

        var helloReady = false;
        if (root.TryGetProperty("helloReady", out var helloReadyElement))
        {
            if (helloReadyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Error("invalid_app_response");
            helloReady = helloReadyElement.GetBoolean();
        }

        return new HostResponse("vaultStatus", "PejPass", ProtocolVersion, status, null,
            HelloReady: helloReady);
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

    private static async Task<HostResponse> SearchCredentialsAsync(JsonElement request)
    {
        if (!TryGetDomain(request, out var domain) ||
            !TryGetBoundedString(request, "query", 100, out var query) || query.Trim().Length < 2)
            return Error("invalid_request");

        var response = await SendPipeRequestAsync(
            new HostRequest("searchCredentials", ProtocolVersion, domain, Query: query.Trim())).ConfigureAwait(false);
        if (response.Error is not null) return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "credentialSearchResults") || !HasString(root, "app", "PejPass") ||
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

        return new HostResponse("credentialSearchResults", "PejPass", ProtocolVersion,
            null, null, domain, credentials);
    }

    private static async Task<HostResponse> FillCredentialAsync(JsonElement request)
    {
        if (!TryGetDomain(request, out var domain) ||
            !TryGetBoundedString(request, "id", 64, out var id) || !Guid.TryParse(id, out _))
            return Error("invalid_request");

        var allowDomainMismatch = request.TryGetProperty("allowDomainMismatch", out var mismatch) &&
            mismatch.ValueKind == JsonValueKind.True;
        var response = await SendPipeRequestAsync(
            new HostRequest("fillCredential", ProtocolVersion, domain, id,
                AllowDomainMismatch: allowDomainMismatch)).ConfigureAwait(false);
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
            !TryGetBoundedString(request, "mode", 16, out var mode) ||
            mode is not ("create" or "update") ||
            (mode == "update" &&
                (!TryGetBoundedString(request, "entryId", 64, out var targetId) ||
                 !Guid.TryParse(targetId, out _))) ||
            string.IsNullOrWhiteSpace(password) || !TryGetWebUrl(url, domain))
            return Error("invalid_request");

        url = SanitizeCredentialUrl(url);
        var entryId = mode == "update" ? request.GetProperty("entryId").GetString() : null;
        var response = await SendPipeRequestAsync(
            new HostRequest("saveCredential", ProtocolVersion, domain, null, url, title, username, password,
                Mode: mode, EntryId: entryId))
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

    private static async Task<HostResponse> GeneratePasswordAsync(JsonElement request)
    {
        if (!request.TryGetProperty("length", out var lengthElement) ||
            !lengthElement.TryGetInt32(out var length) || length is < 8 or > 64 ||
            !TryGetOptionalBoolean(request, "includeLowercase", true, out var includeLowercase) ||
            !TryGetOptionalBoolean(request, "includeUppercase", true, out var includeUppercase) ||
            !TryGetOptionalBoolean(request, "includeDigits", true, out var includeDigits) ||
            !TryGetOptionalBoolean(request, "includeSymbols", true, out var includeSymbols) ||
            !TryGetOptionalBoolean(request, "excludeAmbiguous", true, out var excludeAmbiguous))
            return Error("invalid_request");

        var response = await SendPipeRequestAsync(new HostRequest(
            "generatePassword", ProtocolVersion, Length: length,
            IncludeLowercase: includeLowercase, IncludeUppercase: includeUppercase,
            IncludeDigits: includeDigits, IncludeSymbols: includeSymbols,
            ExcludeAmbiguous: excludeAmbiguous)).ConfigureAwait(false);
        if (response.Error is not null)
            return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "generatedPassword") ||
            !HasString(root, "app", "PejPass") || !HasVersion(root) ||
            !TryGetBoundedString(root, "password", 64, out var password) ||
            password.Length != length ||
            !TryGetBoundedString(root, "strengthLabel", 32, out var strengthLabel) ||
            !root.TryGetProperty("strengthLevel", out var levelElement) ||
            !levelElement.TryGetInt32(out var strengthLevel) || strengthLevel is < 0 or > 5 ||
            !root.TryGetProperty("strengthProgress", out var progressElement) ||
            !progressElement.TryGetDouble(out var strengthProgress) ||
            !double.IsFinite(strengthProgress) || strengthProgress is < 0 or > 100)
            return Error("invalid_app_response");

        return new HostResponse("generatedPassword", "PejPass", ProtocolVersion, "unlocked", null,
            Password: password, StrengthLabel: strengthLabel,
            StrengthLevel: strengthLevel, StrengthProgress: strengthProgress);
    }

    private static bool TryGetOptionalBoolean(JsonElement root, string name, bool defaultValue, out bool value)
    {
        value = defaultValue;
        if (!root.TryGetProperty(name, out var element))
            return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = element.GetBoolean();
        return true;
    }

    private static async Task<HostResponse> CopyToClipboardAsync(JsonElement request)
    {
        if (!TryGetBoundedString(request, "password", 4096, out var password) ||
            string.IsNullOrEmpty(password))
            return Error("invalid_request");

        var response = await SendPipeRequestAsync(
            new HostRequest("copyToClipboard", ProtocolVersion, Password: password)).ConfigureAwait(false);
        if (response.Error is not null)
            return Error(response.Error);

        var root = response.Root;
        if (!HasString(root, "type", "clipboardCopied") ||
            !HasString(root, "app", "PejPass") ||
            !HasVersion(root) ||
            !root.TryGetProperty("clipboardClearSeconds", out var timeoutElement) ||
            !timeoutElement.TryGetInt32(out var timeoutSeconds) ||
            timeoutSeconds is < 5 or > 300)
            return Error("invalid_app_response");

        return new HostResponse(
            "clipboardCopied", "PejPass", ProtocolVersion, null, null,
            ClipboardClearSeconds: timeoutSeconds);
    }

    private static bool TryGetWebUrl(string value, string expectedDomain)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            TryNormalizeDomain(uri.IdnHost, out var host) &&
            host.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeCredentialUrl(string value)
    {
        var builder = new UriBuilder(new Uri(value, UriKind.Absolute))
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }

    private static bool TryGetDomain(JsonElement request, out string domain)
    {
        domain = string.Empty;
        return request.TryGetProperty("domain", out var domainElement) &&
               domainElement.ValueKind == JsonValueKind.String &&
               TryNormalizeDomain(domainElement.GetString(), out domain);
    }

    /// <summary>
    /// How long a request may wait for its turn on the single-instance desktop pipe before the
    /// host answers <c>app_busy</c>. Interactive requests (which can hold the pipe while the user
    /// confirms something in the desktop app) wait longer than status and lookup requests.
    /// </summary>
    private static TimeSpan GateWaitFor(string requestType) => requestType switch
    {
        "unlockVault" or "unlockWithHello" or "searchCredentials" or "fillCredential" => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(2)
    };

    private static async Task<PipeResponse> SendPipeRequestAsync(
        HostRequest request, CancellationToken cancellationToken = default)
    {
        // Do not queue behind a long-running request: the browser gave up on queued requests
        // long ago, and running them late would only delay the next ones. Fail fast instead.
        if (!await PipeRequestGate.WaitAsync(GateWaitFor(request.Type), cancellationToken).ConfigureAwait(false))
            return new PipeResponse(default, "app_busy");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Type switch
            {
                "unlockVault" or "unlockWithHello" or "searchCredentials" => TimeSpan.FromSeconds(45),
                "fillCredential" => TimeSpan.FromSeconds(60),
                "copyToClipboard" => TimeSpan.FromSeconds(3),
                _ => TimeSpan.FromSeconds(1)
            });
            await using var pipe = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
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
                var retryAfter = root.TryGetProperty("retryAfterSeconds", out var retryElement) &&
                                 retryElement.TryGetInt32(out var seconds) && seconds is >= 1 and <= 3600
                    ? seconds : 0;
                return new PipeResponse(default, IsSafeErrorCode(code) ? code : "invalid_app_response", retryAfter);
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
            "multiple_matching_accounts" or "unlock_failed" or "unlock_rate_limited" or "fill_denied" or
            "hello_failed" or "hello_cancelled" or "hello_unavailable" or "hello_not_ready" or
            "clipboard_unavailable" or "search_denied" or "app_busy";

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
        string? MasterPassword = null, string? Mode = null, string? EntryId = null,
        string? Query = null, bool AllowDomainMismatch = false, int? Length = null,
        bool IncludeLowercase = true, bool IncludeUppercase = true, bool IncludeDigits = true,
        bool IncludeSymbols = true, bool ExcludeAmbiguous = true);
    private sealed record CredentialSummary(string Id, string Title, string Username, string Url);
    private sealed record HostResponse(
        string Type, string App, int ProtocolVersion, string? Status, string? Code,
        string? Domain = null, IReadOnlyList<CredentialSummary>? Credentials = null,
        string? Id = null, string? Username = null, string? Password = null,
        string? TotpCode = null, string? Result = null, string? RequestId = null,
        int? RetryAfterSeconds = null, int? ClipboardClearSeconds = null,
        bool HelloReady = false, string? StrengthLabel = null, int? StrengthLevel = null,
        double? StrengthProgress = null);
    private readonly record struct PipeResponse(JsonElement Root, string? Error, int RetryAfterSeconds = 0);
}
