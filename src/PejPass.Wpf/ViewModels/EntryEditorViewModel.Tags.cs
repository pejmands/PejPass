using CommunityToolkit.Mvvm.Input;

namespace PejPass.Wpf.ViewModels;

public partial class EntryEditorViewModel
{
    /// <summary>
    /// Common tags shown as one-click chips in the editor.
    /// Not forced onto entries — only added when the user taps them.
    /// </summary>
    public string[] SuggestedTags { get; } =
    [
        "Banking",
        "Cloud",
        "Crypto",
        "Dev",
        "Education",
        "Email",
        "Entertainment",
        "Family",
        "Finance",
        "Gaming",
        "Government",
        "Health",
        "Hosting",
        "Insurance",
        "Messaging",
        "Personal",
        "Shopping",
        "Social",
        "Subscriptions",
        "Travel",
        "Utilities",
        "VPN",
        "Work"
    ];

    [RelayCommand]
    private void ToggleSuggestedTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        tag = tag.Trim();
        var parts = TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var existing = parts.FindIndex(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            parts.RemoveAt(existing);
        else
            parts.Add(tag);

        TagsText = string.Join(", ", parts);
    }
}
