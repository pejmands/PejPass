using PejPass.Application.Interfaces;
using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Policies;
using System.Security.Cryptography;

namespace PejPass.Application.Services;

public sealed class VaultService(IVaultStore store)
{
    private readonly IVaultStore _store = store;

    public async Task<Vault> CreateVaultAsync(
        string path,
        string masterPassword,
        string vaultName = "Personal Vault",
        CancellationToken ct = default)
    {
        using var session = await CreateVaultSessionAsync(path, masterPassword, vaultName, ct);
        return session.Vault;
    }

    public async Task<VaultSessionData> CreateVaultSessionAsync(
        string path,
        string masterPassword,
        string vaultName = "Personal Vault",
        CancellationToken ct = default)
    {
        var validation = MasterPasswordPolicy.Validate(masterPassword);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage);

        if (_store.Exists(path))
            throw new InvalidOperationException("A vault already exists at the specified path.");

        var vault = new Vault { Name = vaultName };
        return await _store.CreateSessionAsync(path, masterPassword, vault, ct);
    }

    public async Task<Vault> OpenVaultAsync(
        string path,
        string masterPassword,
        CancellationToken ct = default)
    {
        if (!_store.Exists(path))
            throw new FileNotFoundException("Vault file not found.", path);

        return await _store.OpenAsync(path, masterPassword, ct);
    }

    public async Task<VaultSessionData> OpenVaultSessionAsync(
        string path,
        string masterPassword,
        CancellationToken ct = default)
    {
        if (!_store.Exists(path))
            throw new FileNotFoundException("Vault file not found.", path);

        return await _store.OpenSessionAsync(path, masterPassword, ct);
    }

    public Task<Vault> OpenVaultWithKeyAsync(
        string path,
        VaultKeyMaterial keyMaterial,
        CancellationToken ct = default) =>
        _store.OpenWithKeyAsync(path, keyMaterial, ct);

    public async Task EnsureVaultWritableAsync(
        string path,
        CancellationToken ct = default)
    {
        await _store.EnsureWritableAsync(path, ct);
    }

    public Task SaveVaultAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default) =>
        _store.SaveAsync(path, masterPassword, vault, ct);

    public Task SaveVaultAsync(
        string path,
        VaultKeyMaterial keyMaterial,
        Vault vault,
        CancellationToken ct = default) =>
        _store.SaveAsync(path, keyMaterial, vault, ct);

    /// <summary>
    /// Verifies the current password against the on-disk vault, then atomically re-encrypts
    /// the in-memory vault with new key material. The caller replaces the session key only
    /// after this method succeeds.
    /// </summary>
    public async Task ChangeMasterPasswordAsync(
        string path,
        string currentPassword,
        string newPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        using var currentSession = await _store.OpenSessionAsync(path, currentPassword, ct);
        using var newKeyMaterial = await ChangeMasterPasswordAsync(
            path,
            currentPassword,
            newPassword,
            vault,
            currentSession.KeyMaterial,
            ct);
    }

    public async Task<VaultKeyMaterial> ChangeMasterPasswordAsync(
        string path,
        string currentPassword,
        string newPassword,
        Vault vault,
        VaultKeyMaterial currentKeyMaterial,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(currentKeyMaterial);

        var validation = MasterPasswordPolicy.Validate(newPassword);
        if (!validation.IsValid)
            throw new ArgumentException(validation.ErrorMessage);

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new ArgumentException("New password must be different from the current password.");

        using var verifiedSession = await _store.OpenSessionAsync(path, currentPassword, ct);

        var sameKey = CryptographicOperations.FixedTimeEquals(
            currentKeyMaterial.Key.Span,
            verifiedSession.KeyMaterial.Key.Span);
        var sameSalt = CryptographicOperations.FixedTimeEquals(
            currentKeyMaterial.Salt.Span,
            verifiedSession.KeyMaterial.Salt.Span);

        if (!sameKey || !sameSalt ||
            currentKeyMaterial.KdfParameters != verifiedSession.KeyMaterial.KdfParameters)
        {
            throw new InvalidOperationException(
                "The vault file no longer matches the active session. Reopen the vault before changing its master password.");
        }

        return await _store.SaveWithNewPasswordAsync(
            path,
            newPassword,
            vault,
            currentKeyMaterial.KdfParameters,
            ct);
    }
}
