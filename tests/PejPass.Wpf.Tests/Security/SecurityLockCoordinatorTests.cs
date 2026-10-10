using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Security;

public sealed class SecurityLockCoordinatorTests
{
    [Fact]
    public void Handle_ClearsCacheBeforeLockingUi()
    {
        var order = new List<string>();
        var coordinator = new SecurityLockCoordinator(
            () => order.Add("clear"),
            () => order.Add("lock"),
            (action, _) => { action(); return true; });

        coordinator.Handle(waitForUi: false);

        Assert.Equal(["clear", "lock"], order);
    }

    [Fact]
    public void Handle_ClearsCacheEvenWhenUiNeverRuns()
    {
        var cleared = 0;
        var coordinator = new SecurityLockCoordinator(
            () => cleared++,
            () => { },
            (_, _) => false);

        coordinator.Handle(waitForUi: true);

        Assert.Equal(1, cleared);
    }

    [Fact]
    public void Handle_ClearsCacheEvenWhenDispatchThrows()
    {
        var cleared = 0;
        var coordinator = new SecurityLockCoordinator(
            () => cleared++,
            () => { },
            (_, _) => throw new InvalidOperationException("Dispatcher shut down."));

        coordinator.Handle(waitForUi: false);

        Assert.Equal(1, cleared);
    }

    [Fact]
    public void Handle_StillLocksUiWhenClearingFails()
    {
        var locked = 0;
        var coordinator = new SecurityLockCoordinator(
            () => throw new InvalidOperationException("Cache failure."),
            () => locked++,
            (action, _) => { action(); return true; });

        coordinator.Handle(waitForUi: false);

        Assert.Equal(1, locked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Handle_WaitsBoundedTimeOnlyForSuspend(bool waitForUi)
    {
        TimeSpan? observedWait = TimeSpan.FromDays(1);
        var coordinator = new SecurityLockCoordinator(
            () => { },
            () => { },
            (_, wait) => { observedWait = wait; return true; });

        coordinator.Handle(waitForUi);

        Assert.Equal(
            waitForUi ? SecurityLockCoordinator.SuspendWait : null,
            observedWait);
    }
}
