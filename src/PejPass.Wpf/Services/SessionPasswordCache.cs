using System.Security.Cryptography;
using System.Text;

namespace PejPass.Wpf.Services;

/// <summary>
/// In-process DPAPI cache of the master password (CurrentUser scope).
/// Survives Lock within the same app run; cleared on process exit.
/// Bound to a vault path so the wrong file cannot be unlocked with a cached secret.
/// </summary>
public static class SessionPasswordCache
{
    private static byte[]? _protected;
    private static string? _vaultPath;

    public static bool HasCacheFor(string? vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) || _protected is not { Length: > 0 } || _vaultPath is null)
            return false;

        return string.Equals(
            Path.GetFullPath(vaultPath),
            Path.GetFullPath(_vaultPath),
            StringComparison.OrdinalIgnoreCase);
    }

    public static void Store(string vaultPath, string password)
    {
        Clear();

        if (string.IsNullOrEmpty(vaultPath) || string.IsNullOrEmpty(password))
            return;

        try
        {
            _protected = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password),
                optionalEntropy: Encoding.UTF8.GetBytes("PejPass.Session." + Path.GetFullPath(vaultPath).ToLowerInvariant()),
                scope: DataProtectionScope.CurrentUser);
            _vaultPath = Path.GetFullPath(vaultPath);
        }
        catch
        {
            Clear();
        }
    }

    public static bool TryRestore(string vaultPath, out string password)
    {
        password = string.Empty;

        if (!HasCacheFor(vaultPath) || _protected is null)
            return false;

        try
        {
            var bytes = ProtectedData.Unprotect(
                _protected,
                optionalEntropy: Encoding.UTF8.GetBytes("PejPass.Session." + Path.GetFullPath(vaultPath).ToLowerInvariant()),
                scope: DataProtectionScope.CurrentUser);

            password = Encoding.UTF8.GetString(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            return !string.IsNullOrEmpty(password);
        }
        catch
        {
            password = string.Empty;
            return false;
        }
    }

    public static void Clear()
    {
        if (_protected != null)
        {
            CryptographicOperations.ZeroMemory(_protected);
            _protected = null;
        }

        _vaultPath = null;
    }
}
