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

        // Yes = Replace, No = Merge (labels make the choice explicit)
        var replace = DialogService.Confirm(
            $"Backup contains {entryCount} entries" +
            (trashCount > 0 ? $" and {trashCount} in trash" : "") + ".\n\n" +
            "• Replace — overwrite data in the CURRENT vault file\n" +
            "  (path stays the same; re-encrypted with your current password)\n\n" +
            "• Merge — add backup entries into the current vault (new IDs)\n\n" +
            "This is different from Login → Open vault, which would switch to the backup file itself.\n\n" +
            "Replace current vault data with this backup?",
            "Restore mode",
            yesText: "Replace",
            noText: "Merge");

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
            }
            else
            {
                foreach (var e in backupVault.Entries)
                    currentVault.AddEntry(CloneEntry(e, newId: true));

                foreach (var tr in backupVault.Trash)
                {
                    currentVault.Trash.Add(new TrashedEntry
                    {
                        Entry = CloneEntry(tr.Entry, newId: true),
                        DeletedAt = tr.DeletedAt
                    });
                }
                currentVault.UpdatedAt = DateTimeOffset.UtcNow;
            }

            Entries.Clear();
            foreach (var e in currentVault.Entries)
                Entries.Add(e);

            SelectedEntry = null;
            ApplyFilter(preserveSelectionId: null);
            await SaveVaultAsync();

            StatusMessage = replace
                ? $"Vault restored from backup ({currentVault.Entries.Count} entries)."
                : $"Merged {entryCount} entries from backup.";

            DialogService.Success(
                replace
                    ? $"Current vault replaced with backup data.\n\n" +
                      $"{currentVault.Entries.Count} entries saved to:\n{currentPath}"
                    : $"Merged {entryCount} entries from backup into:\n{currentPath}",
                "Restore complete");

            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error($"Restore failed:\n{ex.Message}", "Restore Backup");
            StatusMessage = "Restore failed.";
        }
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
