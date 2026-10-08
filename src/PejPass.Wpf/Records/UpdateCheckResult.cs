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
/// Result of a single manual update check against update.json.
/// </summary>
public sealed class UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }

    public string CurrentVersion { get; init; } = string.Empty;

    public string? LatestVersion { get; init; }

    public string? DownloadUrl { get; init; }

    public string? Sha256 { get; init; }

    public string? Released { get; init; }

    public UpdateNotes? Notes { get; init; }

    public string Message { get; init; } = string.Empty;

    public static UpdateCheckResult UpToDate(string current) => new()
    {
        Status = UpdateCheckStatus.UpToDate,
        CurrentVersion = current,
        LatestVersion = current,
        Message = $"You have the latest version (v{current})."
    };

    public static UpdateCheckResult Available(
        string current,
        UpdateManifest manifest)
    {
        var ver = manifest.Version.TrimStart('v', 'V');
        var msg = string.IsNullOrWhiteSpace(manifest.Released)
            ? $"Version {ver} is available."
            : $"Version {ver} is available ({manifest.Released}).";

        return new UpdateCheckResult
        {
            Status = UpdateCheckStatus.UpdateAvailable,
            CurrentVersion = current,
            LatestVersion = ver,
            DownloadUrl = manifest.DownloadUrl,
            Sha256 = manifest.Sha256,
            Released = manifest.Released,
            Notes = manifest.Notes,
            Message = msg
        };
    }

    public static UpdateCheckResult NetworkError(string detail) => new()
    {
        Status = UpdateCheckStatus.NetworkError,
        Message = $"Could not check for updates. {detail}"
    };

    public static UpdateCheckResult InvalidManifest(string detail) => new()
    {
        Status = UpdateCheckStatus.InvalidManifest,
        Message = $"Update information is invalid. {detail}"
    };
}
