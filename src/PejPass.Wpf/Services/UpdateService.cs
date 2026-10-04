using System.Diagnostics;

namespace PejPass.Wpf.Services;

/// <summary>
/// Handles version information and update discovery.
/// PejPass is offline-first: we never perform automatic network checks.
/// "Check for Updates" simply opens the GitHub Releases page so the user stays in control.
/// </summary>
public sealed class UpdateService
{
    public string CurrentVersion => AppInfoService.Version;

    public string StatusMessage =>
        $"You are running v{CurrentVersion}. Updates are published on GitHub.";

    /// <summary>
    /// Opens the GitHub Releases page in the default browser.
    /// </summary>
    public void OpenReleasesPage()
    {
        var url = $"{AppInfoService.RepositoryUrl.TrimEnd('/')}/releases";

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Opens the main repository page.
    /// </summary>
    public void OpenRepositoryPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppInfoService.RepositoryUrl,
            UseShellExecute = true
        });
    }
}
