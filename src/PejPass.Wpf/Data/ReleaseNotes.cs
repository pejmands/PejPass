using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Data;

/// <summary>
/// Offline fallback for What's New when update.json is unreachable.
/// Prefer publishing structured notes in update.json for the live changelog.
/// </summary>
public static class ReleaseNotes
{
    public static ReleaseNote Latest => All[0];

    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new ReleaseNote
        {
            Version = $"v{AppInfoService.Version}",
            Date = AppInfoService.ReleaseDate,

            Added =
            [
                "Manual update check and download (PejTools-style update.json)",
                "About window with version and release date",
                "What's New (from update.json when online)",
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
    ];
}
