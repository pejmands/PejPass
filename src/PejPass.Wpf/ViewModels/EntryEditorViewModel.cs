using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using System.Security.Cryptography;

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

    public VaultEntry ToEntry()
    {
        var tags = TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (Original is not null)
        {
            // Editing – return a copy with same Id
            return new VaultEntry
            {
                Id = Original.Id,
                Title = Title.Trim(),
                Username = Username.Trim(),
                Password = Password,
                Url = Url.Trim(),
                Notes = Notes,
                Tags = tags,
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
            Tags = tags
        };
    }
}
