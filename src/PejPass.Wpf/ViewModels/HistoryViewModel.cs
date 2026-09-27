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

    public ObservableCollection<HistoryFieldRow> Fields { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string BusyMessage { get; set; } = string.Empty;

    private HistoryRow? _selectedItem;

    public HistoryRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(SelectedSnapshot));
                OnPropertyChanged(nameof(HasSelectedItem));

                BuildComparison();

                OnPropertyChanged(nameof(HasSelectedFields));
            }
        }
    }

    public bool HasSelectedItem => SelectedItem is not null;

    public bool HasHistoryItems => Items.Count > 0;

    public EntryHistoryItem? SelectedSnapshot =>
        SelectedItem?.Snapshot;

    public bool HasSelectedFields =>
    Fields.Any(row =>
        row.IsChanged &&
        row.IsSelected);

    public event EventHandler? HistoryRestored;

    public HistoryViewModel(
        VaultSession vaultSession,
        VaultService vaultService)
    {
        _vaultSession = vaultSession;
        _vaultService = vaultService;

        Load();

        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasHistoryItems));
        };
    }

    private void Load()
    {
        Items.Clear();
        ClearFields();

        var vault = _vaultSession.Vault;

        if (vault is null)
        {
            SelectedItem = null;
            return;
        }

        var historiesByEntry = vault.History
            .GroupBy(h => h.EntryId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(h => h.ChangedAt)
                    .ToList());

        foreach (var history in vault.History
                     .OrderByDescending(h => h.ChangedAt))
        {
            var entry = vault.Entries
                .FirstOrDefault(e => e.Id == history.EntryId);

            var historyList = historiesByEntry[history.EntryId];
            var index = historyList.FindIndex(h => h.Id == history.Id);
            var newerSnapshot = index > 0 ? historyList[index - 1] : null;

            var changedFields = GetChangedFields(
                history,
                newerSnapshot,
                index == 0 ? entry : null);

            Items.Add(new HistoryRow(
                history,
                entry?.Title ?? "(Deleted entry)",
                changedFields));
        }

        SelectedItem = null;
    }

    private static IReadOnlyList<string> GetChangedFields(
        EntryHistoryItem snapshot,
        EntryHistoryItem? newerSnapshot,
        VaultEntry? currentEntry)
    {
        var newer = newerSnapshot;
        var changed = new List<string>();

        if (newer is not null)
        {
            AddIfChanged(changed, "Title", snapshot.Title, newer.Title);
            AddIfChanged(changed, "Username", snapshot.Username, newer.Username);
            AddIfChanged(changed, "Password", snapshot.Password, newer.Password);
            AddIfChanged(changed, "URL", snapshot.Url, newer.Url);
            AddIfChanged(changed, "TOTP", snapshot.TotpSecret, newer.TotpSecret);
            AddIfChanged(changed, "Notes", snapshot.Notes, newer.Notes);
            AddIfChanged(changed, "Tags", FormatTags(snapshot.Tags), FormatTags(newer.Tags));
            AddIfChanged(
                changed,
                "Custom Fields",
                FormatCustomFields(snapshot.CustomFields),
                FormatCustomFields(newer.CustomFields));
        }
        else if (currentEntry is not null)
        {
            AddIfChanged(changed, "Title", snapshot.Title, currentEntry.Title);
            AddIfChanged(changed, "Username", snapshot.Username, currentEntry.Username);
            AddIfChanged(changed, "Password", snapshot.Password, currentEntry.Password);
            AddIfChanged(changed, "URL", snapshot.Url, currentEntry.Url);
            AddIfChanged(changed, "TOTP", snapshot.TotpSecret, currentEntry.TotpSecret);
            AddIfChanged(changed, "Notes", snapshot.Notes, currentEntry.Notes);
            AddIfChanged(changed, "Tags", FormatTags(snapshot.Tags), FormatTags(currentEntry.Tags));
            AddIfChanged(
                changed,
                "Custom Fields",
                FormatCustomFields(snapshot.CustomFields),
                FormatCustomFields(currentEntry.CustomFields));
        }

        return changed;
    }

    private static void AddIfChanged(
        List<string> changed,
        string name,
        string olderValue,
        string newerValue)
    {
        if (!string.Equals(olderValue, newerValue, StringComparison.Ordinal))
            changed.Add(name);
    }

    private void BuildComparison()
    {
        Fields.Clear();

        var snapshot = SelectedItem?.Snapshot;

        if (snapshot is null)
        {
            OnPropertyChanged(nameof(HasSelectedFields));
            return;
        }

        var vault = _vaultSession.Vault;

        if (vault is null)
        {
            OnPropertyChanged(nameof(HasSelectedFields));
            return;
        }

        var current = vault.FindEntry(snapshot.EntryId);

        if (current is null)
        {
            OnPropertyChanged(nameof(HasSelectedFields));
            return;
        }

        AddField(
            EntryHistoryField.Title,
            "Title",
            current.Title,
            snapshot.Title);

        AddField(
            EntryHistoryField.Username,
            "Username",
            current.Username,
            snapshot.Username);

        AddField(
            EntryHistoryField.Password,
            "Password",
            current.Password,
            snapshot.Password,
            isSecret: true);

        AddField(
            EntryHistoryField.Url,
            "URL",
            current.Url,
            snapshot.Url);

        AddField(
            EntryHistoryField.TotpSecret,
            "TOTP",
            current.TotpSecret,
            snapshot.TotpSecret,
            isSecret: true);

        AddField(
            EntryHistoryField.Notes,
            "Notes",
            current.Notes,
            snapshot.Notes);

        AddField(
            EntryHistoryField.Tags,
            "Tags",
            FormatTags(current.Tags),
            FormatTags(snapshot.Tags));

        AddField(
            EntryHistoryField.CustomFields,
            "Custom Fields",
            FormatCustomFields(current.CustomFields),
            FormatCustomFields(snapshot.CustomFields));

        OnPropertyChanged(nameof(HasSelectedFields));
    }

    private void AddField(
        EntryHistoryField field,
        string name,
        string currentValue,
        string snapshotValue,
        bool isSecret = false)
    {
        var isChanged = !string.Equals(
            currentValue,
            snapshotValue,
            StringComparison.Ordinal);

        var row = new HistoryFieldRow(
            field,
            name,
            currentValue,
            snapshotValue,
            isChanged,
            isSecret);

        row.PropertyChanged += OnFieldPropertyChanged;

        Fields.Add(row);
    }

    private void OnFieldPropertyChanged(
    object? sender,
    System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryFieldRow.IsSelected))
            OnPropertyChanged(nameof(HasSelectedFields));
    }

    private void ClearFields()
    {
        foreach (var field in Fields)
            field.PropertyChanged -= OnFieldPropertyChanged;

        Fields.Clear();
    }

    private static string MaskSecret(string value)
    {
        return string.IsNullOrEmpty(value)
            ? "(empty)"
            : "••••••••";
    }

    private static string FormatTags(
        List<string> tags)
    {
        return tags.Count == 0
            ? "(none)"
            : string.Join(", ", tags);
    }

    private static string FormatCustomFields(
        List<CustomField> fields)
    {
        if (fields.Count == 0)
            return "(none)";

        return string.Join(
            Environment.NewLine,
            fields.Select(field =>
                field.IsSecret
                    ? $"{field.Name}: ••••••••"
                    : $"{field.Name}: {field.Value}"));
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedItem is null)
            return;

        var selectedFields = Fields
            .Where(row =>
                row.IsChanged &&
                row.IsSelected)
            .Select(row => row.Field)
            .ToList();

        if (selectedFields.Count == 0)
            return;

        var vault = _vaultSession.Vault;

        if (vault is null)
            return;

        var entry = vault.FindEntry(
            SelectedItem.EntryId);

        if (entry is null)
        {
            DialogService.Warning(
                "The entry could not be found.",
                "Restore history");

            return;
        }

        var title = entry.Title;

        var message = selectedFields.Count == 1
            ? $"Restore the selected field of \"{title}\"?"
            : $"Restore {selectedFields.Count} selected fields of \"{title}\"?";

        var confirmed = DialogService.Confirm(
            message,
            "Restore history",
            yesText: "Restore",
            noText: "Cancel");

        if (!confirmed)
            return;

        if (!vault.RestoreHistoryFields(
                SelectedItem.Snapshot,
                selectedFields))
        {
            DialogService.Warning(
                "The selected fields could not be restored.",
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
            IsBusy = true;
            BusyMessage = "Restoring history...";

            await _vaultService.SaveVaultAsync(
                path,
                _vaultSession.GetSecret(),
                vault);
        }
        catch (Exception ex)
        {
            try
            {
                var restoredVault =
                    await _vaultService.OpenVaultAsync(
                        path,
                        _vaultSession.GetSecret());

                _vaultSession.ReplaceVault(restoredVault);
                Load();

                HistoryRestored?.Invoke(
                    this,
                    EventArgs.Empty);
            }
            catch
            {
                // Keep the original save error.
            }

            DialogService.Error(
                $"Failed to save the restored fields.\n\n{ex.Message}",
                "Restore history");

            return;
        }
        finally
        {
            IsBusy = false;
        }

        Load();

        HistoryRestored?.Invoke(
            this,
            EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeleteHistoryAsync()
    {
        if (SelectedItem is null)
            return;

        var confirmed = DialogService.Confirm(
            "Delete this history snapshot?",
            "Delete history",
            yesText: "Delete",
            noText: "Cancel");

        if (!confirmed)
            return;

        var vault = _vaultSession.Vault;

        if (vault is null)
            return;

        if (!vault.RemoveHistory(SelectedItem.HistoryId))
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Deleting history...";
            await SaveAsync();
        }
        finally
        {
            IsBusy = false;
        }

        Load();

        SelectedItem = null;
    }

    [RelayCommand]
    private async Task DeleteAllHistoryAsync()
    {
        var vault = _vaultSession.Vault;

        if (vault is null || vault.History.Count == 0)
            return;

        var confirmed = DialogService.Confirm(
            "Delete all history snapshots?\n\nThis action cannot be undone.",
            "Delete all history",
            yesText: "Delete All",
            noText: "Cancel");

        if (!confirmed)
            return;

        vault.ClearHistory();

        try
        {
            IsBusy = true;
            BusyMessage = "Deleting all history...";
            await SaveAsync();
        }
        finally
        {
            IsBusy = false;
        }

        Load();

        SelectedItem = null;
    }

    private async Task SaveAsync()
    {
        var path = _vaultSession.VaultPath;

        if (string.IsNullOrEmpty(path))
            return;

        await _vaultService.SaveVaultAsync(
            path,
            _vaultSession.GetSecret(),
            _vaultSession.Vault!);
    }
}

