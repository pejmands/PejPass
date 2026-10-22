using Microsoft.Win32;

namespace PejPass.Wpf.Services;

public enum SecurityLockReason
{
    SessionLock,
    SessionDisconnect,
    Suspend
}

/// <summary>
/// Locks the vault when Windows locks or disconnects the user session, or suspends the system.
/// </summary>
public sealed class WindowsSecurityService : IDisposable
{
    private bool _started;
    private bool _disposed;

    public event EventHandler<SecurityLockReason>? SecurityLockRequested;

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
        switch (reason)
        {
            case SessionSwitchReason.SessionLock:
                SecurityLockRequested?.Invoke(this, SecurityLockReason.SessionLock);
                break;

            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                SecurityLockRequested?.Invoke(this, SecurityLockReason.SessionDisconnect);
                break;
        }
    }

    public void HandlePowerModeChanged(PowerModes mode)
    {
        if (mode == PowerModes.Suspend)
            SecurityLockRequested?.Invoke(this, SecurityLockReason.Suspend);
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
