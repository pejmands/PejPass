using PejPass.Domain.Entities;

namespace PejPass.Application.Security;

/// <summary>
/// Contains an opened or newly created vault and the key material for its active session.
/// </summary>
public sealed class VaultSessionData(Vault vault, VaultKeyMaterial keyMaterial) : IDisposable
{
    public Vault Vault { get; } = vault ?? throw new ArgumentNullException(nameof(vault));
    public VaultKeyMaterial KeyMaterial { get; } = keyMaterial ?? throw new ArgumentNullException(nameof(keyMaterial));

    public void Dispose()
    {
        KeyMaterial.Dispose();
        GC.SuppressFinalize(this);
    }
}
