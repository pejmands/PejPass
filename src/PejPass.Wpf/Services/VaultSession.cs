using PejPass.Domain.Entities;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PejPass.Wpf.Services;

public sealed class VaultSession : IDisposable
{
    private byte[]? _secret;

    public Vault? Vault { get; private set; }

    public string? VaultPath { get; private set; }

    public bool IsActive =>
        Vault is not null &&
        !string.IsNullOrEmpty(VaultPath) &&
        _secret is { Length: > 0 };

    public void Open(
        Vault vault,
        string vaultPath,
        string secret)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        Clear();

        Vault = vault;
        VaultPath = Path.GetFullPath(vaultPath);
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string GetSecret()
    {
        if (_secret is not { Length: > 0 })
            throw new InvalidOperationException("No active vault session.");

        return Encoding.UTF8.GetString(_secret);
    }

    public void UpdateSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        ClearSecret();
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public void Clear()
    {
        ClearSecret();
        Vault = null;
        VaultPath = null;
    }

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }

    private void ClearSecret()
    {
        if (_secret is null)
            return;

        CryptographicOperations.ZeroMemory(_secret);
        _secret = null;
    }
}
