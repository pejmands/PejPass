using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
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
    public partial string PasswordStrengthLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double PasswordStrengthProgress { get; set; }

    [ObservableProperty]
    public partial int PasswordStrengthLevel { get; set; }

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
        SnackbarService.Show(string.IsNullOrEmpty(who)
                ? "TOTP secret imported from QR code."
                : $"TOTP secret imported.\n{who}");
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

        // GroupBy: duplicate field names must not throw (user can add same name twice)
        var newFields = CustomFields
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

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

        if (Original is not null)
        {
            var newUsername = Username.Trim();

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
