using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Infrastructure.Totp;
using PejPass.Wpf.Views;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VaultService _vaultService;
    private readonly IClipboardService _clipboard;
    private readonly IBrowserImportService _importService;
    private readonly AppSettings _settings;

    private System.Timers.Timer? _autoLockTimer;
    private DispatcherTimer? _totpTimer;
    private bool _isPasswordVisible;

    public event EventHandler? RequestLock;

    [ObservableProperty] private string _vaultName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private VaultEntry? _selectedEntry;

    // Detail panel state
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasUrl;
    [ObservableProperty] private bool _hasTotp;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private bool _hasCustomFields;
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private string _displayPassword = string.Empty;
    [ObservableProperty] private string _showPasswordButtonText = "Show";

    // TOTP display
    [ObservableProperty] private string _totpCode = string.Empty;
    [ObservableProperty] private string _totpCodeFormatted = string.Empty;
    [ObservableProperty] private int _totpRemainingSeconds;
    [ObservableProperty] private double _totpProgress;

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public ObservableCollection<VaultEntry> FilteredEntries { get; } = [];
    public ObservableCollection<CustomFieldDisplayItem> DisplayCustomFields { get; } = [];

    public MainViewModel(
        VaultService vaultService,
        IClipboardService clipboard,
        IBrowserImportService importService,
        AppSettings settings)
    {
        _vaultService = vaultService;
        _clipboard = clipboard;
        _importService = importService;
        _settings = settings;

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
        UpdatePasswordDisplay();
        RebuildDisplayCustomFields();
        RefreshTotp();
        ResetAutoLockTimer();
    }

    private void RebuildDisplayCustomFields()
    {
        DisplayCustomFields.Clear();
        if (SelectedEntry?.CustomFields is null) return;

        foreach (var f in SelectedEntry.CustomFields)
        {
            DisplayCustomFields.Add(new CustomFieldDisplayItem(f));
        }
    }

    private void StartTotpTimer()
    {
        _totpTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
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
            TotpCodeFormatted = code.Length == 6
                ? $"{code[..3]} {code[3..]}"
                : code;
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
        var vault = LoginViewModel.CurrentVault;
        if (vault is null) return;

        VaultName = vault.Name;
        Entries.Clear();
        foreach (var e in vault.Entries.OrderBy(x => x.Title))
            Entries.Add(e);

        ApplyFilter(preserveSelectionId: null);
        StatusMessage = $"{Entries.Count} entries";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter(Guid? preserveSelectionId = null)
    {
        var keepId = preserveSelectionId ?? SelectedEntry?.Id;

        FilteredEntries.Clear();
        var q = SearchText?.Trim() ?? string.Empty;

        IEnumerable<VaultEntry> source = Entries;
        if (!string.IsNullOrEmpty(q))
        {
            source = Entries.Where(e =>
                e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Url.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                e.CustomFields.Any(f =>
                    f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    f.Value.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var e in source)
            FilteredEntries.Add(e);

        // Restore selection after collection rebuild so ListBox doesn't lose it
        if (keepId is { } id)
        {
            var match = FilteredEntries.FirstOrDefault(e => e.Id == id);
            SelectedEntry = match; // may be null if filtered out
        }
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
        _autoLockTimer?.Start();
    }

    private static Window? GetOwnerWindow()
    {
        return System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
               ?? System.Windows.Application.Current?.MainWindow;
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
        if (SelectedEntry is null || string.IsNullOrEmpty(SelectedEntry.Username)) return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(SelectedEntry.Username, timeout);
        StatusMessage = $"Username copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.Url)) return;

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
            MessageBox.Show($"Could not open URL:\n{ex.Message}", "Open URL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void Lock()
    {
        _autoLockTimer?.Stop();
        _totpTimer?.Stop();
        _clipboard.Clear();
        LoginViewModel.ClearSession();
        StatusMessage = "Vault locked.";
        RequestLock?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyPassword(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null || string.IsNullOrEmpty(entry.Password)) return;

        var timeout = TimeSpan.FromSeconds(_settings.ClipboardClearSeconds);
        _clipboard.CopyWithTimeout(entry.Password, timeout);
        StatusMessage = $"Password copied. Clears in {_settings.ClipboardClearSeconds}s.";
        ResetAutoLockTimer();
    }

    [RelayCommand]
    private async Task AddEntryAsync()
    {
        var editor = new EntryEditorWindow(new EntryEditorViewModel(null))
        {
            Owner = GetOwnerWindow()
        };

        if (editor.ShowDialog() == true && editor.Result is { } newEntry)
        {
            var vault = LoginViewModel.CurrentVault!;
            vault.AddEntry(newEntry);
            Entries.Add(newEntry);
            ApplyFilter(preserveSelectionId: newEntry.Id);

            await SaveVaultAsync();
            StatusMessage = "Entry added.";
            ResetAutoLockTimer();
        }
    }

    [RelayCommand]
    private async Task EditEntryAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;

        var editor = new EntryEditorWindow(new EntryEditorViewModel(entry))
        {
            Owner = GetOwnerWindow()
        };

        if (editor.ShowDialog() == true && editor.Result is { } updated)
        {
            entry.Title = updated.Title;
            entry.Username = updated.Username;
            entry.Password = updated.Password;
            entry.Url = updated.Url;
            entry.TotpSecret = updated.TotpSecret;
            entry.Notes = updated.Notes;
            entry.Tags = updated.Tags;
            entry.CustomFields = updated.CustomFields;
            entry.Touch();

            ApplyFilter(preserveSelectionId: entry.Id);
            // Force detail panel refresh for same object
            RebuildDisplayCustomFields();
            UpdatePasswordDisplay();
            RefreshTotp();
            OnSelectedEntryChanged(SelectedEntry);

            await SaveVaultAsync();
            StatusMessage = "Entry updated.";
            ResetAutoLockTimer();
        }
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(VaultEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;

        var result = MessageBox.Show(
            $"Delete \"{entry.Title}\"?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var vault = LoginViewModel.CurrentVault!;
        vault.RemoveEntry(entry.Id);
        Entries.Remove(entry);
        SelectedEntry = null;

        ApplyFilter(preserveSelectionId: null);
        await SaveVaultAsync();
        StatusMessage = "Entry deleted.";
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
                MessageBox.Show("No valid password entries found in the file.", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusMessage = "Import finished — nothing imported.";
                return;
            }

            var confirm = MessageBox.Show(
                $"Found {imported.Count} entries.\n\nImport them into the current vault?",
                "Confirm Import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                StatusMessage = "Import cancelled.";
                return;
            }

            var vault = LoginViewModel.CurrentVault!;
            foreach (var e in imported)
            {
                vault.AddEntry(e);
                Entries.Add(e);
            }

            ApplyFilter();
            await SaveVaultAsync();

            StatusMessage = $"Imported {imported.Count} entries.";
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Import failed.";
        }
    }

    private async Task SaveVaultAsync()
    {
        var path = LoginViewModel.CurrentVaultPath!;
        var password = LoginViewModel.CurrentMasterPassword!;
        var vault = LoginViewModel.CurrentVault!;

        await _vaultService.SaveVaultAsync(path, password, vault);
    }
}

/// <summary>
/// UI model for a custom field in the detail panel (supports reveal for secrets).
/// </summary>
public partial class CustomFieldDisplayItem : ObservableObject
{
    public string Name { get; }
    public string Value { get; }
    public bool IsSecret { get; }

    [ObservableProperty] private bool _isRevealed;

    public CustomFieldDisplayItem(CustomField field)
    {
        Name = field.Name;
        Value = field.Value;
        IsSecret = field.IsSecret;
        IsRevealed = !field.IsSecret;
    }

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
