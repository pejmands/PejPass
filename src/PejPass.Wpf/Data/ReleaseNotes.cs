using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Data;

/// <summary>
/// Offline fallback changelog when update.json cannot be reached.
/// Keep newest first. Prefer publishing a full releases[] on the server.
/// </summary>
public static class ReleaseNotes
{
    public static ReleaseNote Latest => All[0];

    public static IReadOnlyList<ReleaseNote> All { get; } = Build();

    private static ReleaseNote[] Build()
    {
        var installed = AppInfoService.Version;

        var raw = new[]
        {
            new ReleaseNote
            {
                Version = $"v{installed}",
                Date = AppInfoService.ReleaseDate,
                IsLatest = true,
                IsInstalled = true,
                IsAvailableUpdate = false,
                Added =
                [
                    "Manual update check and download (PejTools-style update.json)",
                    "About window with version and release date from the executable",
                    "What's New multi-version changelog",
                    "TOTP support",
                    "Vault Health dashboard",
                    "Trash and restore workflow"
                ],
                Improved =
                [
                    "Search experience",
                    "Theme handling",
                    "Clipboard security",
                    "Entry list drag-and-drop UX"
                ],
                Fixed =
                [
                    "Entry editing edge cases",
                    "UI consistency across dialogs"
                ]
            }
        };

        return raw;
    }
}
