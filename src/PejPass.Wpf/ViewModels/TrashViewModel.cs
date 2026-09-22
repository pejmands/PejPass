using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private readonly Vault _vault;
    private readonly List<TrashRow> _all = [];

    public ObservableCollection<TrashRow> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public event EventHandler? Changed;

    public TrashViewModel(Vault vault)
    {
        _vault = vault;
        Reload();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void Reload()
    {
        _all.Clear();
        foreach (var t in _vault.Trash.OrderByDescending(x => x.DeletedAt))
            _all.Add(new TrashRow(t));

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var q = SearchText?.Trim() ?? string.Empty;

        IEnumerable<TrashRow> source = _all;
        if (!string.IsNullOrEmpty(q))
        {
            source = _all.Where(r =>
                r.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Url.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in source)
            Items.Add(row);

        if (_all.Count == 0)
            StatusMessage = "Trash is empty.";
        else if (!string.IsNullOrEmpty(q))
            StatusMessage = $"{Items.Count} of {_all.Count} item(s)";
        else
            StatusMessage = $"{_all.Count} item(s) — recoverable for 30 days.";
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void Restore(TrashRow? row)
    {
        if (row is null) return;

        if (!_vault.RestoreFromTrash(row.EntryId))
            return;

        _all.RemoveAll(r => r.EntryId == row.EntryId);
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = $"Restored \"{row.Title}\".";
        if (_all.Count == 0)
            StatusMessage = "Trash is empty.";
    }

    [RelayCommand]
    private void Purge(TrashRow? row)
    {
        if (row is null) return;

        if (!DialogService.Confirm(
                $"Permanently delete \"{row.Title}\"?\n\nThis cannot be undone.",
                "Purge",
                yesText: "Purge",
                noText: "Cancel"))
            return;

        if (!_vault.PurgeFromTrash(row.EntryId))
            return;

        _all.RemoveAll(r => r.EntryId == row.EntryId);
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        if (_all.Count == 0)
            StatusMessage = "Trash is empty.";
    }

    [RelayCommand]
    private void EmptyTrash()
    {
        if (_all.Count == 0) return;

        if (!DialogService.Confirm(
                $"Permanently delete all {_all.Count} item(s) in Trash?\n\nThis cannot be undone.",
                "Empty Trash",
                yesText: "Empty Trash",
                noText: "Cancel"))
            return;

        _vault.EmptyTrash();
        _all.Clear();
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = "Trash is empty.";
    }
}

public sealed class TrashRow
{
    public Guid EntryId { get; }
    public string Title { get; }
    public string Username { get; }
    public string Url { get; }
    public string DeletedAtText { get; }
    public string DaysLeftText { get; }

    public TrashRow(TrashedEntry item)
    {
        EntryId = item.Entry.Id;
        Title = item.Entry.Title;
        Username = item.Entry.Username;
        Url = item.Entry.Url;
        DeletedAtText = item.DeletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        var expires = item.DeletedAt + Vault.TrashRetention;
        var left = expires - DateTimeOffset.UtcNow;
        DaysLeftText = left.TotalDays <= 0
            ? "Expiring"
            : $"{(int)Math.Ceiling(left.TotalDays)}d left";
    }
}
