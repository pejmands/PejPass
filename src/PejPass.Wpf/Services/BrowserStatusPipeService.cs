using PejPass.Domain.Entities;
using PejPass.Infrastructure.Totp;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace PejPass.Wpf.Services;

internal sealed class BrowserStatusPipeService : IDisposable
{
    private readonly VaultSession _vaultSession;
    private readonly PejPass.Application.Services.VaultService? _vaultService;

    internal BrowserStatusPipeService(VaultSession vaultSession)
        : this(vaultSession, null) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        PejPass.Application.Services.VaultService? vaultService)
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
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(CreateResponse(line), JsonOptions)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception) { }
        }
    }

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

        try
        {
            var app = System.Windows.Application.Current;
            var response = app is not null && !app.Dispatcher.CheckAccess()
                ? app.Dispatcher.Invoke(() => SaveCredential(domain, url, title, username, password))
                : SaveCredential(domain, url, title, username, password);
            return response;
        }
        catch (InvalidOperationException)
        {
            return Error("vault_changed");
        }
        catch (Exception)
        {
            return Error("save_unavailable");
        }
    }

    private StatusResponse SaveCredential(string domain, string url, string title, string username, string password)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault ||
            string.IsNullOrWhiteSpace(_vaultSession.VaultPath))
            return Error("vault_locked");

        if (_vaultSession.HasExternalChanges())
            return Error("vault_changed");

        var generation = _vaultSession.Generation;
        var path = _vaultSession.VaultPath!;
        _vaultService!.EnsureVaultWritableAsync(path).GetAwaiter().GetResult();
        if (!_vaultSession.IsCurrent(generation, vault))
            return Error("vault_locked");

        var snapshot = vault.CreateSnapshot();
        var matches = vault.Entries.Where(entry =>
            EntryMatchesDomain(entry, domain) &&
            string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase)).ToList();

        string resultType;
        Guid savedId;
        if (matches.Count > 1)
            return Error("multiple_matching_accounts");

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
                    return new StatusResponse("credentialSaved", "PejPass", ProtocolVersion,
                        null, null, domain, null, existing.Id.ToString("D"), null, null, null, "unchanged");
                return Error("save_unavailable");
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

        try
        {
            using var keyMaterial = _vaultSession.CopyKeyMaterial();
            _vaultService.SaveVaultAsync(path, keyMaterial, vault).GetAwaiter().GetResult();
            if (!_vaultSession.IsCurrent(generation, vault))
                return Error("vault_locked");
            _vaultSession.RefreshDiskStamp();
            return new StatusResponse("credentialSaved", "PejPass", ProtocolVersion,
                null, null, domain, null, savedId.ToString("D"), null, null, null, resultType);
        }
        catch
        {
            if (_vaultSession.IsCurrent(generation, vault))
                vault.RestoreSnapshot(snapshot);
            return Error("save_unavailable");
        }
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
