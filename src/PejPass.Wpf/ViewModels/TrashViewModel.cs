using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class TrashViewModel : ObservableObject
{
    private readonly Vault _vault;
    private readonly List<TrashRow> _all = [];
    private readonly Func<Task<bool>> _ensureWritable;
    private readonly Func<Task<bool>> _saveVault;
    private readonly Func<VaultEntry, bool> _isDuplicate;

    public ObservableCollection<TrashRow> Items { get; } = [];

    public bool IsTrashEmpty => _all.Count == 0;
    public bool HasNoSearchResults =>
        _all.Count > 0 &&
        Items.Count == 0 &&
        !string.IsNullOrWhiteSpace(SearchText);

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public event EventHandler? Changed;

    public TrashViewModel(
        Vault vault,
        Func<Task<bool>> ensureWritable,
        Func<Task<bool>> saveVault,
        Func<VaultEntry, bool> isDuplicate)
    {
        _vault = vault;
        _ensureWritable = ensureWritable;
        _saveVault = saveVault;
        _isDuplicate = isDuplicate;
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

        OnPropertyChanged(nameof(IsTrashEmpty));
        OnPropertyChanged(nameof(HasNoSearchResults));

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
    private async Task RestoreSelectedAsync(IReadOnlyList<TrashRow>? selectedRows)
    {
        var selected = selectedRows?
            .Where(row => _all.Any(item => item.EntryId == row.EntryId))
            .DistinctBy(row => row.EntryId)
            .ToList();

        if (selected is not { Count: > 0 } || !await _ensureWritable())
            return;

        var snapshot = _vault.CreateSnapshot();
        var restoredIds = new HashSet<Guid>();
        var duplicates = 0;

        foreach (var row in selected)
        {
            var entry = _vault.Trash.FirstOrDefault(t => t.Entry.Id == row.EntryId)?.Entry;
            if (entry is null)
                continue;

            if (_isDuplicate(entry))
            {
                duplicates++;
                continue;
            }

            if (_vault.RestoreFromTrash(row.EntryId))
                restoredIds.Add(row.EntryId);
        }

        if (restoredIds.Count == 0)
        {
            StatusMessage = duplicates > 0
                ? $"Skipped {duplicates} duplicate item(s); they remain in Trash."
                : "No selected items could be restored.";

            return;
        }

        if (!await SaveAndRollbackAsync(snapshot))
            return;

        _all.RemoveAll(row => restoredIds.Contains(row.EntryId));
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);

        StatusMessage = duplicates > 0
            ? $"Restored {restoredIds.Count}; skipped {duplicates} duplicate item(s)."
            : $"Restored {restoredIds.Count} item(s).";

        SnackbarService.Show(duplicates > 0
            ? $"Restored {restoredIds.Count}; skipped {duplicates} duplicate item(s)."
            : $"Restored {restoredIds.Count} item(s).");
    }

    [RelayCommand]
    private async Task RestoreAsync(TrashRow? row)
    {
        if (row is null || !await _ensureWritable()) return;

        var trashedEntry = _vault.Trash
            .FirstOrDefault(t => t.Entry.Id == row.EntryId)?.Entry;

        if (trashedEntry is null)
            return;

        if (_isDuplicate(trashedEntry))
        {
            DialogService.Warning(
                "An identical entry already exists in your list. The item will remain in Trash.",
                "Duplicate entry");
            return;
        }

        var snapshot = _vault.CreateSnapshot();

        if (!_vault.RestoreFromTrash(row.EntryId))
            return;

        if (!await SaveAndRollbackAsync(snapshot))
            return;

        _all.RemoveAll(r => r.EntryId == row.EntryId);
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = $"Restored \"{row.Title}\".";
        if (_all.Count == 0)
            StatusMessage = "Trash is empty.";
    }

    [RelayCommand]
    private async Task PurgeAsync(TrashRow? row)
    {
        if (row is null) return;

        if (!DialogService.Confirm(
                $"This entry will be permanently deleted and cannot be recovered.\n\n\"{row.Title}\"",
                "Delete permanently",
                yesText: "Delete permanently",
                noText: "Cancel"))
            return;

        if (!await _ensureWritable())
            return;

        var snapshot = _vault.CreateSnapshot();

        if (!_vault.PurgeFromTrash(row.EntryId))
            return;

        if (!await SaveAndRollbackAsync(snapshot))
            return;

        _all.RemoveAll(r => r.EntryId == row.EntryId);
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        if (_all.Count == 0)
            StatusMessage = "Trash is empty.";

        SnackbarService.Show("Permanently deleted entry.");
    }

    [RelayCommand]
    private async Task PurgeSelectedAsync(IReadOnlyList<TrashRow>? selectedRows)
    {
        var selected = selectedRows?
            .Where(row => _all.Any(item => item.EntryId == row.EntryId))
            .DistinctBy(row => row.EntryId)
            .ToList();

        if (selected is not { Count: > 0 })
            return;

        if (!DialogService.Confirm(
                $"Permanently delete {selected.Count} selected item(s)? This cannot be undone.",
                "Delete selected items permanently",
                yesText: "Delete permanently",
                noText: "Cancel"))
            return;

        if (!await _ensureWritable())
            return;

        var snapshot = _vault.CreateSnapshot();
        var purgedIds = new HashSet<Guid>();

        foreach (var row in selected)
        {
            if (_vault.PurgeFromTrash(row.EntryId))
                purgedIds.Add(row.EntryId);
        }

        if (!await SaveAndRollbackAsync(snapshot))
            return;

        _all.RemoveAll(row => purgedIds.Contains(row.EntryId));
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = _all.Count == 0
            ? "Trash is empty."
            : $"Permanently deleted {purgedIds.Count} item(s).";
        SnackbarService.Show($"Permanently deleted {purgedIds.Count} item(s).");
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        if (_all.Count == 0) return;

        if (!DialogService.Confirm(
                $"All {_all.Count} item(s) in Trash will be permanently deleted and cannot be recovered.",
                "Empty Trash permanently",
                yesText: "Delete all permanently",
                noText: "Cancel"))
            return;

        if (!await _ensureWritable())
            return;

        var snapshot = _vault.CreateSnapshot();

        _vault.EmptyTrash();

        if (!await SaveAndRollbackAsync(snapshot))
            return;

        _all.Clear();
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
        StatusMessage = "Trash is empty.";
        SnackbarService.Show("Trash emptied.");
    }

    private async Task<bool> SaveAndRollbackAsync(Vault snapshot)
    {
        if (await _saveVault())
            return true;

        _vault.RestoreSnapshot(snapshot);
        Reload();
        return false;
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
