using PejPass.Domain.Entities;

namespace PejPass.Application.Interfaces;

public interface IVaultStore
{
    /// <summary>
    /// Creates a new encrypted vault file.
    /// </summary>
    Task CreateAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default);

    /// <summary>
    /// Opens and decrypts an existing vault.
    /// </summary>
    Task<Vault> OpenAsync(string path, string masterPassword, CancellationToken ct = default);

    /// <summary>
    /// Saves the current vault state (re-encrypts).
    /// </summary>
    Task SaveAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the existing vault file can currently be opened for writing.
    /// </summary>
    Task EnsureWritableAsync(string path, CancellationToken ct = default);

    bool Exists(string path);
}
