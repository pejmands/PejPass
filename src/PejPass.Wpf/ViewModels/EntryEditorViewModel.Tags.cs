using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class EntryEditorViewModel
{
    private static readonly string[] DefaultTags =
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

    public ObservableCollection<EntryEditorTagItem> SuggestedTags { get; } = [];

    public EntryEditorViewModel(VaultEntry? existing, IEnumerable<string>? usedTags)
        : this(existing)
    {
        BuildSuggestedTags(usedTags);
    }

    private void BuildSuggestedTags(IEnumerable<string>? usedTags)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in DefaultTags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                tags.TryAdd(tag.Trim(), tag.Trim());
        }

        if (usedTags is not null)
        {
            foreach (var tag in usedTags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    var trimmed = tag.Trim();
                    tags.TryAdd(trimmed, trimmed);
                }
            }
        }

        SuggestedTags.Clear();

        foreach (var tag in tags.Values.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            SuggestedTags.Add(new EntryEditorTagItem
            {
                Name = tag,
                IsSelected = IsTagSelected(tag)
            });
        }
    }

    [RelayCommand]
    private void ToggleSuggestedTag(EntryEditorTagItem? item)
    {
        if (item is null)
            return;

        var parts = TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var existing = parts.FindIndex(
            t => t.Equals(item.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            parts.RemoveAt(existing);
            item.IsSelected = false;
        }
        else
        {
            parts.Add(item.Name);
            item.IsSelected = true;
        }

        TagsText = string.Join(", ", parts);
    }

    private bool IsTagSelected(string tag)
    {
        return TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnTagsTextChanged(string value)
    {
        foreach (var tag in SuggestedTags)
            tag.IsSelected = IsTagSelected(tag.Name);
    }
}

public partial class EntryEditorTagItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;

    public string DisplayLabel => Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
