using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly VaultSession _vaultSession;
    private readonly VaultService _vaultService;

    public ObservableCollection<HistoryRow> Items { get; } = [];

    private HistoryRow? _selectedItem;

    public HistoryRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                OnPropertyChanged(nameof(SelectedSnapshot));
        }
    }

    public EntryHistoryItem? SelectedSnapshot => SelectedItem?.Snapshot;

    public event EventHandler? HistoryRestored;

    public HistoryViewModel(
        VaultSession vaultSession,
        VaultService vaultService)
    {
        _vaultSession = vaultSession;
        _vaultService = vaultService;
        Load();
    }

    private void Load()
    {
        Items.Clear();

        var vault = _vaultSession.Vault;
        if (vault is null)
            return;

        foreach (var history in vault.History
                     .OrderByDescending(h => h.ChangedAt))
        {
            var entry = vault.Entries.FirstOrDefault(e => e.Id == history.EntryId);

            Items.Add(new HistoryRow(
                history,
                entry?.Title ?? "(Deleted entry)"));
        }
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedItem is null)
            return;

        var vault = _vaultSession.Vault;
        if (vault is null)
            return;

        var entry = vault.Entries
            .FirstOrDefault(e => e.Id == SelectedItem.EntryId);

        var title = entry?.Title ?? SelectedItem.Title;

        var confirmed = DialogService.Confirm(
            $"Restore this version of \"{title}\"?",
            "Restore history",
            yesText: "Restore",
            noText: "Cancel");

        if (!confirmed)
            return;

        if (!vault.RestoreHistory(SelectedItem.Snapshot))
        {
            DialogService.Warning(
                "The entry could not be restored.",
                "Restore history");
            return;
        }

        var path = _vaultSession.VaultPath;

        if (string.IsNullOrEmpty(path))
        {
            DialogService.Warning(
                "The vault path is unavailable.",
                "Restore history");
            return;
        }

        try
        {
            await _vaultService.SaveVaultAsync(
                path,
                _vaultSession.GetSecret(),
                vault);
        }
        catch (Exception ex)
        {
            DialogService.Error(
                $"Failed to save the restored entry.\n\n{ex.Message}",
                "Restore history");

            return;
        }

        Load();
        SelectedItem = null;
        HistoryRestored?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class HistoryRow(
    EntryHistoryItem snapshot,
    string title)
{
    public Guid EntryId { get; } = snapshot.EntryId;
    public string Title { get; } = title;
    public string Username { get; } = snapshot.Username;
    public string Url { get; } = snapshot.Url;
    public string ChangedAtText { get; } = snapshot.ChangedAt
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm");
    public EntryHistoryItem Snapshot { get; } = snapshot;
}
