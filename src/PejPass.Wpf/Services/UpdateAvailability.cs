namespace PejPass.Wpf.Services;

/// <summary>
/// Shared flag for "update available" UI badges (title-bar ··· menu).
/// Set by a background startup check when AutoCheckForUpdates is enabled.
/// </summary>
public static class UpdateAvailability
{
    public static bool IsUpdateAvailable { get; private set; }

    public static event Action? Changed;

    public static void Set(bool isUpdateAvailable)
    {
        if (IsUpdateAvailable == isUpdateAvailable)
            return;

        IsUpdateAvailable = isUpdateAvailable;
        Changed?.Invoke();
    }
}
