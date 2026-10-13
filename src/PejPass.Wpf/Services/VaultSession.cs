using PejPass.Application.Security;
using PejPass.Domain.Entities;
using System.IO;

namespace PejPass.Wpf.Services;

public sealed class VaultSession : IDisposable
{
    private readonly record struct DiskStamp(bool IsAvailable, bool Exists, long Length, DateTime LastWriteTimeUtc);

    private readonly object _sync = new();
    private VaultKeyMaterial? _keyMaterial;
    private Vault? _vault;
    private string? _vaultPath;
    private DiskStamp? _diskStamp;
    private bool _disposed;
    private long _generation;

    public event EventHandler? StateChanged;

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
        var diskStamp = TryReadDiskStamp(fullPath);
        var materialCopy = keyMaterial.Clone();

        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                ClearKeyMaterialCore();
                _vault = vault;
                _vaultPath = fullPath;
                _diskStamp = diskStamp;
                _keyMaterial = materialCopy;
                _generation++;
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

        lock (_sync)
        {
            ThrowIfDisposed();
            _vault = vault;
            _generation++;
        }
    }

    internal bool HasExternalFileChanges(long generation, string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return true;
        }

        var currentStamp = TryReadDiskStamp(fullPath);

        lock (_sync)
        {
            if (_disposed ||
                generation != _generation ||
                _vault is null ||
                _keyMaterial is null ||
                !string.Equals(_vaultPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_diskStamp is not { } expected ||
                !expected.IsAvailable ||
                !currentStamp.IsAvailable)
            {
                return true;
            }

            return expected != currentStamp;
        }
    }

    internal bool TryRefreshDiskStamp(long generation, string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        var currentStamp = TryReadDiskStamp(fullPath);

        lock (_sync)
        {
            if (_disposed ||
                generation != _generation ||
                _vault is null ||
                _keyMaterial is null ||
                !string.Equals(_vaultPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _diskStamp = currentStamp;
            return true;
        }
    }

    private static DiskStamp TryReadDiskStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            return info.Exists
                ? new DiskStamp(true, true, info.Length, info.LastWriteTimeUtc)
                : new DiskStamp(true, false, 0, default);
        }
        catch
        {
            return new DiskStamp(false, false, 0, default);
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
            _diskStamp = null;
            _generation++;
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
