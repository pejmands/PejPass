using PejPass.Domain.Entities;

namespace PejPass.Application.Security;

/// <summary>
/// Contains an opened or newly created vault and the key material for its active session.
/// </summary>
public sealed class VaultSessionData : IDisposable
{
    public Vault Vault { get; }
    public VaultKeyMaterial KeyMaterial { get; }

    public VaultSessionData(Vault vault, VaultKeyMaterial keyMaterial)
    {
        Vault = vault ?? throw new ArgumentNullException(nameof(vault));
        KeyMaterial = keyMaterial ?? throw new ArgumentNullException(nameof(keyMaterial));
    }

    public void Dispose()
    {
        KeyMaterial.Dispose();
        GC.SuppressFinalize(this);
    }
}
