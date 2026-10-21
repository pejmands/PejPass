using PejPass.Domain.Entities;
using PejPass.Infrastructure.Totp;
using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace PejPass.Wpf.Services;

internal sealed class BrowserStatusPipeService : IDisposable
{
    private readonly VaultSession _vaultSession;
    private readonly Application.Services.VaultService? _vaultService;

    internal BrowserStatusPipeService(VaultSession vaultSession)
        : this(vaultSession, null) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Application.Services.VaultService? vaultService)
    {
        _vaultSession = vaultSession;
        _vaultService = vaultService;
    }
    internal const string PipeName = "PejPass.BrowserStatus.v1";
    private const int ProtocolVersion = 1;
    private const int MaximumLineLength = 32768;
    private const int MaximumCredentialResults = 25;
    private const int MaximumPasswordLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverTask;

    public void Start()
    {
        if (_serverTask is not null) return;
        _serverTask = Task.Run(() => RunAsync(_shutdown.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                string? line;
                using (var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        line = await ReadBoundedLineAsync(reader, MaximumLineLength, requestTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (InvalidDataException)
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(Error("invalid_message"), JsonOptions))
                            .ConfigureAwait(false);
                        continue;
                    }
                }

                var response = IsSaveCredentialRequest(line)
                    ? await CreateSaveCredentialResponseAsync(line, cancellationToken).ConfigureAwait(false)
                    : IsUnlockVaultRequest(line)
                        ? await CreateUnlockVaultResponseAsync(line).ConfigureAwait(false)
                        : CreateResponse(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception) { }
        }
    }

    internal static async Task<string?> ReadBoundedLineAsync(
        TextReader reader,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        var buffer = new char[1];
        var builder = new System.Text.StringBuilder(Math.Min(maximumLength, 256));
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return builder.Length == 0 ? null : builder.ToString();

            var character = buffer[0];
            if (character == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                    builder.Length--;
                return builder.ToString();
            }

            if (builder.Length >= maximumLength)
            {
                if (character == '\r')
                {
                    var nextCount = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (nextCount > 0 && buffer[0] == '\n')
                        return builder.ToString();
                }

                throw new InvalidDataException("The browser pipe message exceeds the maximum length.");
            }

            builder.Append(character);
        }
    }

    private static bool IsSaveCredentialRequest(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return false;

        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "saveCredential";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsUnlockVaultRequest(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return false;

        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "unlockVault";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<StatusResponse> CreateUnlockVaultResponseAsync(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return Error("invalid_message");

        string masterPassword;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) ||
                protocolVersion != ProtocolVersion ||
                !TryGetBoundedString(root, "masterPassword", 4096, out masterPassword) ||
                string.IsNullOrEmpty(masterPassword))
                return Error("invalid_request");
        }
        catch (JsonException)
        {
            return Error("invalid_message");
        }

        if (_vaultSession.IsActive)
            return new StatusResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null);

        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
            return Error("app_unavailable");

        try
        {
            var unlockTask = await app.Dispatcher.InvokeAsync(() =>
            {
                var loginWindow = app.Windows.OfType<LoginWindow>().FirstOrDefault();
                if (loginWindow is null)
                {
                    loginWindow = App.Services.GetRequiredService<LoginWindow>();
                    App.PrepareCustomChrome(loginWindow);
                }

                app.MainWindow = loginWindow;
                if (!loginWindow.IsVisible)
                    loginWindow.Show();
                loginWindow.Activate();

                return loginWindow.DataContext is LoginViewModel viewModel
                    ? viewModel.UnlockFromBrowserAsync(masterPassword)
                    : Task.FromResult(false);
            }, System.Windows.Threading.DispatcherPriority.Send).Task.ConfigureAwait(false);

            var unlocked = await unlockTask.ConfigureAwait(false);
            return unlocked && _vaultSession.IsActive
                ? new StatusResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null)
                : Error("unlock_failed");
        }
        catch (Exception)
        {
            return Error("unlock_failed");
        }
    }

    private async Task<StatusResponse> CreateSaveCredentialResponseAsync(
        string? line,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return Error("invalid_message");

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) ||
                protocolVersion != ProtocolVersion)
                return Error("unsupported_request");

            if (!root.TryGetProperty("domain", out var domainElement) ||
                domainElement.ValueKind != JsonValueKind.String ||
                !TryNormalizeDomain(domainElement.GetString(), out var domain) ||
                !TryGetBoundedString(root, "url", 2048, out var url) ||
                !TryGetBoundedString(root, "title", 200, out var title) ||
                !TryGetBoundedString(root, "username", 256, out var username) ||
                !TryGetBoundedString(root, "password", MaximumPasswordLength, out var password) ||
                string.IsNullOrWhiteSpace(password) ||
                !TryGetWebUrl(url, domain))
                return Error("invalid_request");

            if (!_vaultSession.IsActive)
                return Error("vault_locked");
            if (_vaultService is null)
                return Error("app_unavailable");

            return await SaveCredentialAsync(domain, url, title, username, password, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Error("invalid_message");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error("save_unavailable");
        }
        catch (Exception)
        {
            return Error("save_unavailable");
        }
    }

    private async Task<StatusResponse> SaveCredentialAsync(
        string domain,
        string url,
        string title,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault ||
            string.IsNullOrWhiteSpace(_vaultSession.VaultPath))
            return Error("vault_locked");
        if (_vaultSession.HasExternalChanges())
            return Error("vault_changed");

        var generation = _vaultSession.Generation;
        var path = _vaultSession.VaultPath!;
        await _vaultService!.EnsureVaultWritableAsync(path, cancellationToken).ConfigureAwait(false);
        if (!_vaultSession.IsCurrent(generation, vault))
            return Error("vault_locked");

        var app = System.Windows.Application.Current;
        if (app is null)
            return Error("app_unavailable");

        var prepared = await app.Dispatcher.InvokeAsync(() =>
        {
            if (!_vaultSession.IsCurrent(generation, vault) || _vaultSession.HasExternalChanges())
                return new SavePreparation(null, null, Guid.Empty, null, "vault_changed");

            var matches = vault.Entries.Where(entry =>
                EntryMatchesDomain(entry, domain) &&
                string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count > 1)
                return new SavePreparation(null, null, Guid.Empty, null, "multiple_matching_accounts");

            var snapshot = vault.CreateSnapshot();
            string resultType;
            Guid savedId;

            if (matches.Count == 1)
            {
                var existing = matches[0];
                var updated = new VaultEntry
                {
                    Id = existing.Id,
                    Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title,
                    Username = username,
                    Password = password,
                    Url = url,
                    TotpSecret = existing.TotpSecret,
                    Notes = existing.Notes,
                    Tags = [.. existing.Tags],
                    CustomFields = [.. existing.CustomFields],
                    IsFavorite = existing.IsFavorite,
                    SortOrder = existing.SortOrder,
                    CreatedAt = existing.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                if (!vault.UpdateEntry(updated))
                {
                    if (string.Equals(existing.Password, password, StringComparison.Ordinal) &&
                        string.Equals(existing.Url, url, StringComparison.Ordinal))
                        return new SavePreparation(null, null, existing.Id, null, "unchanged");
                    return new SavePreparation(null, null, Guid.Empty, null, "save_unavailable");
                }

                resultType = "updated";
                savedId = existing.Id;
            }
            else
            {
                var entry = new VaultEntry
                {
                    Title = string.IsNullOrWhiteSpace(title) ? domain : title,
                    Username = username,
                    Password = password,
                    Url = url
                };
                vault.AddEntry(entry);
                resultType = "created";
                savedId = entry.Id;
            }

            return new SavePreparation(snapshot, resultType, savedId, vault, null);
        }, System.Windows.Threading.DispatcherPriority.Send, cancellationToken).Task.ConfigureAwait(false);

        if (prepared.Error is not null)
            return prepared.Error == "unchanged"
                ? new StatusResponse("credentialSaved", "PejPass", ProtocolVersion,
                    null, null, domain, null, prepared.Id.ToString("D"), null, null, null, "unchanged")
                : Error(prepared.Error);

        try
        {
            using (var saveLease = await _vaultSession.AcquireSaveLeaseAsync(
                generation, vault, cancellationToken).ConfigureAwait(false))
            {
                if (saveLease is null)
                    return Error("vault_locked");

                using var keyMaterial = _vaultSession.CopyKeyMaterial();
                await _vaultService.SaveVaultAsync(path, keyMaterial, vault, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await app.Dispatcher.InvokeAsync(() =>
            {
                if (_vaultSession.IsCurrent(generation, vault) && prepared.Snapshot is not null)
                    vault.RestoreSnapshot(prepared.Snapshot);
            }, System.Windows.Threading.DispatcherPriority.Send, CancellationToken.None).Task.ConfigureAwait(false);
            return Error("save_unavailable");
        }

        // Persistence has succeeded. A later UI refresh failure must not roll back
        // the in-memory vault while leaving the saved file updated.
        if (_vaultSession.IsCurrent(generation, vault))
        {
            try
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    if (!_vaultSession.IsCurrent(generation, vault))
                        return;

                    _vaultSession.RefreshDiskStamp();

                    if (app.MainWindow is MainWindow
                        { DataContext: ViewModels.MainViewModel viewModel })
                    {
                        viewModel.RefreshEntriesFromVault();
                    }
                }, System.Windows.Threading.DispatcherPriority.Send, CancellationToken.None).Task.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The credential is already persisted; keep the successful save result.
            }
        }

        NotifyBrowserActivity();
        return new StatusResponse("credentialSaved", "PejPass", ProtocolVersion,
            null, null, domain, null, prepared.Id.ToString("D"), null, null, null, prepared.Result);
    }

    private sealed record SavePreparation(
        Vault? Snapshot,
        string? Result,
        Guid Id,
        Vault? Vault,
        string? Error);

    internal StatusResponse CreateResponse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return Error("invalid_message");

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) ||
                protocolVersion != ProtocolVersion)
                return Error("unsupported_request");

            return type.GetString() switch
            {
                "lockVault" => CreateLockVaultResponse(),
                "getVaultStatus" => new StatusResponse("vaultStatus", "PejPass", ProtocolVersion,
                    _vaultSession.IsActive ? "unlocked" : "locked", null),
                "getCredentials" => CreateCredentialResponse(root),
                "fillCredential" => CreateFillResponse(root),
                "saveCredential" => CreateSaveCredentialResponse(root),
                _ => Error("unsupported_request")
            };
        }
        catch (JsonException) { return Error("invalid_message"); }
    }

    private StatusResponse CreateLockVaultResponse()
    {
        var app = System.Windows.Application.Current;
        if (!_vaultSession.IsActive)
            return new StatusResponse("vaultLocked", "PejPass", ProtocolVersion, "locked", null);
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
            return Error("app_unavailable");

        try
        {
            void LockOnUiThread()
            {
                var mainWindow = app.Windows.OfType<MainWindow>()
                    .FirstOrDefault(window => window.DataContext is MainViewModel);
                if (mainWindow?.DataContext is not MainViewModel viewModel)
                    throw new InvalidOperationException("The active vault cannot be locked through the main window.");

                viewModel.Lock(showLogin: true);
            }

            if (app.Dispatcher.CheckAccess())
                LockOnUiThread();
            else
                app.Dispatcher.Invoke(LockOnUiThread, System.Windows.Threading.DispatcherPriority.Send);

            return !_vaultSession.IsActive
                ? new StatusResponse("vaultLocked", "PejPass", ProtocolVersion, "locked", null)
                : Error("app_unavailable");
        }
        catch (Exception)
        {
            return Error("app_unavailable");
        }
    }

    private StatusResponse CreateCredentialResponse(JsonElement root)
    {
        if (!root.TryGetProperty("domain", out var domainElement) ||
            domainElement.ValueKind != JsonValueKind.String ||
            !TryNormalizeDomain(domainElement.GetString(), out var domain))
            return Error("invalid_domain");

        if (!_vaultSession.IsActive) return Error("vault_locked");

        try
        {
            var app = System.Windows.Application.Current;
            var credentials = app is not null && !app.Dispatcher.CheckAccess()
                ? app.Dispatcher.Invoke(() => FindCredentials(domain))
                : FindCredentials(domain);

            return new StatusResponse("credentialMatches", "PejPass", ProtocolVersion,
                null, null, domain, credentials);
        }
        catch (Exception) { return Error("lookup_unavailable"); }
    }

    private StatusResponse CreateFillResponse(JsonElement root)
    {
        if (!root.TryGetProperty("domain", out var domainElement) ||
            domainElement.ValueKind != JsonValueKind.String ||
            !TryNormalizeDomain(domainElement.GetString(), out var domain) ||
            !root.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(idElement.GetString(), out var id))
            return Error("invalid_request");

        if (!_vaultSession.IsActive) return Error("vault_locked");

        try
        {
            var app = System.Windows.Application.Current;
            var credential = app is not null && !app.Dispatcher.CheckAccess()
                ? app.Dispatcher.Invoke(() => FindCredentialForFill(id, domain))
                : FindCredentialForFill(id, domain);

            if (credential is not null)
                NotifyBrowserActivity();

            return credential is null
                ? Error("credential_not_found")
                : new StatusResponse("credentialForFill", "PejPass", ProtocolVersion,
                    null, null, domain, null, id.ToString("D"), credential.Username, credential.Password,
                    credential.TotpCode);
        }
        catch (Exception) { return Error("lookup_unavailable"); }
    }

    private StatusResponse CreateSaveCredentialResponse(JsonElement root)
    {
        if (!root.TryGetProperty("domain", out var domainElement) ||
            domainElement.ValueKind != JsonValueKind.String ||
            !TryNormalizeDomain(domainElement.GetString(), out var domain) ||
            !TryGetBoundedString(root, "url", 2048, out var url) ||
            !TryGetBoundedString(root, "title", 200, out _) ||
            !TryGetBoundedString(root, "username", 256, out _) ||
            !TryGetBoundedString(root, "password", MaximumPasswordLength, out var password) ||
            string.IsNullOrWhiteSpace(password) ||
            !TryGetWebUrl(url, domain))
            return Error("invalid_request");

        if (!_vaultSession.IsActive)
            return Error("vault_locked");
        return Error("save_unavailable");
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

    private static bool TryGetWebUrl(string value, string expectedDomain)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            TryNormalizeDomain(uri.IdnHost, out var host) &&
            host.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyBrowserActivity()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        void Notify()
        {
            if (app.MainWindow is MainWindow { DataContext: MainViewModel viewModel })
                viewModel.NotifyBrowserActivity();
        }

        try
        {
            if (app.Dispatcher.CheckAccess())
                Notify();
            else
                app.Dispatcher.Invoke(Notify, System.Windows.Threading.DispatcherPriority.Send);
        }
        catch (Exception)
        {
            // Browser activity must not fail a completed credential operation.
        }
    }

    private FillCredential? FindCredentialForFill(Guid id, string domain)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault) return null;
        var entry = vault.Entries.FirstOrDefault(entry => entry.Id == id);
        if (entry is null || !EntryMatchesDomain(entry, domain)) return null;

        var password = entry.Password ?? string.Empty;
        if (password.Length > MaximumPasswordLength) return null;

        var totpCode = string.Empty;
        if (!string.IsNullOrWhiteSpace(entry.TotpSecret))
            TotpHelper.TryGetCode(entry.TotpSecret, out totpCode, out _);

        return new FillCredential(entry.Username ?? string.Empty, password, totpCode);
    }

    private List<CredentialSummary> FindCredentials(string domain)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault) return [];

        return [.. vault.Entries
            .Where(entry => EntryMatchesDomain(entry, domain))
            .OrderByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCredentialResults)
            .Select(entry => new CredentialSummary(
                entry.Id.ToString("D"), Limit(entry.Title, 200),
                Limit(entry.Username, 256), Limit(entry.Url, 2048)))];
    }

    internal static bool EntryMatchesDomain(VaultEntry entry, string domain)
    {
        if (!TryGetEntryHost(entry.Url, out var entryHost)) return false;
        return domain.Equals(entryHost, StringComparison.OrdinalIgnoreCase) ||
               domain.EndsWith("." + entryHost, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryNormalizeDomain(string? value, out string domain)
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

    private static bool TryGetEntryHost(string? value, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            if (!Uri.TryCreate("https://" + value.Trim(), UriKind.Absolute, out uri)) return false;
        }

        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.Length > 0;
    }

    private static string Limit(string? value, int maximumLength)
    {
        value ??= string.Empty;
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static StatusResponse Error(string code) =>
        new("error", "PejPass", ProtocolVersion, null, code);

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _serverTask?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _shutdown.Dispose();
    }

    internal sealed record CredentialSummary(string Id, string Title, string Username, string Url);
    internal sealed record FillCredential(string Username, string Password, string TotpCode);
    internal sealed record StatusResponse(
        string Type, string App, int ProtocolVersion, string? Status, string? Code,
        string? Domain = null, IReadOnlyList<CredentialSummary>? Credentials = null,
        string? Id = null, string? Username = null, string? Password = null,
        string? TotpCode = null, string? Result = null);
}
