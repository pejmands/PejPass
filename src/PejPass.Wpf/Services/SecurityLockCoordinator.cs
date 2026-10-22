namespace PejPass.Wpf.Services;

/// <summary>
/// Clears the Windows Hello cache on the calling thread and marshals only the UI lock.
/// </summary>
internal sealed class SecurityLockCoordinator(
    Action clearCache,
    Action lockVault,
    Func<Action, TimeSpan?, bool> dispatchToUi)
{
    /// <summary>Upper bound for waiting on the UI before the system goes to sleep.</summary>
    public static readonly TimeSpan SuspendWait = TimeSpan.FromSeconds(2);

    public void Handle(bool waitForUi)
    {
        try
        {
            clearCache();
        }
        catch
        {
            // A cache error must not prevent the vault UI from being locked.
        }

        try
        {
            // Suspend waits briefly; session lock/disconnect must not block the system event thread.
            dispatchToUi(lockVault, waitForUi ? SuspendWait : null);
        }
        catch
        {
            // The dispatcher may be shutting down.
        }
    }
}
