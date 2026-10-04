using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Data;

/// <summary>
/// Single source of truth for the in-app changelog.
/// Keep this in sync with GitHub Releases when you publish a new version.
/// </summary>
public static class ReleaseNotes
{
    public static ReleaseNote Latest => All[0];

    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new ReleaseNote
        {
            Version = AppInfoService.Version,
            Date = "October 2026",

            Added =
            [
                "About window with version info and manual update check",
                "What's New changelog window",
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
