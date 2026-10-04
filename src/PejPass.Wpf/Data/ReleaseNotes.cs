using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Data;

/// <summary>
/// Offline in-app changelog (What's New window).
/// Independent of the remote update.json — always available without network.
/// Keep this list honest and short; users read it after upgrades.
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
                "Manual update check via update.json",
                "About window with clear update status",
                "What's New changelog",
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
