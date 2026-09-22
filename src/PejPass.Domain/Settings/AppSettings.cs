namespace PejPass.Domain.Settings;

/// <summary>
/// Application-level settings (not encrypted).
/// Stored under LocalAppData\PejPass\settings.json
/// </summary>
public sealed class AppSettings
{
    public string DefaultVaultDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass");

    public string? LastVaultPath { get; set; }

    public int AutoLockMinutes { get; set; } = 10;

    public int ClipboardClearSeconds { get; set; } = 30;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>How the entry list is ordered. Favorites always float to the top.</summary>
    public EntrySortMode SortMode { get; set; } = EntrySortMode.TitleAsc;

    /// <summary>
    /// After a successful master-password unlock in this process, allow Windows Hello
    /// to unlock again without retyping the password (same vault path).
    /// </summary>
    public bool WindowsHelloEnabled { get; set; } = true;

    public static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass",
            "settings.json");
}
