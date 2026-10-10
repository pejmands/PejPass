using Microsoft.Win32;

namespace PejPass.Wpf.Tests.Security;

public sealed class WindowsSecurityServiceTests
{
    [Fact]
    public void SessionLock_RequestsSecurityLock()
    {
        using var service = new Services.WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.HandleSessionSwitch(SessionSwitchReason.SessionLock);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void SessionUnlock_DoesNotRequestSecurityLock()
    {
        using var service = new Services.WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.HandleSessionSwitch(SessionSwitchReason.SessionUnlock);

        Assert.Equal(0, requests);
    }

    [Fact]
    public void Suspend_RequestsSecurityLock()
    {
        using var service = new Services.WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.HandlePowerModeChanged(PowerModes.Suspend);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void Resume_DoesNotRequestSecurityLock()
    {
        using var service = new Services.WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.HandlePowerModeChanged(PowerModes.Resume);

        Assert.Equal(0, requests);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSystemEvents()
    {
        using var service = new Services.WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.Dispose();
        service.HandleSessionSwitch(SessionSwitchReason.SessionLock);
        service.HandlePowerModeChanged(PowerModes.Suspend);

        Assert.Equal(0, requests);
    }
}
