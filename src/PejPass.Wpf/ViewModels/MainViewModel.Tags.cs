using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    /// <summary>null / empty = show all entries.</summary>
    public string? SelectedTagFilter { get; private set; }

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
        var selected = SelectedTagFilter;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        // Drop filter if that tag no longer exists
        if (!string.IsNullOrEmpty(selected) && !counts.ContainsKey(selected))
            SelectedTagFilter = selected = null;

        TagFilters.Clear();
        TagFilters.Add(new TagFilterItem
        {
            Name = "All",
            Count = Entries.Count,
            IsAll = true,
            IsSelected = string.IsNullOrEmpty(selected)
        });

        foreach (var kv in counts
                     .OrderByDescending(x => x.Value)
                     .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            TagFilters.Add(new TagFilterItem
            {
                Name = labels[kv.Key],
                Count = kv.Value,
                IsSelected = selected is not null &&
                             selected.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)
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
            SelectedTagFilter = null;
        else if (SelectedTagFilter is not null &&
                 SelectedTagFilter.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
            SelectedTagFilter = null; // click again clears
        else
            SelectedTagFilter = item.Name;

        foreach (var chip in TagFilters)
            chip.IsSelected = chip.IsAll
                ? string.IsNullOrEmpty(SelectedTagFilter)
                : SelectedTagFilter is not null &&
                  chip.Name.Equals(SelectedTagFilter, StringComparison.OrdinalIgnoreCase);

        ApplyFilter();
        ResetAutoLockTimer();
    }
}

public partial class TagFilterItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public bool IsAll { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string DisplayLabel => IsAll ? $"All ({Count})" : $"{Name} ({Count})";
}
