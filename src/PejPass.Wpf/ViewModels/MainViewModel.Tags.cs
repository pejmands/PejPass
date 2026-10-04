using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Settings;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    private readonly HashSet<string> _selectedTagFilters = new(StringComparer.Ordinal);

    public IReadOnlySet<string> SelectedTagFilters => _selectedTagFilters;

    public bool IsNoTagsFilterSelected { get; private set; }

    public ObservableCollection<TagFilterItem> TagFilters { get; } = [];

    /// <summary>True when the vault uses at least one tag (strip is worth showing).</summary>
    [ObservableProperty]
    public partial bool ShowTagFilters { get; set; }

    /// <summary>
    /// Rebuild chip list from tags actually used in the vault (with counts).
    /// Call whenever Entries membership or tags change.
    /// </summary>
    private void RebuildTagFilters()
    {
        var selectedTags = _selectedTagFilters;
        var noTagsSelected = IsNoTagsFilterSelected;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in Entries)
        {
            foreach (var raw in entry.Tags)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var tag = raw.Trim();
                if (!labels.ContainsKey(tag))
                    labels[tag] = tag;

                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        // Drop selected filters for tags that no longer exist.
        selectedTags.RemoveWhere(tag => !counts.ContainsKey(tag));

        var noTagsCount = Entries.Count(entry =>
            !entry.Tags.Any(tag => !string.IsNullOrWhiteSpace(tag)));

        if (noTagsCount == 0)
            IsNoTagsFilterSelected = noTagsSelected = false;

        TagFilters.Clear();
        TagFilters.Add(new TagFilterItem
        {
            Name = "All",
            Count = Entries.Count,
            IsAll = true,
            IsSelected = selectedTags.Count == 0 && !noTagsSelected
        });

        if (noTagsCount > 0)
        {
            TagFilters.Add(new TagFilterItem
            {
                Name = "No tags",
                Count = noTagsCount,
                IsNoTags = true,
                IsSelected = noTagsSelected
            });
        }

        var orderedTags = _settings.TagSortMode switch
        {
            TagSortMode.Alphabetical => counts
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.Ordinal),
            _ => counts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
        };

        foreach (var kv in orderedTags)
        {
            TagFilters.Add(new TagFilterItem
            {
                Name = labels[kv.Key],
                Count = kv.Value,
                IsSelected = !noTagsSelected && selectedTags.Contains(kv.Key)
            });
        }

        // Hide strip when there are no real tags (only the synthetic "All" chip)
        ShowTagFilters = TagFilters.Count > 1;
    }

    [RelayCommand]
    private void SelectTagFilter(TagFilterItem? item)
    {
        if (item is null) return;

        if (item.IsAll)
        {
            _selectedTagFilters.Clear();
            IsNoTagsFilterSelected = false;
        }
        else if (item.IsNoTags)
        {
            _selectedTagFilters.Clear();
            IsNoTagsFilterSelected = !IsNoTagsFilterSelected;
        }
        else
        {
            IsNoTagsFilterSelected = false;

            if (!_selectedTagFilters.Add(item.Name))
                _selectedTagFilters.Remove(item.Name);
        }

        foreach (var chip in TagFilters)
        {
            chip.IsSelected = chip.IsAll
                ? _selectedTagFilters.Count == 0 && !IsNoTagsFilterSelected
                : chip.IsNoTags
                    ? IsNoTagsFilterSelected
                    : !IsNoTagsFilterSelected && _selectedTagFilters.Contains(chip.Name);
        }

        ApplyFilter();
        ResetAutoLockTimer();
    }
}

public partial class TagFilterItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public bool IsAll { get; init; }
    public bool IsNoTags { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string DisplayLabel => $"{Name} ({Count})";
}
