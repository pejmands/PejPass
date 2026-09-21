using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Domain.Security;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Views;

namespace PejPass.Wpf.ViewModels;

public partial class EntryEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _totpSecret = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _tagsText = string.Empty;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _hasPasswordHistory;
    [ObservableProperty] private bool _hasUsernameHistory;

    public VaultEntry? Original { get; }

    public ObservableCollection<CustomFieldItem> CustomFields { get; } = [];
    public ObservableCollection<PasswordHistoryRow> PasswordHistoryItems { get; } = [];
    public ObservableCollection<UsernameHistoryRow> UsernameHistoryItems { get; } = [];

    public EntryEditorViewModel(VaultEntry? existing)
    {
        Original = existing;
        if (existing is not null)
        {
            Title = existing.Title;
            Username = existing.Username;
            Password = existing.Password;
            Url = existing.Url;
            TotpSecret = existing.TotpSecret;
            Notes = existing.Notes;
            TagsText = string.Join(", ", existing.Tags);
            IsFavorite = existing.IsFavorite;

            foreach (var field in existing.CustomFields)
            {
                CustomFields.Add(new CustomFieldItem
                {
                    Name = field.Name,
                    Value = field.Value,
                    IsSecret = field.IsSecret
                });
            }

            foreach (var h in existing.PasswordHistory)
                PasswordHistoryItems.Add(new PasswordHistoryRow(h));

            foreach (var h in existing.UsernameHistory)
                UsernameHistoryItems.Add(new UsernameHistoryRow(h));

            HasPasswordHistory = PasswordHistoryItems.Count > 0;
            HasUsernameHistory = UsernameHistoryItems.Count > 0;
        }
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        Password = PasswordGenerator.Generate(new PasswordGeneratorOptions
        {
            Length = 20,
            IncludeLowercase = true,
            IncludeUppercase = true,
            IncludeDigits = true,
            IncludeSymbols = true,
            ExcludeAmbiguous = true
        });
    }

    [RelayCommand]
    private void OpenAdvancedGenerator()
    {
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w => w.IsActive);

        var vm = new PasswordGeneratorViewModel();
        var win = new PasswordGeneratorWindow(vm) { Owner = owner };

        if (win.ShowDialog() == true && !string.IsNullOrEmpty(win.GeneratedPassword))
            Password = win.GeneratedPassword;
    }

    [RelayCommand]
    private void RestorePasswordHistory(PasswordHistoryRow? row)
    {
        if (row is null) return;

        if (!DialogService.Confirm(
                "Replace the current password with this history value?",
                "Restore password",
                yesText: "Restore",
                noText: "Cancel"))
            return;

        Password = row.Password;
    }

    [RelayCommand]
    private void DeletePasswordHistory(PasswordHistoryRow? row)
    {
        if (row is null) return;
        PasswordHistoryItems.Remove(row);
        HasPasswordHistory = PasswordHistoryItems.Count > 0;
    }

    [RelayCommand]
    private void TogglePasswordHistoryReveal(PasswordHistoryRow? row)
    {
        if (row is null) return;
        row.IsRevealed = !row.IsRevealed;
    }

    [RelayCommand]
    private void RestoreUsernameHistory(UsernameHistoryRow? row)
    {
        if (row is null) return;

        if (!DialogService.Confirm(
                "Replace the current username with this history value?",
                "Restore username",
                yesText: "Restore",
                noText: "Cancel"))
            return;

        Username = row.Username;
    }

    [RelayCommand]
    private void DeleteUsernameHistory(UsernameHistoryRow? row)
    {
        if (row is null) return;
        UsernameHistoryItems.Remove(row);
        HasUsernameHistory = UsernameHistoryItems.Count > 0;
    }

    [RelayCommand]
    private void AddCustomField()
    {
        CustomFields.Add(new CustomFieldItem
        {
            Name = string.Empty,
            Value = string.Empty,
            IsSecret = false
        });
    }

    [RelayCommand]
    private void RemoveCustomField(CustomFieldItem? item)
    {
        if (item is null) return;
        CustomFields.Remove(item);
    }

    public List<SensitiveChange> GetSensitiveChanges()
    {
        var changes = new List<SensitiveChange>();
        if (Original is null) return changes;

        if (!string.Equals(Original.Password, Password, StringComparison.Ordinal))
            changes.Add(new SensitiveChange("Password", Original.Password, Password));

        if (!string.Equals(Original.Username, Username.Trim(), StringComparison.Ordinal))
            changes.Add(new SensitiveChange("Username", Original.Username, Username.Trim()));

        if (!string.Equals(Original.TotpSecret, TotpSecret.Trim(), StringComparison.Ordinal))
            changes.Add(new SensitiveChange("TOTP Secret", Original.TotpSecret, TotpSecret.Trim()));

        var newFields = CustomFields
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .ToDictionary(f => f.Name.Trim(), f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var old in Original.CustomFields.Where(f => f.IsSecret))
        {
            if (!newFields.TryGetValue(old.Name, out var neu))
                changes.Add(new SensitiveChange($"Custom field \"{old.Name}\" (removed)", old.Value, string.Empty));
            else if (!string.Equals(old.Value, neu.Value ?? string.Empty, StringComparison.Ordinal))
                changes.Add(new SensitiveChange($"Custom field \"{old.Name}\"", old.Value, neu.Value ?? string.Empty));
        }

        return changes;
    }

    public VaultEntry ToEntry()
    {
        var tags = TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var customFields = CustomFields
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => new CustomField
            {
                Name = f.Name.Trim(),
                Value = f.Value ?? string.Empty,
                IsSecret = f.IsSecret
            })
            .ToList();

        // History from UI (user may have deleted rows) + auto-push on change — never into Notes
        var passwordHistory = PasswordHistoryItems
            .Select(h => new PasswordHistoryItem
            {
                Password = h.Password,
                ChangedAt = h.ChangedAt
            })
            .ToList();

        var usernameHistory = UsernameHistoryItems
            .Select(h => new UsernameHistoryItem
            {
                Username = h.Username,
                ChangedAt = h.ChangedAt
            })
            .ToList();

        if (Original is not null)
        {
            if (!string.Equals(Original.Password, Password, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(Original.Password))
            {
                if (passwordHistory.Count == 0 ||
                    !string.Equals(passwordHistory[0].Password, Original.Password, StringComparison.Ordinal))
                {
                    passwordHistory.Insert(0, new PasswordHistoryItem
                    {
                        Password = Original.Password,
                        ChangedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            var newUsername = Username.Trim();
            if (!string.Equals(Original.Username, newUsername, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(Original.Username))
            {
                if (usernameHistory.Count == 0 ||
                    !string.Equals(usernameHistory[0].Username, Original.Username, StringComparison.Ordinal))
                {
                    usernameHistory.Insert(0, new UsernameHistoryItem
                    {
                        Username = Original.Username,
                        ChangedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            if (passwordHistory.Count > VaultEntry.MaxPasswordHistory)
                passwordHistory = passwordHistory.Take(VaultEntry.MaxPasswordHistory).ToList();

            if (usernameHistory.Count > VaultEntry.MaxUsernameHistory)
                usernameHistory = usernameHistory.Take(VaultEntry.MaxUsernameHistory).ToList();

            return new VaultEntry
            {
                Id = Original.Id,
                Title = Title.Trim(),
                Username = newUsername,
                Password = Password,
                Url = Url.Trim(),
                TotpSecret = TotpSecret.Trim(),
                Notes = Notes,
                Tags = tags,
                CustomFields = customFields,
                PasswordHistory = passwordHistory,
                UsernameHistory = usernameHistory,
                IsFavorite = IsFavorite,
                SortOrder = Original.SortOrder,
                CreatedAt = Original.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        return new VaultEntry
        {
            Title = Title.Trim(),
            Username = Username.Trim(),
            Password = Password,
            Url = Url.Trim(),
            TotpSecret = TotpSecret.Trim(),
            Notes = Notes,
            Tags = tags,
            CustomFields = customFields,
            PasswordHistory = passwordHistory,
            UsernameHistory = usernameHistory,
            IsFavorite = IsFavorite
        };
    }
}

public sealed record SensitiveChange(string FieldName, string OldValue, string NewValue);

public partial class CustomFieldItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private bool _isSecret;
}

public partial class PasswordHistoryRow : ObservableObject
{
    public string Password { get; }
    public DateTimeOffset ChangedAt { get; }
    public string ChangedAtText { get; }

    [ObservableProperty] private bool _isRevealed;

    public PasswordHistoryRow(PasswordHistoryItem item)
    {
        Password = item.Password;
        ChangedAt = item.ChangedAt;
        ChangedAtText = item.ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public string DisplayValue =>
        IsRevealed
            ? Password
            : (string.IsNullOrEmpty(Password)
                ? string.Empty
                : new string('•', Math.Min(Password.Length, 16)));

    public string RevealButtonText => IsRevealed ? "Hide" : "Show";

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RevealButtonText));
    }
}

public sealed class UsernameHistoryRow
{
    public string Username { get; }
    public DateTimeOffset ChangedAt { get; }
    public string ChangedAtText { get; }

    public UsernameHistoryRow(UsernameHistoryItem item)
    {
        Username = item.Username;
        ChangedAt = item.ChangedAt;
        ChangedAtText = item.ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
