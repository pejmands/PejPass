using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Domain.Security;
using PejPass.Infrastructure.Totp;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PejPass.Wpf.ViewModels;

public partial class EntryEditorViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotpSecret { get; set; } = string.Empty;

    /// <summary>Non-null when TotpSecret is non-empty and not a valid Base32 secret.</summary>
    [ObservableProperty]
    public partial string? TotpErrorMessage { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TagsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial bool HasPasswordHistory { get; set; }

    [ObservableProperty]
    public partial bool HasUsernameHistory { get; set; }

    [ObservableProperty]
    public partial string PasswordStrengthLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double PasswordStrengthProgress { get; set; }

    [ObservableProperty]
    public partial int PasswordStrengthLevel { get; set; }

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
            ValidateTotpSecret();
        }
    }

    partial void OnPasswordChanged(string value) => UpdatePasswordStrength();

    partial void OnTotpSecretChanged(string value) => ValidateTotpSecret();

    private void ValidateTotpSecret()
    {
        if (string.IsNullOrWhiteSpace(TotpSecret))
        {
            TotpErrorMessage = null;
            return;
        }

        TotpErrorMessage = TotpHelper.IsValidSecret(TotpSecret)
            ? null
            : "Invalid TOTP secret — use a Base32 key, or leave empty.";
    }

    /// <summary>True when empty or a valid Base32 secret.</summary>
    public bool IsTotpSecretValid()
    {
        ValidateTotpSecret();
        return string.IsNullOrEmpty(TotpErrorMessage);
    }

    private static string NormalizeTotpSecret(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var cleaned = raw
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

        return TotpHelper.IsValidSecret(cleaned) ? cleaned : raw.Trim();
    }

    private void UpdatePasswordStrength()
    {
        var level = PasswordStrength.Evaluate(Password);
        PasswordStrengthLevel = (int)level;
        PasswordStrengthLabel = PasswordStrength.GetLabel(level);
        PasswordStrengthProgress = PasswordStrength.GetProgress(level);
    }

    [RelayCommand]
    private void ImportTotpFromFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select QR code image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            ApplyTotpImport(bitmap);
        }
        catch (Exception ex)
        {
            DialogService.Warning($"Could not read image:\n{ex.Message}", "TOTP import");
        }
    }

    [RelayCommand]
    private void ImportTotpFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                DialogService.Info(
                    "Clipboard has no image.\n\nCopy a QR code screenshot first, then try again.",
                    "TOTP import");
                return;
            }

            var image = Clipboard.GetImage();
            if (image is null)
            {
                DialogService.Warning("Could not read image from clipboard.", "TOTP import");
                return;
            }

            ApplyTotpImport(image);
        }
        catch (Exception ex)
        {
            DialogService.Warning($"Clipboard import failed:\n{ex.Message}", "TOTP import");
        }
    }

    private void ApplyTotpImport(BitmapSource image)
    {
        if (!QrTotpImport.TryDecode(image, out var result, out var error) || result is null)
        {
            DialogService.Warning(error ?? "Could not decode QR code.", "TOTP import");
            return;
        }

        TotpSecret = result.Secret;

        // Fill empty Title / Username from QR metadata when helpful
        if (string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(result.Issuer))
            Title = result.Issuer!;
        else if (string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(result.Account))
            Title = result.Account!;

        if (string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(result.Account))
            Username = result.Account!;

        ValidateTotpSecret();

        if (!string.IsNullOrEmpty(TotpErrorMessage))
        {
            DialogService.Warning(
                $"QR was read, but the secret is not valid Base32:\n{TotpErrorMessage}",
                "TOTP import");
            return;
        }

        var who = string.Join(" · ", new[] { result.Issuer, result.Account }.Where(s => !string.IsNullOrWhiteSpace(s)));
        DialogService.Success(
            string.IsNullOrEmpty(who)
                ? "TOTP secret imported from QR code."
                : $"TOTP secret imported.\n{who}",
            "TOTP import");
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
    private static void TogglePasswordHistoryReveal(PasswordHistoryRow? row)
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
                passwordHistory = [.. passwordHistory.Take(VaultEntry.MaxPasswordHistory)];

            if (usernameHistory.Count > VaultEntry.MaxUsernameHistory)
                usernameHistory = [.. usernameHistory.Take(VaultEntry.MaxUsernameHistory)];

            return new VaultEntry
            {
                Id = Original.Id,
                Title = Title.Trim(),
                Username = newUsername,
                Password = Password,
                Url = Url.Trim(),
                TotpSecret = NormalizeTotpSecret(TotpSecret),
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
            TotpSecret = NormalizeTotpSecret(TotpSecret),
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
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSecret { get; set; }
}

public partial class PasswordHistoryRow(PasswordHistoryItem item) : ObservableObject
{
    public string Password { get; } = item.Password;
    public DateTimeOffset ChangedAt { get; } = item.ChangedAt;
    public string ChangedAtText { get; } = item.ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

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

public sealed class UsernameHistoryRow(UsernameHistoryItem item)
{
    public string Username { get; } = item.Username;
    public DateTimeOffset ChangedAt { get; } = item.ChangedAt;
    public string ChangedAtText { get; } = item.ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
