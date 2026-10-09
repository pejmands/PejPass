using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private bool _isDisposed;
    private readonly VaultSession _vaultSession;
    private readonly VaultService _vaultService;
    private readonly AppSettings _settings;

    public ObservableCollection<HistoryRow> Items { get; } = [];

    public ObservableCollection<HistoryRow> FilteredItems { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

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

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilteredItems();
    }

    private bool MatchesSearch(HistoryRow row)
    {
        return row.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.Username.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilteredItems()
    {
        FilteredItems.Clear();

        foreach (var item in Items.Where(MatchesSearch))
            FilteredItems.Add(item);

        OnPropertyChanged(nameof(HasFilteredItems));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    public bool HasHistoryItems => Items.Count > 0;

    public bool HasFilteredItems => FilteredItems.Count > 0;

    public bool HasNoSearchResults =>
        Items.Count > 0 &&
        FilteredItems.Count == 0 &&
        !string.IsNullOrEmpty(SearchText);

    public EntryHistoryItem? SelectedSnapshot =>
        SelectedItem?.Snapshot;

    public bool HasSelectedFields =>
    Fields.Any(row =>
        row.IsChanged &&
        row.IsSelected);

    public event EventHandler? HistoryRestored;

    public HistoryViewModel(
        VaultSession vaultSession,
        VaultService vaultService,
        AppSettings settings)
    {
        _vaultSession = vaultSession;
        _vaultService = vaultService;
        _settings = settings;

        Load();

        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasHistoryItems));
            RefreshFilteredItems();
        };

        RefreshFilteredItems();
    }

    private void Load()
    {
        if (_isDisposed)
            return;

        Items.Clear();
        ClearFields();

        var vault = _vaultSession.Vault;

        if (vault is null)
        {
            SelectedItem = null;
            return;
        }

        var trashedEntryIds = vault.Trash
            .Select(t => t.Entry.Id)
            .ToHashSet();

        var visibleHistory = vault.History
            .Where(h => !trashedEntryIds.Contains(h.EntryId))
            .ToList();

        var historiesByEntry = visibleHistory
            .GroupBy(h => h.EntryId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(h => h.ChangedAt)
                    .ToList());

        foreach (var history in visibleHistory
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

    private static List<string> GetChangedFields(
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
            isSecret,
            _settings.RevealSecretSeconds);

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

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        ClearFields();
        SelectedItem = null;
        Items.Clear();
        FilteredItems.Clear();
        SearchText = string.Empty;
        BusyMessage = string.Empty;
        HistoryRestored = null;
    }

    private void ClearFields()
    {
        foreach (var field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
            field.Dispose();
        }

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

        if (!await EnsureVaultWritableAsync())
            return;

        var snapshot = vault.CreateSnapshot();

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

            using var keyMaterial = _vaultSession.CopyKeyMaterial();
            await _vaultService.SaveVaultAsync(
                path,
                keyMaterial,
                vault);
        }
        catch (Exception)
        {
            _vaultSession.Vault!.RestoreSnapshot(snapshot);
            Load();

            DialogService.Error(
                "Failed to save the restored fields. Please try again.",
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

        if (!await EnsureVaultWritableAsync())
            return;

        var snapshot = vault.CreateSnapshot();

        if (!vault.RemoveHistory(SelectedItem.HistoryId))
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Deleting history...";

            if (!await SaveAsync(snapshot, "Delete history"))
                return;
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

        if (!await EnsureVaultWritableAsync())
            return;

        var snapshot = vault.CreateSnapshot();
        vault.ClearHistory();

        try
        {
            IsBusy = true;
            BusyMessage = "Deleting all history...";

            if (!await SaveAsync(snapshot, "Delete all history"))
                return;
        }
        finally
        {
            IsBusy = false;
        }

        Load();

        SelectedItem = null;
    }

    private async Task<bool> EnsureVaultWritableAsync()
    {
        var path = _vaultSession.VaultPath;

        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            await _vaultService.EnsureVaultWritableAsync(path);
            return true;
        }
        catch (Exception)
        {
            DialogService.Error(
                "The vault cannot be modified right now. Check file permissions and try again.",
                "Vault unavailable");

            return false;
        }
    }

    private async Task<bool> SaveAsync(Vault snapshot, string actionTitle)
    {
        var path = _vaultSession.VaultPath;

        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            using var keyMaterial = _vaultSession.CopyKeyMaterial();
            await _vaultService.SaveVaultAsync(
                path,
                keyMaterial,
                _vaultSession.Vault!);

            return true;
        }
        catch (Exception)
        {
            _vaultSession.Vault!.RestoreSnapshot(snapshot);
            Load();

            DialogService.Error(
                "Failed to save the history changes. Please try again.",
                actionTitle);

            return false;
        }
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
    bool isSecret,
    int revealSecretSeconds) : ObservableObject
{
    private readonly SecretRevealTimer _revealTimer = new();

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
        if (!IsSecret)
            return;

        if (IsSecretVisible)
        {
            _revealTimer.Stop();
            IsSecretVisible = false;
            return;
        }

        IsSecretVisible = true;
        _revealTimer.Start(
            revealSecretSeconds,
            () => IsSecretVisible = false);
    }

    public void Dispose()
    {
        _revealTimer.Dispose();
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
