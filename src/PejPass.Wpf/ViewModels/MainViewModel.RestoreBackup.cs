using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Views;

namespace PejPass.Wpf.ViewModels;

/// <summary>
/// Restore encrypted .pejpass backup into the currently open vault.
/// This is NOT the same as Login → Open vault (which switches the active file).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var currentPath = LoginViewModel.CurrentVaultPath;
        var currentVault = LoginViewModel.CurrentVault;
        var currentPassword = LoginViewModel.CurrentMasterPassword;

        if (string.IsNullOrEmpty(currentPath) || currentVault is null || string.IsNullOrEmpty(currentPassword))
        {
            DialogService.Warning("No vault is open.", "Restore Backup");
            return;
        }

        var openDlg = new OpenFileDialog
        {
            Title = "Restore from encrypted PejPass backup",
            Filter = "PejPass Vault (*.pejpass)|*.pejpass|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (openDlg.ShowDialog() != true)
            return;

        var backupPath = System.IO.Path.GetFullPath(openDlg.FileName);
        var activePath = System.IO.Path.GetFullPath(currentPath);

        if (string.Equals(backupPath, activePath, StringComparison.OrdinalIgnoreCase))
        {
            DialogService.Warning(
                "That is the vault file you already have open.\n\n" +
                "Pick a different .pejpass backup file.",
                "Restore Backup");
            return;
        }

        var prompt = new PasswordPromptWindow(
            title: "Backup password",
            message: "Enter the master password used when this backup was created:")
        {
            Owner = GetOwnerWindow()
        };

        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Password))
            return;

        Vault backupVault;
        try
        {
            StatusMessage = "Opening backup…";
            backupVault = await _vaultService.OpenVaultAsync(backupPath, prompt.Password);
        }
        catch (Exception ex)
        {
            DialogService.Error(
                $"Could not open backup (wrong password or corrupt file):\n{ex.Message}",
                "Restore Backup");
            StatusMessage = "Restore failed.";
            return;
        }

        var entryCount = backupVault.Entries.Count;
        var trashCount = backupVault.Trash.Count;

        var choice = DialogService.Choose(
            $"Backup contains {entryCount} entries" +
            (trashCount > 0 ? $" and {trashCount} in trash" : "") + ".\n\n" +
            "• Replace — overwrite data in the CURRENT vault file\n" +
            "  (path stays the same; re-encrypted with your current password)\n\n" +
            "• Merge — add entries that are not a 100% content match\n" +
            "  (any difference in title, username, password, URL, notes,\n" +
            "   TOTP, tags, or custom fields → treated as new and added)\n\n" +
            "This is different from Login → Open vault, which would switch to the backup file itself.",
            "Restore mode",
            primaryText: "Replace",
            secondaryText: "Merge",
            tertiaryText: "Cancel");

        if (choice == AppDialogResult.Tertiary || choice == AppDialogResult.None)
        {
            StatusMessage = "Restore cancelled.";
            return;
        }

        var replace = choice == AppDialogResult.Primary;

        try
        {
            if (replace)
            {
                if (!DialogService.Confirm(
                        "This will overwrite ALL entries in your current vault with the backup.\n\n" +
                        "The vault file path will not change.\n\n" +
                        "This cannot be undone unless you have another backup.\n\nContinue?",
                        "Confirm replace",
                        yesText: "Replace vault",
                        noText: "Cancel"))
                {
                    StatusMessage = "Restore cancelled.";
                    return;
                }

                currentVault.Entries.Clear();
                currentVault.Trash.Clear();
                foreach (var e in backupVault.Entries)
                    currentVault.Entries.Add(CloneEntry(e));
                foreach (var tr in backupVault.Trash)
                {
                    currentVault.Trash.Add(new TrashedEntry
                    {
                        Entry = CloneEntry(tr.Entry),
                        DeletedAt = tr.DeletedAt
                    });
                }
                currentVault.Name = backupVault.Name;
                currentVault.UpdatedAt = DateTimeOffset.UtcNow;

                Entries.Clear();
                foreach (var e in currentVault.Entries)
                    Entries.Add(e);

                SelectedEntry = null;
                ApplyFilter(preserveSelectionId: null);
                await SaveVaultAsync();

                StatusMessage = $"Vault restored from backup ({currentVault.Entries.Count} entries).";
                DialogService.Success(
                    $"Current vault replaced with backup data.\n\n" +
                    $"{currentVault.Entries.Count} entries saved to:\n{currentPath}",
                    "Restore complete");
            }
            else
            {
                // Merge: skip only when content is 100% identical
                var existingKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in currentVault.Entries)
                    existingKeys.Add(EntryContentFingerprint(e));
                foreach (var tr in currentVault.Trash)
                    existingKeys.Add(EntryContentFingerprint(tr.Entry));

                var added = 0;
                var skipped = 0;

                foreach (var e in backupVault.Entries)
                {
                    var key = EntryContentFingerprint(e);
                    if (existingKeys.Contains(key))
                    {
                        skipped++;
                        continue;
                    }

                    currentVault.AddEntry(CloneEntry(e, newId: true));
                    existingKeys.Add(key);
                    added++;
                }

                foreach (var tr in backupVault.Trash)
                {
                    var key = EntryContentFingerprint(tr.Entry);
                    if (existingKeys.Contains(key))
                    {
                        skipped++;
                        continue;
                    }

                    currentVault.Trash.Add(new TrashedEntry
                    {
                        Entry = CloneEntry(tr.Entry, newId: true),
                        DeletedAt = tr.DeletedAt
                    });
                    existingKeys.Add(key);
                    added++;
                }

                currentVault.UpdatedAt = DateTimeOffset.UtcNow;

                Entries.Clear();
                foreach (var e in currentVault.Entries)
                    Entries.Add(e);

                SelectedEntry = null;
                ApplyFilter(preserveSelectionId: null);
                await SaveVaultAsync();

                StatusMessage = skipped > 0
                    ? $"Merged {added} entries ({skipped} 100% identical skipped)."
                    : $"Merged {added} entries from backup.";

                DialogService.Success(
                    $"Merge finished.\n\n" +
                    $"Added: {added}\n" +
                    $"Skipped (100% identical): {skipped}\n\n" +
                    $"Saved to:\n{currentPath}",
                    "Restore complete");
            }

            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error($"Restore failed:\n{ex.Message}", "Restore Backup");
            StatusMessage = "Restore failed.";
        }
    }

    /// <summary>
    /// Full content fingerprint. Skip on merge only when every content field matches exactly
    /// (Ordinal, no case folding). Id / dates / history / sort order are ignored.
    /// </summary>
    private static string EntryContentFingerprint(VaultEntry e)
    {
        static string S(string? s) => s ?? string.Empty;

        // Tags: order-independent
        var tags = string.Join('\u001e',
            e.Tags.Select(t => S(t)).OrderBy(t => t, StringComparer.Ordinal));

        // Custom fields: order-independent by name|value|IsSecret
        var customs = string.Join('\u001e',
            e.CustomFields
                .Select(f => $"{S(f.Name)}\u001d{S(f.Value)}\u001d{(f.IsSecret ? '1' : '0')}")
                .OrderBy(x => x, StringComparer.Ordinal));

        return string.Join('\u001f',
            S(e.Title),
            S(e.Username),
            S(e.Password),
            S(e.Url),
            S(e.TotpSecret),
            S(e.Notes),
            tags,
            customs,
            e.IsFavorite ? "1" : "0");
    }

    private static VaultEntry CloneEntry(VaultEntry source, bool newId = false)
    {
        return new VaultEntry
        {
            Id = newId ? Guid.NewGuid() : source.Id,
            Title = source.Title,
            Username = source.Username,
            Password = source.Password,
            Url = source.Url,
            TotpSecret = source.TotpSecret,
            Notes = source.Notes,
            Tags = source.Tags.ToList(),
            CustomFields = source.CustomFields.Select(f => new CustomField
            {
                Name = f.Name,
                Value = f.Value,
                IsSecret = f.IsSecret
            }).ToList(),
            PasswordHistory = source.PasswordHistory.Select(h => new PasswordHistoryItem
            {
                Password = h.Password,
                ChangedAt = h.ChangedAt
            }).ToList(),
            UsernameHistory = source.UsernameHistory.Select(h => new UsernameHistoryItem
            {
                Username = h.Username,
                ChangedAt = h.ChangedAt
            }).ToList(),
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
