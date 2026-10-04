using PejPass.Wpf.Records;

namespace PejPass.Wpf.Data;

public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new ReleaseNote
        {
            Version = "0.1.0",
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
