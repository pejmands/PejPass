using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Domain.Security;
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

    public VaultEntry? Original { get; }

    public ObservableCollection<CustomFieldItem> CustomFields { get; } = [];

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
        }
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        // Quick generate with secure defaults
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

    public string BuildHistoryAppendix(IReadOnlyList<SensitiveChange> changes)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"--- History {DateTime.Now:yyyy-MM-dd HH:mm} ---");
        foreach (var c in changes)
        {
            sb.AppendLine($"{c.FieldName}:");
            sb.AppendLine(string.IsNullOrEmpty(c.OldValue) ? "  (empty)" : $"  {c.OldValue}");
        }
        return sb.ToString();
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

        if (Original is not null)
        {
            return new VaultEntry
            {
                Id = Original.Id,
                Title = Title.Trim(),
                Username = Username.Trim(),
                Password = Password,
                Url = Url.Trim(),
                TotpSecret = TotpSecret.Trim(),
                Notes = Notes,
                Tags = tags,
                CustomFields = customFields,
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