public sealed class HistoryRow(
    EntryHistoryItem snapshot,
    string title,
    IReadOnlyList<string> changedFields)
{
    public Guid EntryId { get; } = snapshot.EntryId;

    public Guid HistoryId { get; } = snapshot.Id;

    public string Title { get; } = title;

    public string Username { get; } = snapshot.Username;

    public string Url { get; } = snapshot.Url;

    public string ChangedAtText { get; } = FormatChangedAt(snapshot.ChangedAt);

    public IReadOnlyList<string> ChangedFields { get; } = changedFields;

    public bool IsExpanded { get; set; }

    public string ChangeSummary { get; } = changedFields.Count switch
    {
        0 => "No field changes detected",
        1 => $"{changedFields[0]} changed",
        _ => $"{changedFields.Count} fields changed"
    };

    public EntryHistoryItem Snapshot { get; } = snapshot;

    private static string FormatChangedAt(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var date = local.Date;

        if (date == today)
            return $"Today · {local:HH:mm}";

        if (date == today.AddDays(-1))
            return $"Yesterday · {local:HH:mm}";

        return local.Year == DateTimeOffset.Now.Year
            ? local.ToString("MMM d · HH:mm")
            : local.ToString("MMM d, yyyy · HH:mm");
    }
}

public partial class HistoryFieldRow(
    EntryHistoryField field,
    string name,
    string currentValue,
    string snapshotValue,
    bool isChanged,
    bool isSecret) : ObservableObject
{
    public EntryHistoryField Field { get; } = field;

    public string Name { get; } = name;

    public string CurrentValue { get; } = currentValue;

    public string SnapshotValue { get; } = snapshotValue;

    public bool IsChanged { get; } = isChanged;

    public bool IsSecret { get; } = isSecret;

    [ObservableProperty]
    public partial bool IsSecretVisible { get; set; }

    public string CurrentDisplayValue =>
        IsSecret && !IsSecretVisible
            ? MaskSecret(CurrentValue)
            : string.IsNullOrEmpty(CurrentValue)
                ? "(empty)"
                : CurrentValue;

    public string SnapshotDisplayValue =>
        IsSecret && !IsSecretVisible
            ? MaskSecret(SnapshotValue)
            : string.IsNullOrEmpty(SnapshotValue)
                ? "(empty)"
                : SnapshotValue;

    partial void OnIsSecretVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentDisplayValue));
        OnPropertyChanged(nameof(SnapshotDisplayValue));
    }

    [RelayCommand]
    private void ToggleSecretVisibility()
    {
        if (IsSecret)
            IsSecretVisible = !IsSecretVisible;
    }

    private static string MaskSecret(string value)
    {
        return string.IsNullOrEmpty(value)
            ? "(empty)"
            : "••••••••";
    }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
