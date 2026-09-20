namespace PejPass.Domain.Settings;

/// <summary>
/// Application-level settings (not encrypted).
/// Stored separately from the vault.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Default vault location under LocalAppData\PejPass
    /// </summary>
    public string DefaultVaultDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass");

    /// <summary>
    /// Last used vault path (can be overridden by user).
    /// </summary>
    public string? LastVaultPath { get; set; }

    /// <summary>
    /// Auto-lock timeout in minutes. 0 = disabled.
    /// </summary>
    public int AutoLockMinutes { get; set; } = 10;

    /// <summary>
    /// Clipboard clear timeout in seconds.
    /// </summary>
    public int ClipboardClearSeconds { get; set; } = 30;
}
