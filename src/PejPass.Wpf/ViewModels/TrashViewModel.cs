using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private readonly Vault _vault;

    public ObservableCollection<TrashRow> Items { get; } = [];

    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    public event EventHandler? Changed;

    public TrashViewModel(Vault vault)
    {
        _vault = vault;
        Reload();
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var t in _vault.Trash.OrderByDescending(x => x.DeletedAt))
            Items.Add(new TrashRow(t));

        StatusMessage = Items.Count == 0
            ? "Trash is empty."
            : $"{Items.Count} item(s) — recoverable for 30 days.";
    }

    [RelayCommand]
    private void Restore(TrashRow? row)
    {
        if (row is null) return;

        if (!_vault.RestoreFromTrash(row.EntryId))
            return;

        Items.Remove(row);
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = $"Restored \"{row.Title}\".";
        if (Items.Count == 0)
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

        Items.Remove(row);
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = Items.Count == 0
            ? "Trash is empty."
            : $"{Items.Count} item(s) remaining.";
    }

    [RelayCommand]
    private void EmptyTrash()
    {
        if (Items.Count == 0) return;

        if (!DialogService.Confirm(
                $"Permanently delete all {Items.Count} item(s) in Trash?\n\nThis cannot be undone.",
                "Empty Trash",
                yesText: "Empty Trash",
                noText: "Cancel"))
            return;

        _vault.EmptyTrash();
        Items.Clear();
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
