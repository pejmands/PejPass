namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Stop timers without locking UI — used when the main window is closed (app exit).
    /// </summary>
    public void StopBackgroundTimers()
    {
        try { _autoLockTimer?.Stop(); } catch { /* ignore */ }
        try { _totpTimer?.Stop(); } catch { /* ignore */ }
    }
}
