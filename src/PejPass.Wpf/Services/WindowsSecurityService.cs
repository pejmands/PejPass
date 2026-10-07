using Microsoft.Win32;

namespace PejPass.Wpf.Services;

/// <summary>
/// Locks the vault when Windows locks the user session or suspends the system.
/// </summary>
public sealed class WindowsSecurityService : IDisposable
{
    private bool _started;
    private bool _disposed;

    public event EventHandler? SecurityLockRequested;

    public void Start()
    {
        if (_started || _disposed)
            return;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _started = true;
    }

    public void HandleSessionSwitch(SessionSwitchReason reason)
    {
        if (reason == SessionSwitchReason.SessionLock)
            SecurityLockRequested?.Invoke(this, EventArgs.Empty);
    }

    public void HandlePowerModeChanged(PowerModes mode)
    {
        if (mode == PowerModes.Suspend)
            SecurityLockRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) =>
        HandleSessionSwitch(e.Reason);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        HandlePowerModeChanged(e.Mode);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_started)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _started = false;
        }

        SecurityLockRequested = null;
        GC.SuppressFinalize(this);
    }
}
