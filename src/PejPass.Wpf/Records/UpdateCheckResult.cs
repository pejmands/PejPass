namespace PejPass.Wpf.Records;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NetworkError,
    InvalidManifest,
    Cancelled
}

/// <summary>
/// Result of a single manual update check.
/// </summary>
public sealed class UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }

    public string CurrentVersion { get; init; } = string.Empty;

    public string? LatestVersion { get; init; }

    public string? DownloadUrl { get; init; }

    public string? Released { get; init; }

    public UpdateNotes? Notes { get; init; }

    public string Message { get; init; } = string.Empty;

    public static UpdateCheckResult UpToDate(string current) => new()
    {
        Status = UpdateCheckStatus.UpToDate,
        CurrentVersion = current,
        LatestVersion = current,
        Message = $"You are using the latest version (v{current})."
    };

    public static UpdateCheckResult Available(
        string current,
        UpdateManifest manifest) => new()
    {
        Status = UpdateCheckStatus.UpdateAvailable,
        CurrentVersion = current,
        LatestVersion = manifest.Version,
        DownloadUrl = manifest.DownloadUrl,
        Released = manifest.Released,
        Notes = manifest.Notes,
        Message = $"Version {manifest.Version} is available."
    };

    public static UpdateCheckResult NetworkError(string detail) => new()
    {
        Status = UpdateCheckStatus.NetworkError,
        Message = $"Could not reach the update server. {detail}"
    };

    public static UpdateCheckResult InvalidManifest(string detail) => new()
    {
        Status = UpdateCheckStatus.InvalidManifest,
        Message = $"Update information is invalid. {detail}"
    };
}
