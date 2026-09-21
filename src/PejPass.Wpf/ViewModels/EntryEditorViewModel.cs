using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;

namespace PejPass.Wpf.ViewModels;

public partial class EntryEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _tagsText = string.Empty;

    public VaultEntry? Original { get; }

    public ObservableCollection<CustomFieldItem> CustomFields { get; } = new();

    public EntryEditorViewModel(VaultEntry? existing)
    {
        Original = existing;
        if (existing is not null)
        {
            Title = existing.Title;
            Username = existing.Username;
            Password = existing.Password;
            Url = existing.Url;
            Notes = existing.Notes;
            TagsText = string.Join(", ", existing.Tags);

            foreach (var field in existing.CustomFields)
            {
                CustomFields.Add(new CustomFieldItem
                {
                    Name = field.Name,
                    Value = field.Value
                });
            }
        }
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*-_=+";
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);

        var result = new char[20];
        for (int i = 0; i < 20; i++)
            result[i] = chars[bytes[i] % chars.Length];

        Password = new string(result);
    }

    [RelayCommand]
    private void AddCustomField()
    {
        CustomFields.Add(new CustomFieldItem
        {
            Name = string.Empty,
            Value = string.Empty
        });
    }

    [RelayCommand]
    private void RemoveCustomField(CustomFieldItem? item)
    {
        if (item is null) return;
        CustomFields.Remove(item);
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
                Value = f.Value ?? string.Empty
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
                Notes = Notes,
                Tags = tags,
                CustomFields = customFields,
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
            Notes = Notes,
            Tags = tags,
            CustomFields = customFields
        };
    }
}

/// <summary>
/// UI-friendly wrapper for a custom field (supports two-way binding).
/// </summary>
public partial class CustomFieldItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
}
