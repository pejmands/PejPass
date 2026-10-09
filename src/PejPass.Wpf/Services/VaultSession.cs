using PejPass.Application.Security;
using PejPass.Domain.Entities;
using System.IO;

namespace PejPass.Wpf.Services;

public sealed class VaultSession : IDisposable
{
    private readonly object _sync = new();
    private VaultKeyMaterial? _keyMaterial;
    private Vault? _vault;
    private string? _vaultPath;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public Vault? Vault
    {
        get { lock (_sync) return _vault; }
        private set { lock (_sync) _vault = value; }
    }

    public string? VaultPath
    {
        get { lock (_sync) return _vaultPath; }
        private set { lock (_sync) _vaultPath = value; }
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return !_disposed &&
                    _vault is not null &&
                    !string.IsNullOrEmpty(_vaultPath) &&
                    _keyMaterial is not null;
        }
    }

    public void Open(
        Vault vault,
        string vaultPath,
        VaultKeyMaterial keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentNullException.ThrowIfNull(keyMaterial);

        var fullPath = Path.GetFullPath(vaultPath);
        var materialCopy = keyMaterial.Clone();

        lock (_sync)
        {
            ThrowIfDisposed();
            ClearKeyMaterialCore();
            _vault = vault;
            _vaultPath = fullPath;
            _keyMaterial = materialCopy;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceVault(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        lock (_sync)
        {
            ThrowIfDisposed();
            _vault = vault;
        }
    }

    public VaultKeyMaterial CopyKeyMaterial()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_keyMaterial is null)
                throw new InvalidOperationException("No active vault session.");

            return _keyMaterial.Clone();
        }
    }

    public void UpdateKeyMaterial(VaultKeyMaterial keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        var materialCopy = keyMaterial.Clone();

        lock (_sync)
        {
            ThrowIfDisposed();
            ClearKeyMaterialCore();
            _keyMaterial = materialCopy;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        var changed = false;

        lock (_sync)
        {
            if (_vault is null && _vaultPath is null && _keyMaterial is null)
                return;

            ClearKeyMaterialCore();
            _vault = null;
            _vaultPath = null;
            changed = true;
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            ClearKeyMaterialCore();
            _vault = null;
            _vaultPath = null;
            _disposed = true;
            StateChanged = null;
        }

        GC.SuppressFinalize(this);
    }

    private void ClearKeyMaterialCore()
    {
        _keyMaterial?.Dispose();
        _keyMaterial = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
