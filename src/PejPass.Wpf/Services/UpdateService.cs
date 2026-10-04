namespace PejPass.Wpf.Services;

public sealed class UpdateService
{
    public string CurrentVersion =>
        AppInfoService.Version;

    public string LatestVersion =>
        AppInfoService.Version;

    public bool HasUpdate =>
        !IsLatestVersion(LatestVersion);

    public string StatusMessage =>
        HasUpdate
            ? $"Version {LatestVersion} is available."
            : "You are using the latest version.";

    public bool IsLatestVersion(string version)
    {
        return string.Equals(
            CurrentVersion,
            version,
            StringComparison.OrdinalIgnoreCase);
    }
}
