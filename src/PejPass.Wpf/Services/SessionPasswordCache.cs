using PejPass.Domain.Settings;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PejPass.Wpf.Services;

/// <summary>
/// In-process DPAPI cache of the master password (CurrentUser scope).
/// The cache is bound to a vault path and expires according to AppSettings.
/// The protected bytes are cleared on expiry, security lock, password change, or app exit.
/// </summary>
/// <remarks>
/// ZeroMemory clears buffers owned by this service, but managed password strings and other
/// copies may remain in process memory until the runtime reclaims them.
/// </remarks>
public sealed class SessionPasswordCache : IDisposable
{
    private const int MaxConsecutiveHelloFailures = 3;
    private readonly object _sync = new();
    private readonly AppSettings _settings;
    private readonly TimeProvider _timeProvider;
    private byte[]? _protected;
    private string? _vaultPath;
    private DateTimeOffset? _cachedAt;
    private ITimer? _expiryTimer;
    private int _consecutiveHelloFailures;
    private bool _disposed;

    public SessionPasswordCache(AppSettings settings, TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public int ConsecutiveHelloFailures
    {
        get { lock (_sync) return _consecutiveHelloFailures; }
    }

    public bool HasCacheFor(string? vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return false;

        string fullPath;
        try { fullPath = Path.GetFullPath(vaultPath); }
        catch { return false; }

        lock (_sync)
        {
            ThrowIfDisposed();
            ExpireIfNeededCore();
            return _protected is { Length: > 0 } &&
                _vaultPath is not null &&
                _consecutiveHelloFailures < MaxConsecutiveHelloFailures &&
                string.Equals(fullPath, _vaultPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Call only after successful master-password authentication.</summary>
    public void Store(string vaultPath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrEmpty(password);

        string fullPath;
        try { fullPath = Path.GetFullPath(vaultPath); }
        catch { Clear(); return; }

        lock (_sync)
        {
            ThrowIfDisposed();
            ClearCore();
            byte[]? passwordBytes = null;
            try
            {
                passwordBytes = Encoding.UTF8.GetBytes(password);
                var entropy = Encoding.UTF8.GetBytes("PejPass.Session." + fullPath.ToLowerInvariant());
                _protected = ProtectedData.Protect(
                    passwordBytes,
                    optionalEntropy: entropy,
                    scope: DataProtectionScope.CurrentUser);
                _vaultPath = fullPath;
                _cachedAt = _timeProvider.GetUtcNow();
                _consecutiveHelloFailures = 0;
                ScheduleExpiryCore();
            }
            catch { ClearCore(); }
            finally
            {
                if (passwordBytes is not null)
                    CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
    }

    public bool TryRestore(string vaultPath, out string password)
    {
        password = string.Empty;
        if (!HasCacheFor(vaultPath))
            return false;

        string fullPath;
        try { fullPath = Path.GetFullPath(vaultPath); }
        catch { return false; }

        lock (_sync)
        {
            ThrowIfDisposed();
            ExpireIfNeededCore();
            if (_protected is not { Length: > 0 } || _vaultPath is null ||
                !string.Equals(fullPath, _vaultPath, StringComparison.OrdinalIgnoreCase))
                return false;

            byte[]? bytes = null;
            try
            {
                bytes = ProtectedData.Unprotect(
                    _protected,
                    optionalEntropy: Encoding.UTF8.GetBytes("PejPass.Session." + fullPath.ToLowerInvariant()),
                    scope: DataProtectionScope.CurrentUser);
                password = Encoding.UTF8.GetString(bytes);
                return !string.IsNullOrEmpty(password);
            }
            catch
            {
                password = string.Empty;
                ClearCore();
                return false;
            }
            finally
            {
                if (bytes is not null)
                    CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    /// <summary>
    /// Records one completed Windows Hello verification that exhausted authentication retries.
    /// Cancellation and system errors must not call this method.
    /// </summary>
    public bool RecordHelloAuthenticationFailure()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _consecutiveHelloFailures++;
            if (_consecutiveHelloFailures < MaxConsecutiveHelloFailures)
                return false;
            ClearCore();
            return true;
        }
    }

    /// <summary>Resets the failure counter after successful master-password authentication.</summary>
    public void ResetHelloFailures()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _consecutiveHelloFailures = 0;
        }
    }

    /// <summary>Re-evaluates the current timeout after the user changes settings.</summary>
    public void RefreshExpiry()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ExpireIfNeededCore();
            if (_protected is { Length: > 0 })
                ScheduleExpiryCore();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (!_disposed)
                ClearCore();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            ClearCore();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private TimeSpan? GetCacheLifetime() => _settings.WindowsHelloTimeoutMinutes switch
    {
        0 => null,
        15 => TimeSpan.FromMinutes(15),
        60 => TimeSpan.FromHours(1),
        240 => TimeSpan.FromHours(4),
        _ => TimeSpan.FromHours(4)
    };

    private bool IsExpiredCore()
    {
        var lifetime = GetCacheLifetime();
        return lifetime.HasValue && _cachedAt.HasValue &&
            _timeProvider.GetUtcNow() - _cachedAt.Value >= lifetime.Value;
    }

    private void ExpireIfNeededCore()
    {
        if (_protected is { Length: > 0 } && IsExpiredCore())
            ClearCore();
    }

    private void ScheduleExpiryCore()
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;
        var lifetime = GetCacheLifetime();
        if (!lifetime.HasValue || !_cachedAt.HasValue) return;

        var remaining = lifetime.Value - (_timeProvider.GetUtcNow() - _cachedAt.Value);
        if (remaining <= TimeSpan.Zero) { ClearCore(); return; }

        _expiryTimer = _timeProvider.CreateTimer(
            static state => ((SessionPasswordCache)state!).OnExpiryTimer(),
            this, remaining, Timeout.InfiniteTimeSpan);
    }

    private void OnExpiryTimer()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            ExpireIfNeededCore();
            if (_protected is { Length: > 0 })
                ScheduleExpiryCore();
        }
    }

    private void ClearCore()
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;
        if (_protected is not null)
        {
            CryptographicOperations.ZeroMemory(_protected);
            _protected = null;
        }
        _vaultPath = null;
        _cachedAt = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
