using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Data;

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
                "TOTP support",
                "Vault Health dashboard",
                "Trash and restore workflow"
            ],

            Improved =
            [
                "Search experience",
                "Theme handling",
                "Clipboard security"
            ],

            Fixed =
            [
                "Entry editing issues",
                "UI consistency improvements"
            ]
        }
    ];
}
