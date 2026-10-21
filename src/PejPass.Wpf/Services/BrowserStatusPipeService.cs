using PejPass.Domain.Entities;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace PejPass.Wpf.Services;

internal sealed class BrowserStatusPipeService(VaultSession vaultSession) : IDisposable
{
    internal const string PipeName = "PejPass.BrowserStatus.v1";
    private const int ProtocolVersion = 1;
    private const int MaximumLineLength = 32768;
    private const int MaximumCredentialResults = 25;
    private const int MaximumPasswordLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly VaultSession _vaultSession = vaultSession;
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
                    null, null, domain, null, id.ToString("D"), credential.Username, credential.Password);
        }
        catch (Exception) { return Error("lookup_unavailable"); }
    }

    private FillCredential? FindCredentialForFill(Guid id, string domain)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault) return null;
        var entry = vault.Entries.FirstOrDefault(entry => entry.Id == id);
        if (entry is null || !EntryMatchesDomain(entry, domain)) return null;

        var password = entry.Password ?? string.Empty;
        if (password.Length > MaximumPasswordLength) return null;
        return new FillCredential(entry.Username ?? string.Empty, password);
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
    internal sealed record FillCredential(string Username, string Password);
    internal sealed record StatusResponse(
        string Type, string App, int ProtocolVersion, string? Status, string? Code,
        string? Domain = null, IReadOnlyList<CredentialSummary>? Credentials = null,
        string? Id = null, string? Username = null, string? Password = null);
}
