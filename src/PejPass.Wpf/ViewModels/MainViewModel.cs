using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Infrastructure.Totp;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VaultService _vaultService;
    private readonly IClipboardService _clipboard;
    private readonly IBrowserImportService _importService;
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private readonly VaultSession _vaultSession;
    private readonly SessionPasswordCache _sessionPasswordCache;

    private static readonly TimeSpan AutoLockActivityThrottle = TimeSpan.FromMilliseconds(500);
    private System.Timers.Timer? _autoLockTimer;
    private DispatcherTimer? _totpTimer;
    private bool _isPasswordVisible;
    private readonly SecretRevealTimer _passwordRevealTimer = new();
    private DateTime _lastAutoLockActivityUtc;

    public event EventHandler<LockRequestedEventArgs>? RequestLock;
    public event EventHandler? RequestScrollToEntry;

    [ObservableProperty]
    public partial string VaultName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string BusyMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasTotpFilter { get; set; }

    [ObservableProperty]
    public partial bool HasNotesFilter { get; set; }

    [ObservableProperty]
    public partial VaultEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool HasUrl { get; set; }

    [ObservableProperty]
    public partial bool HasTotp { get; set; }

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    [ObservableProperty]
    public partial bool HasCustomFields { get; set; }

    [ObservableProperty]
    public partial bool HasTags { get; set; }

    [ObservableProperty]
    public partial string DisplayPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShowPasswordButtonText { get; set; } = "Show";

    [ObservableProperty]
    public partial string CreatedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UpdatedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFavoriteSelected { get; set; }

    [ObservableProperty]
    public partial int SelectedSortIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedTagSortIndex { get; set; }

    [ObservableProperty]
    public partial string TotpCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotpCodeFormatted { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TotpRemainingSeconds { get; set; }

    [ObservableProperty]
    public partial double TotpProgress { get; set; }

    public string[] SortOptions { get; } =
    [
        "A → Z",
        "Z → A",
        "Newest",
        "Oldest",
        "Manual"
    ];

    public string[] TagSortOptions { get; } =
    [
        "Most used",
        "Alphabetical"
    ];

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public ObservableCollection<VaultEntry> FilteredEntries { get; } = [];
    public ObservableCollection<CustomFieldDisplayItem> DisplayCustomFields { get; } = [];

    public bool IsEntryListEmpty => FilteredEntries.Count == 0;
    public bool HasEntries => Entries.Count > 0;
    public bool HasContentFilter => HasTotpFilter || HasNotesFilter;
    public bool HasActiveEntryFilter =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        _selectedTagFilters.Count > 0 ||
        IsNoTagsFilterSelected ||
        HasContentFilter;

    public MainViewModel(
        VaultService vaultService,
        IClipboardService clipboard,
        IBrowserImportService importService,
        AppSettings settings,
        ThemeService themeService,
        VaultSession vaultSession,
        SessionPasswordCache sessionPasswordCache)
    {
        _vaultService = vaultService;
        _clipboard = clipboard;
        _importService = importService;
        _settings = settings;
        _themeService = themeService;
        _vaultSession = vaultSession;
        _sessionPasswordCache = sessionPasswordCache;
        _lastAutoLockActivityUtc = DateTime.UtcNow;
        InputManager.Current.PreProcessInput += OnPreProcessInput;

        SelectedSortIndex = (int)_settings.SortMode;
        SelectedTagSortIndex = (int)_settings.TagSortMode;

        _ = LoadVaultAsync();
        StartAutoLockTimer();
        StartTotpTimer();
    }

    partial void OnSelectedEntryChanged(VaultEntry? value)
    {
        _passwordRevealTimer.Stop();
        _isPasswordVisible = false;
        HasSelection = value is not null;
        HasUrl = !string.IsNullOrWhiteSpace(value?.Url);
        HasTotp = !string.IsNullOrWhiteSpace(value?.TotpSecret);
        HasNotes = !string.IsNullOrWhiteSpace(value?.Notes);
        HasCustomFields = value?.CustomFields is { Count: > 0 };
        HasTags = value?.Tags is { Count: > 0 };
        IsFavoriteSelected = value?.IsFavorite == true;

        if (value is not null)
        {
            CreatedAtText = value.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            UpdatedAtText = value.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            CreatedAtText = string.Empty;
            UpdatedAtText = string.Empty;
        }

        UpdatePasswordDisplay();
        RebuildDisplayCustomFields();
        RefreshTotp();
    }

    partial void OnSelectedSortIndexChanged(int value)
    {
        if (value < 0 || value > 4) return;
        _settings.SortMode = (EntrySortMode)value;
        SettingsStore.Save(_settings);
        ApplyFilter();
    }

    partial void OnSelectedTagSortIndexChanged(int value)
    {
        if (value < 0 || value > 1) return;
        _settings.TagSortMode = (TagSortMode)value;
        SettingsStore.Save(_settings);
        RebuildTagFilters();
    }

    private void RebuildDisplayCustomFields()
    {
        foreach (var item in DisplayCustomFields)
            item.Dispose();

        DisplayCustomFields.Clear();
        if (SelectedEntry?.CustomFields is null) return;

        foreach (var f in SelectedEntry.CustomFields)
            DisplayCustomFields.Add(new CustomFieldDisplayItem(f));
    }

    private void StartTotpTimer()
    {
        _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += (_, _) => RefreshTotp();
        _totpTimer.Start();
    }

    private void RefreshTotp()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.TotpSecret))
        {
            HasTotp = false;
            TotpCode = string.Empty;
            TotpCodeFormatted = string.Empty;
            TotpRemainingSeconds = 0;
            TotpProgress = 0;
            return;
        }

        if (TotpHelper.TryGetCode(SelectedEntry.TotpSecret, out var code, out var remaining))
        {
            HasTotp = true;
            TotpCode = code;
            TotpCodeFormatted = code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
            TotpRemainingSeconds = remaining;
            TotpProgress = remaining / (double)TotpHelper.StepSeconds;
        }
        else
        {
            HasTotp = true;
            TotpCode = string.Empty;
            TotpCodeFormatted = "Invalid secret";
            TotpRemainingSeconds = 0;
            TotpProgress = 0;
        }
    }

    private void UpdatePasswordDisplay()
    {
        if (SelectedEntry is null)
        {
            DisplayPassword = string.Empty;
            ShowPasswordButtonText = "Show";
            return;
        }

        if (_isPasswordVisible)
        {
            DisplayPassword = SelectedEntry.Password;
            ShowPasswordButtonText = "Hide";
        }
        else
        {
            DisplayPassword = string.IsNullOrEmpty(SelectedEntry.Password)
                ? string.Empty
                : new string('•', Math.Min(SelectedEntry.Password.Length, 16));
            ShowPasswordButtonText = "Show";
        }
    }

    private async Task LoadVaultAsync()
    {
        var vault = _vaultSession.Vault;
        if (vault is null)
            return;

        VaultName = vault.Name;
        Entries.Clear();

        foreach (var e in vault.Entries)
            Entries.Add(e);

        var purged = vault.PurgeExpiredTrash();

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: null);
        UpdateEntryStatus(purged > 0 ? $"purged {purged} expired trash item(s)" : null);

        if (purged == 0 || string.IsNullOrEmpty(_vaultSession.VaultPath))
            return;

        var ownsBusyState = !IsBusy;

        try
        {
            if (ownsBusyState)
            {
                IsBusy = true;
                BusyMessage = "Saving vault...";
            }

            await _vaultService.SaveVaultAsync(
                _vaultSession.VaultPath,
                _vaultSession.GetSecret(),
                vault);
        }
        catch (Exception)
        {
            DialogService.Error(
                "Failed to save expired trash cleanup. Please try again.",
                "Save failed");
        }
        finally
        {
            if (ownsBusyState)
                IsBusy = false;
        }
    }

    private void UpdateEntryStatus(string? extra = null)
    {
        var vault = _vaultSession.Vault;
        var trash = vault?.Trash.Count ?? 0;

        var baseMsg = trash > 0
            ? $"{Entries.Count} entries · {trash} in trash"
            : $"{Entries.Count} entries";

        StatusMessage = string.IsNullOrEmpty(extra)
            ? baseMsg
            : $"{baseMsg} · {extra}";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnHasTotpFilterChanged(bool value)
    {
        OnPropertyChanged(nameof(HasContentFilter));
        ApplyFilter();
    }

    partial void OnHasNotesFilterChanged(bool value)
    {
        OnPropertyChanged(nameof(HasContentFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearContentFilters()
    {
        HasTotpFilter = false;
        HasNotesFilter = false;
    }

    private void ApplyFilter(Guid? preserveSelectionId = null)
    {
        var keepId = preserveSelectionId ?? SelectedEntry?.Id;

        FilteredEntries.Clear();
        var q = SearchText?.Trim() ?? string.Empty;

        IEnumerable<VaultEntry> source = Entries;

        if (IsNoTagsFilterSelected)
        {
            source = source.Where(e =>
                !e.Tags.Any(t => !string.IsNullOrWhiteSpace(t)));
        }
        else if (_selectedTagFilters.Count > 0)
        {
            var selectedTags = _selectedTagFilters;
            source = source.Where(e =>
                e.Tags.Any(t => selectedTags.Contains(t.Trim())));
        }

        if (HasTotpFilter)
            source = source.Where(e => !string.IsNullOrWhiteSpace(e.TotpSecret));

        if (HasNotesFilter)
            source = source.Where(e => !string.IsNullOrWhiteSpace(e.Notes));

        if (!string.IsNullOrEmpty(q))
        {
            source = source.Where(e =>
                e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Url.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                e.CustomFields.Any(f =>
                    f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    f.Value.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        source = SortEntries(source);

        foreach (var e in source)
            FilteredEntries.Add(e);

        OnPropertyChanged(nameof(IsEntryListEmpty));
        OnPropertyChanged(nameof(HasActiveEntryFilter));
        OnPropertyChanged(nameof(HasEntries));

        if (keepId is { } id)
            SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == id);
    }

    public async Task MoveEntriesAsync(
        IReadOnlyList<VaultEntry> movingEntries,
        VaultEntry target,
        bool insertAfter)
    {
        if (_settings.SortMode != EntrySortMode.Manual ||
            movingEntries.Count == 0 ||
            !Entries.Any(entry => entry.Id == target.Id))
        {
            return;
        }

        var movingIds = movingEntries
            .Select(entry => entry.Id)
            .ToHashSet();

        if (movingIds.Contains(target.Id) ||
            movingEntries.Any(entry => entry.IsFavorite != target.IsFavorite) ||
            movingEntries.Any(entry => !FilteredEntries.Any(visible => visible.Id == entry.Id)))
        {
            return;
        }

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var snapshot = vault.CreateSnapshot();

        // Reorder only within the target's favorite group.
        var group = Entries
            .Where(entry => entry.IsFavorite == target.IsFavorite)
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var movingInOrder = group
            .Where(entry => movingIds.Contains(entry.Id))
            .ToList();

        if (movingInOrder.Count != movingIds.Count)
            return;

        group.RemoveAll(entry => movingIds.Contains(entry.Id));

        var targetIndex = group.FindIndex(entry => entry.Id == target.Id);
        if (targetIndex < 0)
            return;

        var insertIndex = targetIndex + (insertAfter ? 1 : 0);
        group.InsertRange(insertIndex, movingInOrder);

        for (var index = 0; index < group.Count; index++)
            group[index].SortOrder = index * 10;

        ApplyFilter(preserveSelectionId: SelectedEntry?.Id);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot, SelectedEntry?.Id);
            return;
        }

        SnackbarService.Show("Entry order updated.");
    }

    public async Task ResetManualOrderAsync(EntrySortMode sourceSortMode)
    {
        if (_settings.SortMode != EntrySortMode.Manual ||
            sourceSortMode == EntrySortMode.Manual ||
            Entries.Count == 0)
        {
            return;
        }

        var sourceName = sourceSortMode switch
        {
            EntrySortMode.TitleAsc => "A → Z",
            EntrySortMode.TitleDesc => "Z → A",
            EntrySortMode.NewestFirst => "Newest",
            EntrySortMode.OldestFirst => "Oldest",
            _ => "the selected order"
        };

        if (!DialogService.Confirm(
                $"Reset the manual order to {sourceName}?\n\nYour current manual order will be replaced.",
                "Reset Manual Order",
                yesText: "Reset",
                noText: "Cancel"))
        {
            return;
        }

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var snapshot = vault.CreateSnapshot();

        foreach (var isFavorite in new[] { true, false })
        {
            var group = Entries
                .Where(entry => entry.IsFavorite == isFavorite)
                .ToList();

            IEnumerable<VaultEntry> ordered = sourceSortMode switch
            {
                EntrySortMode.TitleAsc => group.OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase),
                EntrySortMode.TitleDesc => group.OrderByDescending(entry => entry.Title, StringComparer.OrdinalIgnoreCase),
                EntrySortMode.NewestFirst => group.OrderByDescending(entry => entry.CreatedAt),
                EntrySortMode.OldestFirst => group.OrderBy(entry => entry.CreatedAt),
                _ => group
            };

            var orderedEntries = ordered.ToList();

            for (var index = 0; index < orderedEntries.Count; index++)
                orderedEntries[index].SortOrder = index * 10;
        }

        ApplyFilter(preserveSelectionId: SelectedEntry?.Id);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot, SelectedEntry?.Id);
            return;
        }

        SnackbarService.Show("Manual order reset.");
    }

    public async Task MoveEntriesToEdgeAsync(
        IReadOnlyList<VaultEntry> movingEntries,
        bool isFavorite,
        bool moveToTop)
    {
        if (_settings.SortMode != EntrySortMode.Manual || movingEntries.Count == 0)
            return;

        var movingIds = movingEntries
            .Select(entry => entry.Id)
            .ToHashSet();

        if (movingIds.Count == 0 ||
            movingEntries.Any(entry => entry.IsFavorite != isFavorite) ||
            movingEntries.Any(entry => !Entries.Any(active => active.Id == entry.Id)))
        {
            return;
        }

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var snapshot = vault.CreateSnapshot();

        var group = Entries
            .Where(entry => entry.IsFavorite == isFavorite)
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var movingInOrder = group
            .Where(entry => movingIds.Contains(entry.Id))
            .ToList();

        if (movingInOrder.Count != movingIds.Count)
            return;

        group.RemoveAll(entry => movingIds.Contains(entry.Id));

        if (moveToTop)
            group.InsertRange(0, movingInOrder);
        else
            group.AddRange(movingInOrder);

        for (var index = 0; index < group.Count; index++)
            group[index].SortOrder = index * 10;

        ApplyFilter(preserveSelectionId: SelectedEntry?.Id);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot, SelectedEntry?.Id);
            return;
        }

        SnackbarService.Show(moveToTop ? "Entries moved to top." : "Entries moved to bottom.");
    }

    private IEnumerable<VaultEntry> SortEntries(IEnumerable<VaultEntry> source)
    {
        var mode = _settings.SortMode;

        return mode switch
        {
            EntrySortMode.TitleDesc =>
                source.OrderByDescending(e => e.IsFavorite)
                      .ThenByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),

            EntrySortMode.NewestFirst =>
                source.OrderByDescending(e => e.IsFavorite)
                      .ThenByDescending(e => e.CreatedAt),

            EntrySortMode.OldestFirst =>
                source.OrderByDescending(e => e.IsFavorite)
                      .ThenBy(e => e.CreatedAt),

            EntrySortMode.Manual =>
                source.OrderByDescending(e => e.IsFavorite)
                      .ThenBy(e => e.SortOrder)
                      .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase),

            _ =>
                source.OrderByDescending(e => e.IsFavorite)
                      .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (!_vaultSession.IsActive || _settings.AutoLockMinutes <= 0)
            return;

        var input = e.StagingItem.Input;
        var isActivity = input switch
        {
            KeyboardEventArgs keyboard => keyboard.RoutedEvent == Keyboard.KeyDownEvent,
            MouseButtonEventArgs => true,
            MouseWheelEventArgs => true,
            MouseEventArgs mouse => mouse.RoutedEvent == Mouse.MouseMoveEvent,
            _ => false
        };

        if (!isActivity)
            return;

        var now = DateTime.UtcNow;
        if (now - _lastAutoLockActivityUtc < AutoLockActivityThrottle)
            return;

        ResetAutoLockTimer();
    }

    private void StartAutoLockTimer()
    {
        if (_settings.AutoLockMinutes <= 0)
            return;

        _autoLockTimer = new System.Timers.Timer
        {
            Interval = _settings.AutoLockMinutes * 60_000,
            AutoReset = false
        };

        _autoLockTimer.Elapsed += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                Lock(ShouldShowLoginAfterAutoLock()));
        };

        _autoLockTimer.Start();
    }

    private static bool ShouldShowLoginAfterAutoLock()
    {
        var app = System.Windows.Application.Current;
        var window = app?.MainWindow;

        return window is not null &&
               window.IsVisible;
    }

    private void ResetAutoLockTimer()
    {
        if (!_vaultSession.IsActive || _settings.AutoLockMinutes <= 0)
            return;

        _lastAutoLockActivityUtc = DateTime.UtcNow;

        _autoLockTimer?.Stop();
        _autoLockTimer?.Start();
    }

    private static Window? GetOwnerWindow()
    {
        return System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
               ?? System.Windows.Application.Current?.MainWindow;
    }

    private IEnumerable<string> GetUsedTags()
    {
        return Entries
            .SelectMany(e => e.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim());
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null)
            return;

        var isFavorite = !entry.IsFavorite;

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var snapshot = vault.CreateSnapshot();

        if (!vault.SetFavorite(entry.Id, isFavorite))
            return;

        IsFavoriteSelected = isFavorite;

        ApplyFilter(preserveSelectionId: entry.Id);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot);
            return;
        }

        SnackbarService.Show(isFavorite
            ? "Added to favorites."
            : "Removed from favorites.");
    }

    [RelayCommand]
    private static void OpenAbout()
    {
        var window = new AboutWindow
        {
            Owner = GetOwnerWindow()
        };

        window.ShowDialog();
    }

    [RelayCommand]
    private static void OpenWhatsNew()
    {
        var window = new WhatsNewWindow
        {
            Owner = GetOwnerWindow()
        };

        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(
            _settings,
            _themeService,
            _vaultService,
            _vaultSession,
            _sessionPasswordCache);

        var win = new SettingsWindow(vm) { Owner = GetOwnerWindow() };
        win.ShowDialog();

        _autoLockTimer?.Stop();
        StartAutoLockTimer();
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            var seconds = _settings.RevealSecretSeconds;
            _passwordRevealTimer.Start(seconds, () =>
            {
                _isPasswordVisible = false;
                UpdatePasswordDisplay();
            });
        }
        else
        {
            _passwordRevealTimer.Stop();
        }

        UpdatePasswordDisplay();
    }

    [RelayCommand]
    private void ToggleCustomFieldVisibility(CustomFieldDisplayItem? item)
    {
        if (item is null || !item.IsSecret)
            return;

        if (item.IsRevealed)
            item.Hide();
        else
            item.Reveal(_settings.RevealSecretSeconds);
    }

    [RelayCommand]
    private void CopyCustomField(CustomFieldDisplayItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Value)) return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(item.Value, timeout);

        SnackbarService.Show($"\"{item.Name}\" copied. Clears in {_settings.ClipboardClearSeconds}s.");
    }

    [RelayCommand]
    private void CopyUsername()
    {
        if (SelectedEntry is null || string.IsNullOrEmpty(SelectedEntry.Username))
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(SelectedEntry.Username, timeout);

        SnackbarService.Show($"Username copied. Clears in {_settings.ClipboardClearSeconds}s.");
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.Url))
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(SelectedEntry.Url, timeout);

        SnackbarService.Show($"URL copied. Clears in {_settings.ClipboardClearSeconds}s.");
    }

    [RelayCommand]
    private void CopyTotp()
    {
        if (string.IsNullOrEmpty(TotpCode) || TotpCodeFormatted == "Invalid secret")
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(TotpCode, timeout);

        SnackbarService.Show($"TOTP code copied. Clears in {_settings.ClipboardClearSeconds}s.");
    }

    [RelayCommand]


    private void OpenUrl()
    {
        if (SelectedEntry is null ||
            string.IsNullOrWhiteSpace(SelectedEntry.Url))
        {
            return;
        }

        try
        {
            if (!UpdateService.OpenUrl(SelectedEntry.Url))
            {
                DialogService.Warning(
                    "The URL is invalid or uses an unsupported scheme.",
                    "Open URL");

                return;
            }

            SnackbarService.Show("Opened in browser");
        }
        catch
        {
            DialogService.Warning(
                "Could not open the URL.",
                "Open URL");
        }
    }

    [RelayCommand]
    private void Lock()
    {
        Lock(true);
    }

    public void Lock(bool showLogin)
    {
        if (!_vaultSession.IsActive)
            return;

        _autoLockTimer?.Stop();
        _totpTimer?.Stop();
        _clipboard.ClearIfOwned();
        _vaultSession.Clear();

        StatusMessage = "Vault locked.";
        RequestLock?.Invoke(
            this,
            new LockRequestedEventArgs(showLogin));
    }

    [RelayCommand]
    private void CopyPassword(VaultEntry? entry)
    {
        entry ??= SelectedEntry;

        if (entry is null || string.IsNullOrEmpty(entry.Password))
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(entry.Password, timeout);

        SnackbarService.Show($"Password copied. Clears in {_settings.ClipboardClearSeconds}s.");
    }

    [RelayCommand]
    private async Task AddEntryAsync()
    {
        var editor = new EntryEditorWindow(
            new EntryEditorViewModel(null, GetUsedTags()),
            candidate => Entries.Any(e =>
                string.Equals(
                    EntryContentFingerprint(e),
                    EntryContentFingerprint(candidate),
                    StringComparison.Ordinal)))
        {
            Owner = GetOwnerWindow()
        };

        if (editor.ShowDialog() == true && editor.Result is { } newEntry)
        {
            if (!await EnsureVaultWritableAsync())
                return;

            var vault = _vaultSession.Vault!;
            var snapshot = vault.CreateSnapshot();

            newEntry.SortOrder = Entries
                .Where(entry => entry.IsFavorite == newEntry.IsFavorite)
                .Select(entry => entry.SortOrder)
                .DefaultIfEmpty(-10)
                .Max() + 10;

            vault.AddEntry(newEntry);
            Entries.Add(newEntry);

            RebuildTagFilters();
            ApplyFilter(preserveSelectionId: newEntry.Id);

            if (!await SaveVaultAsync())
            {
                RestoreVaultSnapshot(snapshot);
                return;
            }

            SnackbarService.Show("Entry added.");
        }
    }

    [RelayCommand]
    private async Task EditEntryAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null)
            return;

        var editorVm = new EntryEditorViewModel(entry, GetUsedTags());
        var editor = new EntryEditorWindow(
            editorVm,
            candidate => Entries.Any(e =>
                e.Id != candidate.Id &&
                string.Equals(
                    EntryContentFingerprint(e),
                    EntryContentFingerprint(candidate),
                    StringComparison.Ordinal)))
        {
            Owner = GetOwnerWindow()
        };

        if (editor.ShowDialog() != true || editor.Result is not { } updated)
            return;

        var sensitive = editorVm.GetSensitiveChanges();

        if (sensitive.Count > 0)
        {
            var summary = string.Join("\n", sensitive.Select(c => $"• {c.FieldName}"));

            var proceed = DialogService.Confirm(
                $"These sensitive fields will change:\n\n{summary}\n\nContinue?",
                "Confirm sensitive changes",
                yesText: "Save",
                noText: "Cancel");

            if (!proceed)
                return;
        }

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var entryIndex = Entries.IndexOf(entry);

        if (!vault.Entries.Any(e => e.Id == updated.Id))
            return;

        var snapshot = vault.CreateSnapshot();

        var dataChanged = vault.UpdateEntry(updated);
        var favoriteChanged = vault.SetFavorite(updated.Id, updated.IsFavorite);

        if (!dataChanged && !favoriteChanged)
        {
            SnackbarService.Show("No changes.", SnackbarKind.Info);
            return;
        }

        var persistedEntry = vault.FindEntry(updated.Id)!;

        if (entryIndex >= 0)
            Entries[entryIndex] = persistedEntry;

        SelectedEntry = persistedEntry;

        IsFavoriteSelected = persistedEntry.IsFavorite;

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: persistedEntry.Id);
        RebuildDisplayCustomFields();
        UpdatePasswordDisplay();
        RefreshTotp();
        OnSelectedEntryChanged(SelectedEntry);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot, updated.Id);
            return;
        }

        SnackbarService.Show("Entry updated.");
    }

    [RelayCommand]
    private async Task DeleteAllEntriesAsync()
    {
        var vault = _vaultSession.Vault;
        if (vault is null || Entries.Count == 0)
            return;

        var count = Entries.Count;
        var confirmationMessage = count == 1
            ? "Move this entry to Trash?\n\nYou can restore it within 30 days."
            : $"Move all {count} entries to Trash?\n\nYou can restore them within 30 days.";

        if (!DialogService.Confirm(
                confirmationMessage,
                "Delete All Entries",
                yesText: "Move all to Trash",
                noText: "Cancel"))
            return;

        if (!await EnsureVaultWritableAsync())
            return;

        var snapshot = vault.CreateSnapshot();
        var entryIds = Entries.Select(e => e.Id).ToArray();

        foreach (var entryId in entryIds)
            vault.SoftDelete(entryId);

        Entries.Clear();
        SelectedEntry = null;

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: null);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot);
            return;
        }

        UpdateEntryStatus();
        SnackbarService.Show(count == 1
            ? "Moved 1 entry to Trash."
            : $"Moved {count} entries to Trash.");
    }

    [RelayCommand]
    private async Task DeleteSelectedEntriesAsync(IReadOnlyList<VaultEntry>? selectedEntries)
    {
        var selected = selectedEntries?
            .Where(e => Entries.Any(active => active.Id == e.Id))
            .DistinctBy(e => e.Id)
            .ToList();

        if (selected is not { Count: > 0 })
            return;

        if (!DialogService.Confirm(
                $"Move {selected.Count} selected entries to Trash?\n\nYou can restore them within 30 days.",
                "Move selected entries to Trash",
                yesText: "Move to Trash",
                noText: "Cancel"))
            return;

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var snapshot = vault.CreateSnapshot();

        foreach (var entry in selected)
        {
            vault.SoftDelete(entry.Id);
            Entries.Remove(entry);
        }

        if (SelectedEntry is not null && selected.Any(e => e.Id == SelectedEntry.Id))
            SelectedEntry = null;

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: null);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot);
            return;
        }

        UpdateEntryStatus($"moved {selected.Count} to trash");
        SnackbarService.Show($"Moved {selected.Count} entries to Trash.");
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;

        if (!DialogService.Confirm(
                $"Move \"{entry.Title}\" to Trash?\n\nYou can restore it within 30 days.",
                "Move to Trash",
                yesText: "Move to Trash",
                noText: "Cancel"))
            return;

        if (!await EnsureVaultWritableAsync())
            return;

        var vault = _vaultSession.Vault!;
        var snapshot = vault.CreateSnapshot();

        vault.SoftDelete(entry.Id);
        Entries.Remove(entry);
        SelectedEntry = null;

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: null);

        if (!await SaveVaultAsync())
        {
            RestoreVaultSnapshot(snapshot);
            return;
        }

        UpdateEntryStatus("moved to trash");
        SnackbarService.Show("Moved entry to Trash.");
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import browser passwords (CSV)",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Importing...";
            StatusMessage = "Importing...";
            var imported = await _importService.ImportFromCsvAsync(dlg.FileName);

            if (imported.Count == 0)
            {
                SnackbarService.Show("Import finished — nothing imported.", SnackbarKind.Info);
                return;
            }

            if (!DialogService.Confirm(
                    $"Found {imported.Count} entries.\n\nMerge them into the current vault?\n\nIdentical entries are skipped. Entries matching the current Trash are restored to the list.",
                    "Confirm Import",
                    yesText: "Import",
                    noText: "Cancel"))
            {
                SnackbarService.Show("Import cancelled.", SnackbarKind.Info);
                return;
            }

            if (!await EnsureVaultWritableAsync())
                return;

            var vault = _vaultSession.Vault!;
            var snapshot = vault.CreateSnapshot();
            var result = MergeImportedEntries(vault, imported, includeTags: false);

            Entries.Clear();
            foreach (var entry in vault.Entries)
                Entries.Add(entry);

            RebuildTagFilters();
            ApplyFilter();

            if (!await SaveVaultAsync())
            {
                RestoreVaultSnapshot(snapshot);
                return;
            }

            DialogService.Success(
                $"Import finished.\n\n" +
                $"Added to list:              {result.AddedToList}\n" +
                $"Restored from trash → list: {result.RestoredFromTrash}\n" +
                $"Skipped (already in list):  {result.SkippedAlreadyInList}\n\n" +
                $"Vault now: {vault.Entries.Count} entries · {vault.Trash.Count} in trash\n\n" +
                $"Saved to:\n{_vaultSession.VaultPath}",
                "Import complete");
        }
        catch (Exception)
        {
            DialogService.Error(
                "Import failed. Check the file format and try again.",
                "Import Error");

            StatusMessage = "Import failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenHealth()
    {
        var vm = new VaultHealthViewModel(Entries);
        var win = new VaultHealthWindow(vm)
        {
            Owner = GetOwnerWindow()
        };

        if (win.ShowDialog() == true && win.SelectedEntryId is { } id)
        {
            if (FilteredEntries.All(e => e.Id != id))
            {
                SearchText = string.Empty;
                ApplyFilter(preserveSelectionId: id);
            }
            else
            {
                SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == id);
            }

            if (SelectedEntry is not null)
                RequestScrollToEntry?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task OpenTrashAsync()
    {
        var vault = _vaultSession.Vault;
        if (vault is null) return;

        var vm = new TrashViewModel(
            vault,
            EnsureVaultWritableAsync,
            SaveVaultAsync,
            candidate => Entries.Any(e =>
                string.Equals(
                    EntryContentFingerprint(e),
                    EntryContentFingerprint(candidate),
                    StringComparison.Ordinal)));

        var win = new TrashWindow(vm)
        {
            Owner = GetOwnerWindow()
        };

        vm.Changed += OnTrashChanged;

        try
        {
            win.ShowDialog();
        }
        finally
        {
            vm.Changed -= OnTrashChanged;
        }

        RefreshEntriesFromVault();
    }

    private void OnTrashChanged(object? sender, EventArgs e)
    {
        RefreshEntriesFromVault();
    }

    private void RefreshEntriesFromVault()
    {
        var vault = _vaultSession.Vault;
        if (vault is null)
            return;

        Entries.Clear();

        foreach (var e in vault.Entries)
            Entries.Add(e);

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: SelectedEntry?.Id);
        UpdateEntryStatus();
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        if (!_vaultSession.IsActive)
        {
            DialogService.Warning(
                "No vault file is open.",
                "Export Backup");

            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export encrypted vault backup",
            Filter = "PejPass Vault (*.pejpass)|*.pejpass|All files (*.*)|*.*",
            DefaultExt = ".pejpass",
            FileName = $"PejPass-backup-{DateTime.Now:yyyyMMdd-HHmm}.pejpass",
            AddExtension = true
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Exporting backup...";

            await _vaultService.SaveVaultAsync(
                dlg.FileName,
                _vaultSession.GetSecret(),
                _vaultSession.Vault!);

            SnackbarService.Show("Encrypted backup exported.");
        }
        catch (Exception)
        {
            DialogService.Error(
                "Backup export failed. Check the destination and try again.",
                "Export Backup");

            StatusMessage = "Backup failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenHistory()
    {
        var window = App.Services.GetRequiredService<HistoryWindow>();

        if (window.DataContext is HistoryViewModel historyViewModel)
        {
            historyViewModel.HistoryRestored += OnHistoryRestored;
        }

        window.Owner = System.Windows.Application.Current.MainWindow;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            if (window.DataContext is HistoryViewModel currentViewModel)
                currentViewModel.HistoryRestored -= OnHistoryRestored;
        }
    }

    private async void OnHistoryRestored(object? sender, EventArgs e)
    {
        await LoadVaultAsync();
        SelectedEntry = null;
    }

    private void RestoreVaultSnapshot(Vault snapshot, Guid? preserveSelectionId = null)
    {
        _vaultSession.Vault!.RestoreSnapshot(snapshot);

        Entries.Clear();
        foreach (var entry in _vaultSession.Vault.Entries)
            Entries.Add(entry);

        RebuildTagFilters();

        SelectedEntry = preserveSelectionId is { } id
            ? Entries.FirstOrDefault(e => e.Id == id)
            : null;

        ApplyFilter(preserveSelectionId);

        if (SelectedEntry is not null)
            OnSelectedEntryChanged(SelectedEntry);
        else
        {
            RebuildDisplayCustomFields();
            UpdatePasswordDisplay();
            RefreshTotp();
        }

        UpdateEntryStatus();
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

    private async Task<bool> SaveVaultAsync()
    {
        var ownsBusyState = !IsBusy;

        if (ownsBusyState)
        {
            IsBusy = true;
            BusyMessage = "Saving vault...";
        }

        try
        {
            await _vaultService.SaveVaultAsync(
                _vaultSession.VaultPath!,
                _vaultSession.GetSecret(),
                _vaultSession.Vault!);

            return true;
        }
        catch (Exception)
        {
            try
            {
                var vault = await _vaultService.OpenVaultAsync(
                    _vaultSession.VaultPath!,
                    _vaultSession.GetSecret());

                _vaultSession.ReplaceVault(vault);
                await LoadVaultAsync();
                SelectedEntry = null;
            }
            catch
            {
                // Keep the original save error.
            }

            DialogService.Error(
                "Failed to save the vault. Check disk space and file permissions, then try again.",
                "Save failed");

            return false;
        }
        finally
        {
            if (ownsBusyState)
                IsBusy = false;
        }
    }
}

public partial class CustomFieldDisplayItem(CustomField field) : ObservableObject, IDisposable
{
    private readonly SecretRevealTimer _revealTimer = new();

    public string Name { get; } = field.Name;
    public string Value { get; } = field.Value;
    public bool IsSecret { get; } = field.IsSecret;

    [ObservableProperty]
    public partial bool IsRevealed { get; set; } = !field.IsSecret;

    public string DisplayValue =>
        IsSecret && !IsRevealed
            ? (string.IsNullOrEmpty(Value) ? string.Empty : new string('•', Math.Min(Value.Length, 16)))
            : Value;

    public string RevealButtonText => IsRevealed ? "Hide" : "Show";

    public void Reveal(int seconds)
    {
        IsRevealed = true;
        _revealTimer.Start(seconds, Hide);
    }

    public void Hide()
    {
        _revealTimer.Stop();
        IsRevealed = false;
    }

    public void Dispose()
    {
        _revealTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RevealButtonText));
    }
}


public sealed class LockRequestedEventArgs(bool showLogin) : EventArgs
{
    public bool ShowLogin { get; } = showLogin;
}
