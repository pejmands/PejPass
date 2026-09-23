using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using PejPass.Domain.Policies;

namespace PejPass.Application.Services;

public sealed class VaultService(IVaultStore store)
{
    private readonly IVaultStore _store = store;

    public async Task<Vault> CreateVaultAsync(string path, string masterPassword, string vaultName = "Personal Vault", CancellationToken ct = default)
    {
        var validation = MasterPasswordPolicy.Validate(masterPassword);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage);

        if (_store.Exists(path))
            throw new InvalidOperationException("A vault already exists at the specified path.");

        var vault = new Vault { Name = vaultName };
        await _store.CreateAsync(path, masterPassword, vault, ct);
        return vault;
    }

    public async Task<Vault> OpenVaultAsync(string path, string masterPassword, CancellationToken ct = default)
    {
        if (!_store.Exists(path))
            throw new FileNotFoundException("Vault file not found.", path);

        return await _store.OpenAsync(path, masterPassword, ct);
    }

    public async Task SaveVaultAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default)
    {
        await _store.SaveAsync(path, masterPassword, vault, ct);
    }

    /// <summary>
    /// Verifies the current master password, then re-encrypts the vault with a new one.
    /// Pass the in-memory vault so unsaved entry edits are preserved.
    /// </summary>
    public async Task ChangeMasterPasswordAsync(
        string path,
        string currentPassword,
        string newPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(vault);

        var validation = MasterPasswordPolicy.Validate(newPassword);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage);

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new ArgumentException("New password must be different from the current password.");

        // Verify current password against the on-disk vault
        _ = await _store.OpenAsync(path, currentPassword, ct);

        await _store.SaveAsync(path, newPassword, vault, ct);
    }
}
