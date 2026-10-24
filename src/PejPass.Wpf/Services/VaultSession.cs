using PejPass.Application.Security;
using PejPass.Domain.Entities;
using System.IO;

namespace PejPass.Wpf.Services;

public sealed class VaultSession : IDisposable
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private VaultKeyMaterial? _keyMaterial;
    private Vault? _vault;
    private string? _vaultPath;
    private bool _disposed;
    private long _generation;
    private VaultDiskStamp? _diskStamp;

    public event EventHandler? StateChanged;

    public async ValueTask<IDisposable?> AcquireSaveLeaseAsync(
        long generation,
        Vault vault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            if (!_disposed &&
                _generation == generation &&
                ReferenceEquals(_vault, vault) &&
                !string.IsNullOrEmpty(_vaultPath) &&
                _keyMaterial is not null)
                return new SaveLease(_saveGate);
        }

        _saveGate.Release();
        return null;
    }

    private sealed class SaveLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }

    /// <summary>Changes whenever the active vault session is replaced or invalidated.</summary>
    public long Generation
    {
        get { lock (_sync) return _generation; }
    }

    public bool IsCurrent(long generation, Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        lock (_sync)
            return !_disposed &&
                _generation == generation &&
                ReferenceEquals(_vault, vault) &&
                !string.IsNullOrEmpty(_vaultPath) &&
                _keyMaterial is not null;
    }

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

        try
        {
            _saveGate.Wait();
            try
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    ClearKeyMaterialCore();
                    _vault = vault;
                    _vaultPath = fullPath;
                    _diskStamp = CaptureDiskStamp(fullPath);
                    _keyMaterial = materialCopy;
                    _generation++;
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch
        {
            materialCopy.Dispose();
            throw;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceVault(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        _saveGate.Wait();
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _vault = vault;
                _generation++;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public bool HasExternalChanges()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _vaultPath is not null &&
                CaptureDiskStamp(_vaultPath) != _diskStamp;
        }
    }

    public void RefreshDiskStamp()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _diskStamp = _vaultPath is null ? null : CaptureDiskStamp(_vaultPath);
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

    public bool TryUpdateKeyMaterial(VaultKeyMaterial keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        var materialCopy = keyMaterial.Clone();
        bool updated;

        _saveGate.Wait();
        try
        {
            lock (_sync)
            {
                updated = !_disposed &&
                    _vault is not null &&
                    _vaultPath is not null &&
                    _keyMaterial is not null;

                if (updated)
                {
                    ClearKeyMaterialCore();
                    _keyMaterial = materialCopy;
                    _generation++;
                }
            }
        }
        finally
        {
            _saveGate.Release();
        }

        if (!updated)
        {
            materialCopy.Dispose();
            return false;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        var changed = false;

        _saveGate.Wait();
        try
        {
            lock (_sync)
            {
                if (_vault is null && _vaultPath is null && _keyMaterial is null)
                    return;

                ClearKeyMaterialCore();
                _vault = null;
                _vaultPath = null;
                _diskStamp = null;
                _generation++;
                changed = true;
            }
        }
        finally
        {
            _saveGate.Release();
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _saveGate.Wait();
        try
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                ClearKeyMaterialCore();
                _vault = null;
                _vaultPath = null;
                _diskStamp = null;
                _generation++;
                _disposed = true;
                StateChanged = null;
            }
        }
        finally
        {
            _saveGate.Release();
        }

        GC.SuppressFinalize(this);
    }

    private static VaultDiskStamp? CaptureDiskStamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? new VaultDiskStamp(info.Length, info.LastWriteTimeUtc)
            : null;
    }

    private readonly record struct VaultDiskStamp(long Length, DateTime LastWriteTimeUtc);

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
