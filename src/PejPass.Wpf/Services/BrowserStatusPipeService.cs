using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Domain.Security;
using PejPass.Infrastructure.Totp;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text.Json;

namespace PejPass.Wpf.Services;

internal sealed partial class BrowserStatusPipeService : IDisposable
{
    private readonly VaultSession _vaultSession;
    private readonly Application.Services.VaultService? _vaultService;
    private IClipboardService? _clipboardService;
    private readonly Func<string, string, string, bool>? _fillConfirmation;
    private readonly Func<string, Task<WindowsHelloVerificationResult>>? _fillHelloVerification;

    internal BrowserStatusPipeService(VaultSession vaultSession)
        : this(vaultSession, null) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Func<string, string, string, bool> fillConfirmation,
        Func<string, Task<WindowsHelloVerificationResult>> fillHelloVerification)
        : this(vaultSession, null, EventsPipeName, fillConfirmation, fillHelloVerification) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Application.Services.VaultService? vaultService)
        : this(vaultSession, vaultService, EventsPipeName) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Application.Services.VaultService? vaultService,
        IClipboardService clipboardService)
        : this(vaultSession, vaultService, EventsPipeName)
    {
        _clipboardService = clipboardService;
    }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Application.Services.VaultService? vaultService,
        string eventsPipeName)
        : this(vaultSession, vaultService, eventsPipeName, null, null) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Application.Services.VaultService? vaultService,
        string eventsPipeName,
        Func<string, string, string, bool>? fillConfirmation)
        : this(vaultSession, vaultService, eventsPipeName, fillConfirmation, null) { }

    internal BrowserStatusPipeService(
        VaultSession vaultSession,
        Application.Services.VaultService? vaultService,
        string eventsPipeName,
        Func<string, string, string, bool>? fillConfirmation,
        Func<string, Task<WindowsHelloVerificationResult>>? fillHelloVerification = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventsPipeName);
        _vaultSession = vaultSession;
        _vaultService = vaultService;
        _eventsPipeName = eventsPipeName;
        _fillConfirmation = fillConfirmation;
        _fillHelloVerification = fillHelloVerification;
        _vaultSession.StateChanged += OnVaultStateChanged;
    }

    internal UnlockRateLimiter UnlockLimiter { get; init; } = new(TimeProvider.System);
    internal const string PipeName = "PejPass.BrowserStatus.v1";
    private const int ProtocolVersion = 1;
    private const int MaximumLineLength = 32768;
    private const int MaximumCredentialResults = 25;
    private const int MaximumPasswordLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _vaultLockSync = new();
    private CancellationTokenSource _vaultLockCancellation = new();
    private readonly object _credentialIndexSync = new();
    private Vault? _credentialIndexVault;
    private DateTimeOffset _credentialIndexUpdatedAt;
    // DO NOT CHANGE; WRONG SUGGESTION.
    private Dictionary<string, List<VaultEntry>> _credentialIndexByRegistrableDomain =
        new(StringComparer.OrdinalIgnoreCase);
    private Task? _serverTask;

    public void Start()
    {
        if (_serverTask is not null) return;
        _serverTask = Task.Run(() => RunAsync(_shutdown.Token));
        StartEventsOnly();
    }

    /// <summary>Starts only the event pipe, allowing isolated tests without opening the request pipe.</summary>
    internal void StartEventsOnly() =>
        _eventsTask ??= Task.Run(() => RunEventsAsync(_shutdown.Token));

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

                var requestType = GetRequestType(line);
                var response = requestType switch
                {
                    "searchCredentials" => await CreateSearchCredentialResponseAsync(line).ConfigureAwait(false),
                    "generatePassword" => CreateGeneratedPasswordResponse(line),
                    "saveCredential" => await CreateSaveCredentialResponseAsync(line, cancellationToken).ConfigureAwait(false),
                    "unlockVault" => await CreateUnlockVaultResponseAsync(line).ConfigureAwait(false),
                    "unlockWithHello" => await CreateUnlockWithHelloResponseAsync(line).ConfigureAwait(false),
                    "fillCredential" => await CreateFillCredentialResponseAsync(line).ConfigureAwait(false),
                    _ => CreateResponse(line)
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { await BackoffAsync(cancellationToken).ConfigureAwait(false); }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception) { await BackoffAsync(cancellationToken).ConfigureAwait(false); }
        }
    }

    private static async Task BackoffAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    internal static async Task<string?> ReadBoundedLineAsync(
        TextReader reader,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        var buffer = new char[1024];
        var builder = new System.Text.StringBuilder(Math.Min(maximumLength, 1024));
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return builder.Length == 0 ? null : builder.ToString();

            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
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
                        var hasNextBufferedCharacter = index + 1 < count;
                        var nextCharacter = hasNextBufferedCharacter
                            ? buffer[index + 1]
                            : '\0';

                        if (!hasNextBufferedCharacter)
                        {
                            var nextCount = await reader.ReadAsync(
                                buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                            if (nextCount > 0)
                                nextCharacter = buffer[0];
                        }

                        if (nextCharacter == '\n')
                            return builder.ToString();
                    }

                    throw new InvalidDataException("The browser pipe message exceeds the maximum length.");
                }

                builder.Append(character);
            }
        }
    }

    private static string? GetRequestType(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String
                    ? type.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private StatusResponse CreateGeneratedPasswordResponse(string? line)
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
                protocolVersion != ProtocolVersion ||
                !root.TryGetProperty("length", out var lengthElement) ||
                !lengthElement.TryGetInt32(out var length) ||
                length is < PasswordGeneratorOptions.MinLength or > PasswordGeneratorOptions.MaxLength ||
                !TryGetOptionalBoolean(root, "includeLowercase", true, out var includeLowercase) ||
                !TryGetOptionalBoolean(root, "includeUppercase", true, out var includeUppercase) ||
                !TryGetOptionalBoolean(root, "includeDigits", true, out var includeDigits) ||
                !TryGetOptionalBoolean(root, "includeSymbols", true, out var includeSymbols) ||
                !TryGetOptionalBoolean(root, "excludeAmbiguous", true, out var excludeAmbiguous))
                return Error("invalid_request");

            var password = PasswordGenerator.Generate(new PasswordGeneratorOptions
            {
                Length = length,
                IncludeLowercase = includeLowercase,
                IncludeUppercase = includeUppercase,
                IncludeDigits = includeDigits,
                IncludeSymbols = includeSymbols,
                ExcludeAmbiguous = excludeAmbiguous
            });
            var level = PasswordStrength.Evaluate(password);
            return new StatusResponse("generatedPassword", "PejPass", ProtocolVersion, "unlocked", null,
                Password: password,
                StrengthLabel: PasswordStrength.GetLabel(level),
                StrengthLevel: (int)level,
                StrengthProgress: PasswordStrength.GetProgress(level));
        }
        catch (JsonException)
        {
            return Error("invalid_message");
        }
        catch (InvalidOperationException)
        {
            return Error("invalid_request");
        }
        catch (Exception)
        {
            return Error("generation_unavailable");
        }
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

    internal async Task<StatusResponse> CreateUnlockVaultResponseAsync(string? line)
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

        var gate = UnlockLimiter.TryBegin(out var retryAfter);
        if (gate != UnlockGate.Allowed)
            return RateLimited(retryAfter);

        var outcome = UnlockOutcome.Aborted;
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                return Error("app_unavailable");

            var unlockTask = await app.Dispatcher.InvokeAsync(() =>
            {
                var loginWindow = app.Windows.OfType<LoginWindow>().FirstOrDefault();
                if (loginWindow is null)
                {
                    loginWindow = App.Services.GetRequiredService<LoginWindow>();
                    App.PrepareCustomChrome(loginWindow);
                }

                return loginWindow.UnlockFromBrowserAsync(masterPassword);
            }, System.Windows.Threading.DispatcherPriority.Send).Task.ConfigureAwait(false);

            var unlocked = await unlockTask.ConfigureAwait(false);
            if (unlocked && _vaultSession.IsActive)
            {
                outcome = UnlockOutcome.Success;
                return new StatusResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null);
            }

            outcome = UnlockOutcome.Failure;
            return Error("unlock_failed");
        }
        catch (Exception)
        {
            outcome = UnlockOutcome.Failure;
            return Error("unlock_failed");
        }
        finally
        {
            UnlockLimiter.Complete(outcome);
        }
    }

    internal async Task<StatusResponse> CreateUnlockWithHelloResponseAsync(string? line)
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
        }
        catch (JsonException)
        {
            return Error("invalid_message");
        }

        if (_vaultSession.IsActive)
            return new StatusResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null);

        if (!IsHelloReady())
            return Error("hello_not_ready");

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

                return loginWindow.UnlockWithHelloFromBrowserAsync();
            }, System.Windows.Threading.DispatcherPriority.Send).Task.ConfigureAwait(false);

            var unlocked = await unlockTask.ConfigureAwait(false);
            return unlocked && _vaultSession.IsActive
                ? new StatusResponse("vaultUnlocked", "PejPass", ProtocolVersion, "unlocked", null)
                : Error("hello_failed");
        }
        catch (Exception)
        {
            return Error("hello_failed");
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
                !TryGetBoundedString(root, "mode", 16, out var mode) ||
                mode is not ("create" or "update") ||
                (mode == "update" &&
                    (!TryGetBoundedString(root, "entryId", 64, out var requestedEntryId) ||
                     !Guid.TryParse(requestedEntryId, out _))) ||
                !TryGetWebUrl(url, domain))
                return Error("invalid_request");

            url = SanitizeCredentialUrl(url);

            if (!_vaultSession.IsActive)
                return Error("vault_locked");
            if (_vaultService is null)
                return Error("app_unavailable");

            var entryId = mode == "update" ? root.GetProperty("entryId").GetString() : null;
            return await SaveCredentialAsync(domain, url, title, username, password, mode, entryId, cancellationToken)
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
        string mode,
        string? entryId,
        CancellationToken cancellationToken)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault ||
            string.IsNullOrWhiteSpace(_vaultSession.VaultPath))
            return Error("vault_locked");
        var credentialOrigin = GetCredentialOrigin(url);
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

        using var saveLease = await _vaultSession.AcquireSaveLeaseAsync(
            generation, vault, cancellationToken).ConfigureAwait(false);
        if (saveLease is null)
            return Error("vault_locked");

        var prepared = await app.Dispatcher.InvokeAsync(() =>
        {
            if (!_vaultSession.IsCurrent(generation, vault) || _vaultSession.HasExternalChanges())
                return new SavePreparation(null, null, Guid.Empty, null, "vault_changed");

            VaultEntry? existing = null;
            if (mode == "update")
            {
                if (!Guid.TryParse(entryId, out var targetId))
                    return new SavePreparation(null, null, Guid.Empty, null, "invalid_request");

                existing = vault.Entries.FirstOrDefault(entry =>
                    entry.Id == targetId && EntryMatchesDomain(entry, domain));
                if (existing is null)
                    return new SavePreparation(null, null, Guid.Empty, null, "credential_not_found");
            }

            var snapshot = vault.CreateSnapshot();
            string resultType;
            Guid savedId;

            if (existing is not null)
            {
                var updated = new VaultEntry
                {
                    Id = existing.Id,
                    Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title,
                    Username = username,
                    Password = password,
                    Url = string.IsNullOrWhiteSpace(existing.Url) ? credentialOrigin : existing.Url,
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
                        string.Equals(existing.Url, updated.Url, StringComparison.Ordinal))
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
                    Url = credentialOrigin
                };
                vault.AddEntry(entry);
                resultType = "created";
                savedId = entry.Id;
            }

            var saveSnapshot = vault.CreateSnapshot();
            return new SavePreparation(snapshot, resultType, savedId, vault, null, saveSnapshot);
        }, System.Windows.Threading.DispatcherPriority.Send, cancellationToken).Task.ConfigureAwait(false);

        if (prepared.Error is not null)
            return prepared.Error == "unchanged"
                ? new StatusResponse("credentialSaved", "PejPass", ProtocolVersion,
                    null, null, domain, null, prepared.Id.ToString("D"), null, null, null, "unchanged")
                : Error(prepared.Error);

        try
        {
            using var keyMaterial = _vaultSession.CopyKeyMaterial();
            await _vaultService.SaveVaultAsync(
                path, keyMaterial, prepared.SaveSnapshot!, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            saveLease.Dispose();
            await app.Dispatcher.InvokeAsync(() =>
            {
                if (_vaultSession.IsCurrent(generation, vault) && prepared.Snapshot is not null)
                {
                    vault.RestoreSnapshot(prepared.Snapshot);
                    if (app.MainWindow is MainWindow
                        { DataContext: MainViewModel viewModel })
                    {
                        viewModel.RefreshEntriesFromVault();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Send, CancellationToken.None).Task.ConfigureAwait(false);
            return Error("save_unavailable");
        }

        // Release the save gate before dispatching UI work; session replacement can wait on this gate.
        saveLease.Dispose();

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
                        { DataContext: MainViewModel viewModel })
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
        string? Error,
        Vault? SaveSnapshot = null);

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
                    _vaultSession.IsActive ? "unlocked" : "locked", null, HelloReady: IsHelloReady()),
                "getCredentials" => CreateCredentialResponse(root),
                "searchCredentials" => Error("async_required"),
                "fillCredential" => Error("async_required"),
                // Save requests are handled asynchronously by RunAsync.
                // Keep this synchronous fallback for CreateResponse tests only.
                "saveCredential" => CreateSaveCredentialResponse(root),
                "copyToClipboard" => CreateCopyToClipboardResponse(root),
                _ => Error("unsupported_request")
            };
        }
        catch (JsonException) { return Error("invalid_message"); }
    }

    private StatusResponse CreateCopyToClipboardResponse(JsonElement root)
    {
        if (!TryGetBoundedString(root, "password", MaximumPasswordLength, out var password) ||
            string.IsNullOrEmpty(password))
            return Error("invalid_request");
        if (_clipboardService is null)
            return Error("app_unavailable");

        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
            return Error("app_unavailable");

        try
        {
            var settings = App.Services.GetRequiredService<AppSettings>();
            var timeoutSeconds = Math.Clamp(settings.ClipboardClearSeconds, 5, 300);
            app.Dispatcher.Invoke(
                () => _clipboardService.CopyWithTimeout(password, TimeSpan.FromSeconds(timeoutSeconds)),
                System.Windows.Threading.DispatcherPriority.Send);
            return new StatusResponse(
                "clipboardCopied", "PejPass", ProtocolVersion, null, null,
                ClipboardClearSeconds: timeoutSeconds);
        }
        catch (Exception)
        {
            return Error("clipboard_unavailable");
        }
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
                // A browser unlock can intentionally leave MainWindow hidden. Such a window
                // may not be present in Application.Windows until it has been shown, so
                // prefer Application.MainWindow and use the window collection as a fallback.
                var mainWindow = app.MainWindow as MainWindow;
                var viewModel = mainWindow?.DataContext as MainViewModel;

                if (viewModel is null)
                {
                    mainWindow = app.Windows.OfType<MainWindow>()
                        .FirstOrDefault(window => window.DataContext is MainViewModel);
                    viewModel = mainWindow?.DataContext as MainViewModel;
                }

                if (viewModel is null)
                    throw new InvalidOperationException("The active vault cannot be locked through the main window.");

                viewModel.Lock(showLogin: false);
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

    internal async Task<StatusResponse> CreateSearchCredentialResponseAsync(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return Error("invalid_message");

        string domain;
        string query;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) || protocolVersion != ProtocolVersion ||
                !root.TryGetProperty("domain", out var domainElement) ||
                domainElement.ValueKind != JsonValueKind.String ||
                !TryNormalizeDomain(domainElement.GetString(), out domain) ||
                !TryGetBoundedString(root, "query", 100, out query) ||
                query.Trim().Length < 2)
                return Error("invalid_request");
            query = query.Trim();
        }
        catch (JsonException) { return Error("invalid_message"); }

        if (!_vaultSession.IsActive) return Error("vault_locked");
        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
            return Error("app_unavailable");

        try
        {
            var requireConfirmation = true;
            try
            {
                requireConfirmation = App.Services.GetService<AppSettings>()?.BrowserAutofillConfirmationEnabled ?? true;
            }
            catch
            {
                // If settings are unavailable, keep confirmation enabled.
            }

            if (requireConfirmation)
            {
                var approvalTask = await app.Dispatcher.InvokeAsync(() => DialogService.ConfirmAsync(
                    $"Search saved accounts across your vault for this text?\n\nSearch: {Limit(query, 100)}\nCurrent site: {domain}\n\nAccount titles, usernames, and website addresses may be shown in the browser extension.",
                    "Confirm Credential Search",
                    yesText: "Search",
                    noText: "Cancel",
                    bringToFront: true), System.Windows.Threading.DispatcherPriority.Send).Task.ConfigureAwait(false);
                var approved = await approvalTask.ConfigureAwait(false);
                if (!approved) return Error("search_denied");
            }

            if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault)
                return Error("vault_locked");

            var comparison = query.Trim();
            var matches = vault.Entries
                .Where(entry =>
                    (entry.Title?.Contains(comparison, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (entry.Username?.Contains(comparison, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (entry.Url?.Contains(comparison, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderByDescending(entry => entry.IsFavorite)
                .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumCredentialResults)
                .Select(entry => new CredentialSummary(
                    entry.Id.ToString("D"), Limit(entry.Title, 200),
                    Limit(entry.Username, 256), Limit(entry.Url, 2048)))
                .ToArray();

            return new StatusResponse("credentialSearchResults", "PejPass", ProtocolVersion,
                null, null, domain, matches);
        }
        catch (Exception) { return Error("lookup_unavailable"); }
    }

    internal async Task<StatusResponse> CreateFillCredentialResponseAsync(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
            return Error("invalid_message");

        Guid id;
        string domain;
        bool allowDomainMismatch;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "fillCredential" ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) ||
                protocolVersion != ProtocolVersion ||
                !root.TryGetProperty("domain", out var domainElement) ||
                domainElement.ValueKind != JsonValueKind.String ||
                !TryNormalizeDomain(domainElement.GetString(), out domain) ||
                !root.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(idElement.GetString(), out id))
                return Error("invalid_request");
            allowDomainMismatch = root.TryGetProperty("allowDomainMismatch", out var mismatch) &&
                mismatch.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return Error("invalid_message");
        }

        if (!_vaultSession.IsActive)
            return Error("vault_locked");

        var vaultGeneration = _vaultSession.Generation;
        CancellationToken vaultLockToken;
        lock (_vaultLockSync)
            vaultLockToken = _vaultLockCancellation.Token;

        if (!_vaultSession.IsActive || _vaultSession.Generation != vaultGeneration)
            return Error("vault_locked");

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _shutdown.Token, vaultLockToken);

        try
        {
            (FillCredential? Credential, string? ErrorCode) result;
            var app = System.Windows.Application.Current;
            if (_fillConfirmation is not null && _fillHelloVerification is not null)
            {
                result = await FindAndConfirmCredentialForFillAsync(
                    id, domain, allowDomainMismatch, requestCancellation.Token).ConfigureAwait(false);
            }
            else
            {
                if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                    return Error("app_unavailable");


                Task<(FillCredential? Credential, string? ErrorCode)> operation =
                    app.Dispatcher.CheckAccess()
                        ? FindAndConfirmCredentialForFillAsync(id, domain, allowDomainMismatch, requestCancellation.Token)
                        : app.Dispatcher.InvokeAsync(
                            () => FindAndConfirmCredentialForFillAsync(id, domain, allowDomainMismatch, requestCancellation.Token),
                            System.Windows.Threading.DispatcherPriority.Send)
                            .Task.Unwrap();

                result = await operation.ConfigureAwait(false);
            }

            if (requestCancellation.IsCancellationRequested ||
                !_vaultSession.IsActive ||
                _vaultSession.Generation != vaultGeneration)
                return Error("vault_locked");

            if (result.ErrorCode is not null)
                return Error(result.ErrorCode);

            var credential = result.Credential;
            if (credential is null)
                return Error("credential_not_found");

            NotifyBrowserActivity();
            return new StatusResponse("credentialForFill", "PejPass", ProtocolVersion,
                null, null, domain, null, id.ToString("D"), credential.Username, credential.Password,
                credential.TotpCode);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            return Error(_vaultSession.IsActive ? "fill_denied" : "vault_locked");
        }
        catch (Exception) { return Error("lookup_unavailable"); }
    }

    private async Task<(FillCredential? Credential, string? ErrorCode)> FindAndConfirmCredentialForFillAsync(
        Guid id, string domain, bool allowDomainMismatch, CancellationToken cancellationToken)
    {
        var entry = allowDomainMismatch ? FindEntryById(id) : FindEntryForFill(id, domain);
        if (entry is null)
            return (null, "credential_not_found");

        var requireDesktopConfirmation = true;
        var requireWindowsHello = true;
        try
        {
            var settings = App.Services.GetService<AppSettings>();
            requireDesktopConfirmation = settings?.BrowserAutofillConfirmationEnabled ?? true;
            requireWindowsHello = settings?.BrowserAutofillWindowsHelloEnabled ?? true;
        }
        catch
        {
            // If settings are unavailable, keep both confirmation steps enabled.
        }

        var isCrossDomainSelection = RequiresCrossDomainConfirmation(entry, domain, allowDomainMismatch);
        var desktopConfirmationCompleted = false;

        // Accounts from another website always require explicit confirmation.
        if (requireDesktopConfirmation || isCrossDomainSelection)
        {
            var confirmationError = await ConfirmBrowserAutofillAsync(
                entry, domain, isCrossDomainSelection, cancellationToken);
            if (confirmationError is not null)
                return (null, confirmationError);

            desktopConfirmationCompleted = true;
        }

        if (cancellationToken.IsCancellationRequested || !_vaultSession.IsActive)
            return (null, "vault_locked");

        if (requireWindowsHello)
        {
            var helloResult = await VerifyBrowserAutofillWithWindowsHelloAsync(
                domain, Limit(entry.Title, 200), Limit(entry.Username, 256));
            if (helloResult == WindowsHelloVerificationResult.Unavailable)
            {
                // If Hello is unavailable, fall back to explicit desktop approval.
                if (!desktopConfirmationCompleted)
                {
                    var confirmationError = await ConfirmBrowserAutofillAsync(
                        entry, domain, isCrossDomainSelection, cancellationToken);
                    if (confirmationError is not null)
                        return (null, confirmationError);
                }
            }
            else if (helloResult != WindowsHelloVerificationResult.Verified)
            {
                var code = helloResult switch
                {
                    WindowsHelloVerificationResult.AuthenticationFailed => "hello_failed",
                    WindowsHelloVerificationResult.Cancelled => "hello_cancelled",
                    _ => "hello_unavailable"
                };
                return (null, code);
            }
        }

        // Re-read the credential after approval so no stale or expired TOTP code is returned.
        var credential = await FindCredentialForFillAsync(id, domain, allowDomainMismatch);
        return cancellationToken.IsCancellationRequested || !_vaultSession.IsActive
            ? (null, "vault_locked")
            : (credential, null);
    }

    private async Task<string?> ConfirmBrowserAutofillAsync(
        VaultEntry entry, string domain, bool isCrossDomainSelection, CancellationToken cancellationToken)
    {
        bool approved;
        if (_fillConfirmation is not null)
        {
            approved = _fillConfirmation(domain, Limit(entry.Title, 200), Limit(entry.Username, 256));
        }
        else
        {
            var domainNotice = isCrossDomainSelection
                ? $"\nSaved website: {Limit(entry.Url, 2048)}\nThis account belongs to another website. Verify that it is the correct account before continuing.\n"
                : string.Empty;
            approved = await DialogService.ConfirmAsync(
                $"Allow PejPass to provide the username and password for this site?\n\n" +
                $"Current site: {domain}\nAccount: {Limit(entry.Title, 200)}\nUsername: {Limit(entry.Username, 256)}{domainNotice}\n" +
                "Only allow this if you just requested autofill.",
                "Confirm Browser Autofill",
                yesText: "Allow",
                noText: "Deny",
                bringToFront: true,
                cancellationToken: cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested || !_vaultSession.IsActive)
            return "vault_locked";

        return approved ? null : "fill_denied";
    }

    private async Task<WindowsHelloVerificationResult> VerifyBrowserAutofillWithWindowsHelloAsync(
        string domain, string title, string username)
    {
        if (_fillHelloVerification is not null)
            return await _fillHelloVerification(domain);

        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
            return WindowsHelloVerificationResult.Unavailable;

        if (!await WindowsHelloHelper.IsAvailableAsync())
            return WindowsHelloVerificationResult.Unavailable;

        // Browser autofill can be requested while every PejPass window is hidden.
        // Passing a hidden LoginWindow/MainWindow HWND can place the native Hello
        // prompt at a screen corner. Use a transparent, centered owner just for
        // this verification; it must not reveal or activate the main application UI.
        var owner = new System.Windows.Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = System.Windows.WindowStyle.None,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Opacity = 0,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
        };

        try
        {
            owner.Show();

            var message = $"Confirm browser autofill\nSite: {domain}\nAccount: {SanitizePromptText(title)}\nUsername: {SanitizePromptText(username)}";
            return await WindowsHelloHelper.VerifyAsync(owner, message);
        }
        finally
        {
            owner.Close();
        }
    }

    // Synchronous test fallback only. Production requests use
    // CreateSaveCredentialResponseAsync through RunAsync.
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

    internal static string SanitizeCredentialUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("A valid HTTP or HTTPS URL is required.", nameof(value));

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }

    internal static string GetCredentialOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("A valid HTTP or HTTPS URL is required.", nameof(value));

        return uri.GetLeftPart(UriPartial.Authority);
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

    private VaultEntry? FindEntryForFill(Guid id, string domain)
    {
        var entry = FindEntryById(id);
        return entry is not null && EntryMatchesDomain(entry, domain) ? entry : null;
    }

    private VaultEntry? FindEntryById(Guid id)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault) return null;
        return vault.Entries.FirstOrDefault(entry => entry.Id == id);
    }

    private async Task<FillCredential?> FindCredentialForFillAsync(Guid id, string domain, bool allowDomainMismatch = false)
    {
        var entry = allowDomainMismatch ? FindEntryById(id) : FindEntryForFill(id, domain);
        if (entry is null) return null;

        var totpCode = string.Empty;
        if (!string.IsNullOrWhiteSpace(entry.TotpSecret))
        {
            var totpSecret = entry.TotpSecret;
            var now = DateTime.UtcNow;
            if (TotpHelper.TryGetCode(
                    totpSecret, now, out totpCode, out var remainingSeconds, out var periodSeconds) &&
                remainingSeconds is > 0 and < 3)
            {
                // Precompute the next time-step code before waiting for it to become valid.
                var nextCodeReady = TotpHelper.TryGetCode(
                    totpSecret, now.AddSeconds(periodSeconds), out var nextCode, out _, out _);

                // Avoid returning a code that may expire before the user submits the login form.
                await Task.Delay(TimeSpan.FromSeconds(remainingSeconds));

                // Re-read the entry in case it changed while waiting.
                entry = allowDomainMismatch ? FindEntryById(id) : FindEntryForFill(id, domain);
                if (entry is null) return null;

                if (nextCodeReady &&
                    string.Equals(entry.TotpSecret, totpSecret, StringComparison.Ordinal))
                {
                    totpCode = nextCode;
                }
                else
                {
                    // The TOTP secret changed, so generate a code from the current entry.
                    totpCode = string.Empty;
                    if (!string.IsNullOrWhiteSpace(entry.TotpSecret))
                        TotpHelper.TryGetCode(entry.TotpSecret, out totpCode, out _);
                }
            }
        }

        var password = entry.Password ?? string.Empty;
        if (password.Length > MaximumPasswordLength) return null;

        return new FillCredential(entry.Username ?? string.Empty, password, totpCode);
    }

    private List<CredentialSummary> FindCredentials(string domain)
    {
        if (!_vaultSession.IsActive || _vaultSession.Vault is not { } vault) return [];

        var registrableDomain = GetRegistrableDomain(domain);
        if (registrableDomain is null) return [];

        EnsureCredentialIndex(vault);
        if (!_credentialIndexByRegistrableDomain.TryGetValue(registrableDomain, out var entries))
            return [];

        return [.. entries
            .OrderByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCredentialResults)
            .Select(entry => new CredentialSummary(
                entry.Id.ToString("D"), Limit(entry.Title, 200),
                Limit(entry.Username, 256), Limit(entry.Url, 2048)))];
    }

    private void EnsureCredentialIndex(Vault vault)
    {
        lock (_credentialIndexSync)
        {
            if (ReferenceEquals(_credentialIndexVault, vault) &&
                _credentialIndexUpdatedAt == vault.UpdatedAt)
                return;

            var index = new Dictionary<string, List<VaultEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in vault.Entries)
            {
                if (!TryGetEntryHost(entry.Url, out var host) ||
                    GetRegistrableDomain(host) is not { } registrableDomain)
                    continue;

                if (!index.TryGetValue(registrableDomain, out var entries))
                {
                    entries = [];
                    index.Add(registrableDomain, entries);
                }

                entries.Add(entry);
            }

            _credentialIndexByRegistrableDomain = index;
            _credentialIndexVault = vault;
            _credentialIndexUpdatedAt = vault.UpdatedAt;
        }
    }

    private void ClearCredentialIndex()
    {
        lock (_credentialIndexSync)
        {
            _credentialIndexVault = null;
            _credentialIndexUpdatedAt = default;
            _credentialIndexByRegistrableDomain.Clear();
        }
    }

    internal static bool RequiresCrossDomainConfirmation(
        VaultEntry entry, string domain, bool allowDomainMismatch) =>
        allowDomainMismatch && !EntryMatchesDomain(entry, domain);

    internal static bool EntryMatchesDomain(VaultEntry entry, string domain)
    {
        if (!TryGetEntryHost(entry.Url, out var entryHost)) return false;

        var entryRegistrableDomain = GetRegistrableDomain(entryHost);
        var requestedRegistrableDomain = GetRegistrableDomain(domain);
        return entryRegistrableDomain is not null &&
               requestedRegistrableDomain is not null &&
               entryRegistrableDomain.Equals(requestedRegistrableDomain, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? GetRegistrableDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        var normalizedHost = host.Trim().TrimEnd('.');
        if (IPAddress.TryParse(normalizedHost, out var ipAddress))
            return ipAddress.ToString().ToLowerInvariant();

        var labels = normalizedHost.ToLowerInvariant().Split('.');
        if (labels.Length < 2 || labels.Any(label => label.Length == 0)) return null;

        var publicSuffixLength = PublicSuffixList.GetPublicSuffixLength(labels);
        if (labels.Length <= publicSuffixLength) return null;
        return string.Join(".", labels[^(publicSuffixLength + 1)..]);
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

    private static string SanitizePromptText(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static bool IsHelloReady()
    {
        try
        {
            var settings = App.Services.GetService<AppSettings>();
            var cache = App.Services.GetService<SessionPasswordCache>();
            return settings?.WindowsHelloEnabled == true &&
                cache is not null &&
                settings.RecentVaultPaths.Any(cache.HasCacheFor);
        }
        catch
        {
            return false;
        }
    }

    private static StatusResponse Error(string code) =>
        new("error", "PejPass", ProtocolVersion, null, code);

    private static StatusResponse RateLimited(TimeSpan retryAfter) =>
        new("error", "PejPass", ProtocolVersion, null, "unlock_rate_limited",
            RetryAfterSeconds: (int)Math.Ceiling(Math.Max(1, retryAfter.TotalSeconds)));

    public void Dispose()
    {
        _vaultSession.StateChanged -= OnVaultStateChanged;
        _shutdown.Cancel();
        lock (_vaultLockSync)
        {
            _vaultLockCancellation.Cancel();
            _vaultLockCancellation.Dispose();
        }

        try
        {
            var tasks = new[] { _serverTask, _eventsTask }.OfType<Task>().ToArray();
            Task.WaitAll(tasks, TimeSpan.FromSeconds(1));
        }
        catch (AggregateException) { }

        _shutdown.Dispose();
    }

    internal sealed record CredentialSummary(string Id, string Title, string Username, string Url);
    internal sealed record FillCredential(string Username, string Password, string TotpCode);
    internal sealed record StatusResponse(
        string Type, string App, int ProtocolVersion, string? Status, string? Code,
        string? Domain = null, IReadOnlyList<CredentialSummary>? Credentials = null,
        string? Id = null, string? Username = null, string? Password = null,
        string? TotpCode = null, string? Result = null, int? RetryAfterSeconds = null,
        int? ClipboardClearSeconds = null, bool HelloReady = false,
        string? StrengthLabel = null, int? StrengthLevel = null, double? StrengthProgress = null);
}
