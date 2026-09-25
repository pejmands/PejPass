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
using System.Diagnostics;
using System.Windows;
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

    private System.Timers.Timer? _autoLockTimer;
    private DispatcherTimer? _totpTimer;
    private bool _isPasswordVisible;

    public event EventHandler? RequestLock;
    public event EventHandler? RequestScrollToEntry;

    [ObservableProperty]
    public partial string VaultName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

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

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public ObservableCollection<VaultEntry> FilteredEntries { get; } = [];
    public ObservableCollection<CustomFieldDisplayItem> DisplayCustomFields { get; } = [];

    public MainViewModel(
        VaultService vaultService,
        IClipboardService clipboard,
        IBrowserImportService importService,
        AppSettings settings,
        ThemeService themeService,
        VaultSession vaultSession)
    {
        _vaultService = vaultService;
        _clipboard = clipboard;
        _importService = importService;
        _settings = settings;
        _themeService = themeService;
        _vaultSession = vaultSession;

        SelectedSortIndex = (int)_settings.SortMode;

        LoadVault();
        StartAutoLockTimer();
        StartTotpTimer();
    }

    partial void OnSelectedEntryChanged(VaultEntry? value)
    {
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
        ResetAutoLockTimer();
    }

    partial void OnSelectedSortIndexChanged(int value)
    {
        if (value < 0 || value > 4) return;
        _settings.SortMode = (EntrySortMode)value;
        SettingsStore.Save(_settings);
        ApplyFilter();
    }

    private void RebuildDisplayCustomFields()
    {
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

    private void LoadVault()
    {
        var vault = _vaultSession.Vault;
        if (vault is null) return;

        VaultName = vault.Name;
        Entries.Clear();

        foreach (var e in vault.Entries)
            Entries.Add(e);

        var purged = vault.PurgeExpiredTrash();
        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: null);
        UpdateEntryStatus(purged > 0 ? $"purged {purged} expired trash item(s)" : null);
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

    private void ApplyFilter(Guid? preserveSelectionId = null)
    {
        var keepId = preserveSelectionId ?? SelectedEntry?.Id;

        FilteredEntries.Clear();
        var q = SearchText?.Trim() ?? string.Empty;

        IEnumerable<VaultEntry> source = Entries;

        if (!string.IsNullOrEmpty(SelectedTagFilter))
        {
            var tag = SelectedTagFilter;
            source = source.Where(e =>
                e.Tags.Any(t => string.Equals(t.Trim(), tag, StringComparison.OrdinalIgnoreCase)));
        }

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

        if (keepId is { } id)
            SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == id);
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

    private void StartAutoLockTimer()
    {
        if (_settings.AutoLockMinutes <= 0) return;

        _autoLockTimer = new System.Timers.Timer(_settings.AutoLockMinutes * 60_000);
        _autoLockTimer.Elapsed += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => Lock());
        };
        _autoLockTimer.AutoReset = false;
        _autoLockTimer.Start();
    }

    private void ResetAutoLockTimer()
    {
        _autoLockTimer?.Stop();

        if (_settings.AutoLockMinutes > 0)
        {
            _autoLockTimer ??= new System.Timers.Timer();
            _autoLockTimer.Interval = _settings.AutoLockMinutes * 60_000;
            _autoLockTimer.Start();
        }
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
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;

        entry.IsFavorite = !entry.IsFavorite;
        entry.Touch();

        IsFavoriteSelected = entry.IsFavorite;

        ApplyFilter(preserveSelectionId: entry.Id);
        if (!await SaveVaultAsync())
            return;

        StatusMessage = entry.IsFavorite
            ? "Added to favorites."
            : "Removed from favorites.";

        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(
            _settings,
            _themeService,
            _vaultService,
            _vaultSession);

        var win = new SettingsWindow(vm) { Owner = GetOwnerWindow() };
        win.ShowDialog();

        _autoLockTimer?.Stop();
        StartAutoLockTimer();
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        _isPasswordVisible = !_isPasswordVisible;
        UpdatePasswordDisplay();
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void ToggleCustomFieldVisibility(CustomFieldDisplayItem? item)
    {
        if (item is null || !item.IsSecret) return;

        item.IsRevealed = !item.IsRevealed;
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void CopyCustomField(CustomFieldDisplayItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Value)) return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(item.Value, timeout);

        StatusMessage = $"\"{item.Name}\" copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void CopyUsername()
    {
        if (SelectedEntry is null || string.IsNullOrEmpty(SelectedEntry.Username))
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(SelectedEntry.Username, timeout);

        StatusMessage = $"Username copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.Url))
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(SelectedEntry.Url, timeout);

        StatusMessage = $"URL copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void CopyTotp()
    {
        if (string.IsNullOrEmpty(TotpCode) || TotpCodeFormatted == "Invalid secret")
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(TotpCode, timeout);

        StatusMessage = $"TOTP code copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void OpenUrl()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.Url))
            return;

        try
        {
            var url = SelectedEntry.Url.Trim();

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            StatusMessage = "Opened in browser.";
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Warning($"Could not open URL:\n{ex.Message}", "Open URL");
        }
    }

    [RelayCommand]
    private void Lock()
    {
        _autoLockTimer?.Stop();
        _totpTimer?.Stop();
        _clipboard.Clear();
        _vaultSession.Clear();

        StatusMessage = "Vault locked.";
        RequestLock?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyPassword(VaultEntry? entry)
    {
        entry ??= SelectedEntry;

        if (entry is null || string.IsNullOrEmpty(entry.Password))
            return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(entry.Password, timeout);

        StatusMessage = $"Password copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private async Task AddEntryAsync()
    {
        var editor = new EntryEditorWindow(
            new EntryEditorViewModel(null, GetUsedTags()))
        {
            Owner = GetOwnerWindow()
        };

        if (editor.ShowDialog() == true && editor.Result is { } newEntry)
        {
            _vaultSession.Vault!.AddEntry(newEntry);
            Entries.Add(newEntry);

            RebuildTagFilters();
            ApplyFilter(preserveSelectionId: newEntry.Id);

            if (!await SaveVaultAsync())
                return;

            StatusMessage = "Entry added.";
            ResetAutoLockTimer();
        }
    }

    [RelayCommand]
    private async Task EditEntryAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;

        var editorVm = new EntryEditorViewModel(entry, GetUsedTags());
        var editor = new EntryEditorWindow(editorVm)
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

        var vault = _vaultSession.Vault!;

        if (!vault.UpdateEntry(updated))
        {
            StatusMessage = "No changes.";
            return;
        }

        var entryIndex = Entries.IndexOf(entry);

        if (entryIndex >= 0)
            Entries[entryIndex] = updated;

        SelectedEntry = updated;

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: entry.Id);
        RebuildDisplayCustomFields();
        UpdatePasswordDisplay();
        RefreshTotp();
        OnSelectedEntryChanged(SelectedEntry);

        if (!await SaveVaultAsync())
            return;

        StatusMessage = "Entry updated.";
        ResetAutoLockTimer();
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

        _vaultSession.Vault!.SoftDelete(entry.Id);
        Entries.Remove(entry);
        SelectedEntry = null;

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: null);

        if (!await SaveVaultAsync())
            return;

        UpdateEntryStatus("moved to trash");
        ResetAutoLockTimer();
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
            StatusMessage = "Importing...";
            var imported = await _importService.ImportFromCsvAsync(dlg.FileName);

            if (imported.Count == 0)
            {
                DialogService.Info(
                    "No valid password entries found in the file.",
                    "Import");

                StatusMessage = "Import finished — nothing imported.";
                return;
            }

            if (!DialogService.Confirm(
                    $"Found {imported.Count} entries.\n\nImport them into the current vault?",
                    "Confirm Import",
                    yesText: "Import",
                    noText: "Cancel"))
            {
                StatusMessage = "Import cancelled.";
                return;
            }

            var vault = _vaultSession.Vault!;

            foreach (var e in imported)
            {
                vault.AddEntry(e);
                Entries.Add(e);
            }

            RebuildTagFilters();
            ApplyFilter();

            if (!await SaveVaultAsync())
                return;

            StatusMessage = $"Imported {imported.Count} entries.";
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error(
                $"Import failed:\n{ex.Message}",
                "Import Error");

            StatusMessage = "Import failed.";
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

        ResetAutoLockTimer();
    }

    [RelayCommand]
    private async Task OpenTrashAsync()
    {
        var vault = _vaultSession.Vault;
        if (vault is null) return;

        var vm = new TrashViewModel(vault);
        var win = new TrashWindow(vm)
        {
            Owner = GetOwnerWindow()
        };

        win.ShowDialog();

        Entries.Clear();

        foreach (var e in vault.Entries)
            Entries.Add(e);

        RebuildTagFilters();
        ApplyFilter(preserveSelectionId: SelectedEntry?.Id);

        if (!await SaveVaultAsync())
            return;

        UpdateEntryStatus();
        ResetAutoLockTimer();
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
            await _vaultService.SaveVaultAsync(
                dlg.FileName,
                _vaultSession.GetSecret(),
                _vaultSession.Vault!);

            StatusMessage = "Encrypted backup exported.";

            DialogService.Success(
                $"Backup saved to:\n{dlg.FileName}\n\nThis file is encrypted with your master password — store it offline.",
                "Backup exported");

            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error(
                $"Backup failed:\n{ex.Message}",
                "Export Backup");

            StatusMessage = "Backup failed.";
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

    private void OnHistoryRestored(object? sender, EventArgs e)
    {
        LoadVault();
        SelectedEntry = null;
    }

    private async Task<bool> SaveVaultAsync()
    {
        try
        {
            await _vaultService.SaveVaultAsync(
                _vaultSession.VaultPath!,
                _vaultSession.GetSecret(),
                _vaultSession.Vault!);

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                var vault = await _vaultService.OpenVaultAsync(
                    _vaultSession.VaultPath!,
                    _vaultSession.GetSecret());

                _vaultSession.ReplaceVault(vault);
                LoadVault();
                SelectedEntry = null;
            }
            catch
            {
                // Keep the original save error.
            }

            DialogService.Error(
                $"Failed to save the vault.\n\n{ex.Message}",
                "Save failed");

            return false;
        }
    }
}

public partial class CustomFieldDisplayItem(CustomField field) : ObservableObject
{
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

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RevealButtonText));
    }
}
