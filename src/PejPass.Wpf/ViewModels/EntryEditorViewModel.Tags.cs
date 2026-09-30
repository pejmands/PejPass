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

    private readonly List<string> _otherEntryTags = [];
    private readonly HashSet<string> _knownTags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _originalTags = new(StringComparer.Ordinal);

    public ObservableCollection<EntryEditorTagItem> SuggestedTags { get; } = [];

    public EntryEditorViewModel(VaultEntry? existing, IEnumerable<string>? usedTags)
        : this(existing)
    {
        if (existing is not null)
        {
            foreach (var tag in existing.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    _originalTags.Add(tag.Trim());
            }
        }

        if (usedTags is not null)
        {
            _otherEntryTags.AddRange(
                usedTags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim()));

            if (existing is not null)
            {
                foreach (var tag in existing.Tags)
                {
                    var index = _otherEntryTags.FindIndex(
                        t => t.Equals(tag.Trim(), StringComparison.Ordinal));

                    if (index >= 0)
                        _otherEntryTags.RemoveAt(index);
                }
            }
        }

        foreach (var tag in DefaultTags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                _knownTags.Add(tag.Trim());
        }

        if (usedTags is not null)
        {
            foreach (var tag in usedTags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    _knownTags.Add(tag.Trim());
            }
        }

        BuildSuggestedTags(usedTags);
    }

    private void BuildSuggestedTags(IEnumerable<string>? usedTags)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);

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

    public bool HasCaseVariantInOtherEntries(string tag)
    {
        if (_originalTags.Contains(tag))
            return false;

        if (_otherEntryTags.Any(
            existing => existing.Equals(tag, StringComparison.Ordinal)))
        {
            return false;
        }

        return _knownTags.Any(
            existing => existing.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                        !existing.Equals(tag, StringComparison.Ordinal));
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
            if (parts[existing].Equals(item.Name, StringComparison.Ordinal))
            {
                parts.RemoveAll(t => t.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                parts[existing] = item.Name;

                for (var i = parts.Count - 1; i > existing; i--)
                {
                    if (parts[i].Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                        parts.RemoveAt(i);
                }
            }
        }
        else
        {
            parts.Add(item.Name);
        }

        TagsText = string.Join(", ", parts);
    }

    private bool IsTagSelected(string tag)
    {
        return TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t.Equals(tag, StringComparison.Ordinal));
    }

    public static List<string> NormalizeTags(string? value)
    {
        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(tag))
                tags.Add(tag);
        }

        tags.Sort(StringComparer.OrdinalIgnoreCase);
        return tags;
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
