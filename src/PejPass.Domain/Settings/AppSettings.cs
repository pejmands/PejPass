namespace PejPass.Domain.Settings;

/// <summary>
/// Application-level settings (not encrypted).
/// Stored under LocalAppData\PejPass\settings.json
/// </summary>
public sealed class AppSettings
{
    public List<string> RecentVaultPaths { get; set; } = [];

    public int AutoLockMinutes { get; set; } = 10;

    public int ClipboardClearSeconds { get; set; } = 30;

    public int RevealSecretSeconds { get; set; } = 10;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public FontSizeMode FontSize { get; set; } = FontSizeMode.Medium;

    public double Zoom { get; set; } = 1.0;

    /// <summary>How the entry list is ordered. Favorites always float to the top.</summary>
    public EntrySortMode SortMode { get; set; } = EntrySortMode.TitleAsc;

    /// <summary>How the tag filter chips are ordered.</summary>
    public TagSortMode TagSortMode { get; set; } = TagSortMode.MostUsed;

    /// <summary>
    /// After a successful master-password unlock in this process, allow Windows Hello
    /// to unlock again without retyping the password (same vault path).
    /// </summary>
    public bool WindowsHelloEnabled { get; set; } = true;

    public bool OnlineFaviconFetchingEnabled { get; set; }

    /// <summary>
    /// When true, PejPass checks for updates once at startup (manual install still required).
    /// Default: off.
    /// </summary>
    public bool AutoCheckForUpdates { get; set; }

    /// <summary>
    /// When true, closing or minimizing the main window hides it to the system tray
    /// instead of exiting. Real exit only via tray menu or explicit Exit.
    /// Default: off (opt-in).
    /// </summary>
    public bool MinimizeToSystemTray { get; set; }

    public bool HasSeenFastDragScrollTip { get; set; }

    public static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass",
            "settings.json");
}

public enum TagSortMode
{
    MostUsed,
    Alphabetical
}
