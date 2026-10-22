using Microsoft.Win32;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Security;

public sealed class WindowsSecurityServiceTests
{
    [Fact]
    public void SessionLock_RequestsSecurityLock()
    {
        using var service = new WindowsSecurityService();

        var reasons = new List<SecurityLockReason>();
        service.SecurityLockRequested += (_, reason) => reasons.Add(reason);

        service.HandleSessionSwitch(SessionSwitchReason.SessionLock);

        Assert.Equal([SecurityLockReason.SessionLock], reasons);
    }

    [Theory]
    [InlineData(SessionSwitchReason.RemoteDisconnect)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect)]
    public void SessionDisconnect_RequestsSecurityLock(SessionSwitchReason switchReason)
    {
        using var service = new WindowsSecurityService();

        var reasons = new List<SecurityLockReason>();
        service.SecurityLockRequested += (_, reason) => reasons.Add(reason);

        service.HandleSessionSwitch(switchReason);

        Assert.Equal([SecurityLockReason.SessionDisconnect], reasons);
    }

    [Fact]
    public void SessionUnlock_DoesNotRequestSecurityLock()
    {
        using var service = new WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.HandleSessionSwitch(SessionSwitchReason.SessionUnlock);

        Assert.Equal(0, requests);
    }

    [Fact]
    public void Suspend_RequestsSecurityLock()
    {
        using var service = new WindowsSecurityService();

        var reasons = new List<SecurityLockReason>();
        service.SecurityLockRequested += (_, reason) => reasons.Add(reason);

        service.HandlePowerModeChanged(PowerModes.Suspend);

        Assert.Equal([SecurityLockReason.Suspend], reasons);
    }

    [Fact]
    public void Resume_DoesNotRequestSecurityLock()
    {
        using var service = new WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.HandlePowerModeChanged(PowerModes.Resume);

        Assert.Equal(0, requests);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSystemEvents()
    {
        using var service = new WindowsSecurityService();

        var requests = 0;
        service.SecurityLockRequested += (_, _) => requests++;

        service.Dispose();
        service.HandleSessionSwitch(SessionSwitchReason.SessionLock);
        service.HandlePowerModeChanged(PowerModes.Suspend);

        Assert.Equal(0, requests);
    }
}
