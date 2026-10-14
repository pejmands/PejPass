using System.IO;

namespace PejPass.Wpf.Services;

/// <summary>
/// Keeps an in-memory, vault-specific recovery hint for a password change that was
/// saved successfully but whose original session was invalidated before completion.
/// </summary>
public sealed class PendingMasterPasswordChangeNotice
{
    public const string RecoveryHint =
        "The master password was changed before the vault was locked. " +
        "If you entered your previous password, try your new master password.";

    private readonly Lock _sync = new();
    private string? _vaultPath;

    public void Mark(string? vaultPath)
    {
        var normalizedPath = NormalizePath(vaultPath);
        if (normalizedPath is null)
            return;

        lock (_sync)
            _vaultPath = normalizedPath;
    }

    public bool IsPendingFor(string? vaultPath)
    {
        var normalizedPath = NormalizePath(vaultPath);
        if (normalizedPath is null)
            return false;

        lock (_sync)
        {
            return _vaultPath is not null &&
                string.Equals(
                    normalizedPath,
                    _vaultPath,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public void ClearFor(string? vaultPath)
    {
        var normalizedPath = NormalizePath(vaultPath);
        if (normalizedPath is null)
            return;

        lock (_sync)
        {
            if (string.Equals(
                    normalizedPath,
                    _vaultPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _vaultPath = null;
            }
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
