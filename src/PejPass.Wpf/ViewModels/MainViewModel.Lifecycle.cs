using System.Windows.Input;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Stop timers without locking UI — used when the main window is closed (app exit).
    /// </summary>
    public void StopBackgroundTimers()
    {
        try { StopAutoLockTimer(); } catch { /* ignore */ }
        try { _totpTimer?.Stop(); } catch { /* ignore */ }
        try { _searchDebouncer.Dispose(); } catch { /* ignore */ }
        try { _passwordRevealTimer.Dispose(); } catch { /* ignore */ }
        try
        {
            foreach (var item in DisplayCustomFields)
                item.Dispose();
        }
        catch { /* ignore */ }
        try { InputManager.Current.PreProcessInput -= OnPreProcessInput; } catch { /* ignore */ }
    }
}
