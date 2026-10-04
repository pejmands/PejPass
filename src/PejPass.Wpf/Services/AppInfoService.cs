namespace PejPass.Wpf.Services;

/// <summary>
/// Application identity and public endpoints.
/// Version is the single source of truth for the running build.
/// </summary>
public static class AppInfoService
{
    public const string Name = "PejPass";

    public const string Version = "0.1.0";

    public const string RepositoryUrl =
        "https://github.com/pejmands/PejPass";

    /// <summary>
    /// Manifest used by the manual "Check for Updates" action.
    /// For local testing point this at http://localhost/update.json
    /// In production this can be a GitHub raw URL or a static host you control.
    /// </summary>
    public const string UpdateManifestUrl =
        "http://localhost/update.json";
}
