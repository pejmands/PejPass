using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PejPass.Application.Interfaces;
using PejPass.Application.Services;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Views;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VaultService _vaultService;
    private readonly IClipboardService _clipboard;
    private readonly IBrowserImportService _importService;
    private readonly AppSettings _settings;

    private System.Timers.Timer? _autoLockTimer;
    private bool _isPasswordVisible;

    public event EventHandler? RequestLock;

    [ObservableProperty] private string _vaultName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private VaultEntry? _selectedEntry;

    // Detail panel helpers
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private bool _hasCustomFields;
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private string _displayPassword = string.Empty;
    [ObservableProperty] private string _showPasswordButtonText = "Show";

    public ObservableCollection<VaultEntry> Entries { get; } = new();
    public ObservableCollection<VaultEntry> FilteredEntries { get; } = new();

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
    }

    partial void OnSelectedEntryChanged(VaultEntry? value)
    {
        _isPasswordVisible = false;
        HasSelection = value is not null;
        HasNotes = !string.IsNullOrWhiteSpace(value?.Notes);
        HasCustomFields = value?.CustomFields?.Count > 0;
        HasTags = value?.Tags?.Count > 0;
        UpdatePasswordDisplay();
        ResetAutoLockTimer();
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

        ApplyFilter();
        StatusMessage = $"{Entries.Count} entries";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
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
    }

    private void StartAutoLockTimer()
    {
        if (_settings.AutoLockMinutes <= 0) return;

        _autoLockTimer = new System.Timers.Timer(_settings.AutoLockMinutes * 60_000);
        _autoLockTimer.Elapsed += (_, _) =>
        {
            Application.Current?.Dispatcher.Invoke(() => Lock());
        };
        _autoLockTimer.AutoReset = false;
        _autoLockTimer.Start();
    }

    private void ResetAutoLockTimer()
    {
        _autoLockTimer?.Stop();
        _autoLockTimer?.Start();
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        _isPasswordVisible = !_isPasswordVisible;
        UpdatePasswordDisplay();
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
    private void Lock()
    {
        _autoLockTimer?.Stop();
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
        var editor = new EntryEditorWindow(new EntryEditorViewModel(null));
        if (editor.ShowDialog() == true && editor.Result is { } newEntry)
        {
            var vault = LoginViewModel.CurrentVault!;
            vault.AddEntry(newEntry);
            Entries.Add(newEntry);
            ApplyFilter();
            SelectedEntry = newEntry;

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

        var editor = new EntryEditorWindow(new EntryEditorViewModel(entry));
        if (editor.ShowDialog() == true && editor.Result is { } updated)
        {
            entry.Title = updated.Title;
            entry.Username = updated.Username;
            entry.Password = updated.Password;
            entry.Url = updated.Url;
            entry.Notes = updated.Notes;
            entry.Tags = updated.Tags;
            entry.CustomFields = updated.CustomFields;
            entry.Touch();

            // Force UI refresh of detail panel
            var current = entry;
            SelectedEntry = null;
            SelectedEntry = current;

            ApplyFilter();
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
        if (SelectedEntry?.Id == entry.Id)
            SelectedEntry = null;

        ApplyFilter();
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
