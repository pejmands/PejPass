namespace PejPass.Domain.Settings;

/// <summary>
/// Application-level settings (not encrypted).
/// Stored separately from the vault under LocalAppData\PejPass\settings.json
/// </summary>
public sealed class AppSettings
{
    public string DefaultVaultDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass");

    public string? LastVaultPath { get; set; }

    /// <summary>Auto-lock timeout in minutes. 0 = disabled.</summary>
    public int AutoLockMinutes { get; set; } = 10;

    /// <summary>Clipboard clear timeout in seconds.</summary>
    public int ClipboardClearSeconds { get; set; } = 30;

    /// <summary>UI theme: System, Dark, or Light.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass",
            "settings.json");
}
