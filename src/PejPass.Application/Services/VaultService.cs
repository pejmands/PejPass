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
}
