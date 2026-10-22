using PejPass.Application.Security;
using PejPass.Domain.Entities;

namespace PejPass.Application.Interfaces;

public interface IVaultStore
{
    /// <summary>
    /// Creates a new encrypted vault file.
    /// </summary>
    Task CreateAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default);

    /// <summary>
    /// Creates a vault and returns the key material used by its active session.
    /// </summary>
    Task<VaultSessionData> CreateSessionAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a vault using the master password and returns its session key material.
    /// </summary>
    Task<VaultSessionData> OpenSessionAsync(
        string path,
        string masterPassword,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a vault using previously authenticated key material.
    /// </summary>
    Task<Vault> OpenWithKeyAsync(
        string path,
        VaultKeyMaterial keyMaterial,
        CancellationToken ct = default);

    /// <summary>
    /// Saves the current vault state using key material from the active session.
    /// </summary>
    Task SaveAsync(
        string path,
        VaultKeyMaterial keyMaterial,
        Vault vault,
        CancellationToken ct = default);

    /// <summary>
    /// Creates fresh key material from a master password and atomically saves the vault.
    /// </summary>
    Task<VaultKeyMaterial> SaveWithNewPasswordAsync(
        string path,
        string masterPassword,
        Vault vault,
        Argon2Parameters kdfParameters,
        CancellationToken ct = default);

    /// <summary>
    /// Opens and decrypts an existing vault.
    /// </summary>
    Task<Vault> OpenAsync(string path, string masterPassword, CancellationToken ct = default, bool migrateLegacy = true);

    /// <summary>
    /// Saves the current vault state (re-encrypts with a newly derived key).
    /// </summary>
    Task SaveAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the existing vault file can currently be opened for writing.
    /// </summary>
    Task EnsureWritableAsync(string path, CancellationToken ct = default);

    bool Exists(string path);
}
