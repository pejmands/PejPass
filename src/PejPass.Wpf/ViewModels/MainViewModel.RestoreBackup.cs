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
            "• Replace — overwrite data in the CURRENT vault file\n\n" +
            "• Merge rules:\n" +
            "  – 100% identical content only is treated as the same item\n" +
            "  – Backup active + current trash → restored to the list\n" +
            "  – Backup trash + current active → left in the list (not demoted)\n" +
            "  – Backup trash + nothing → added to trash\n\n" +
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
                    $"{currentVault.Entries.Count} entries · {currentVault.Trash.Count} in trash\n\n" +
                    $"Saved to:\n{currentPath}",
                    "Restore complete");
            }
            else
            {
                // ---- Merge with explicit active-vs-trash rules ----
                // Map fingerprint → entry (or trashed entry) in the CURRENT vault
                var activeByFp = new Dictionary<string, VaultEntry>(StringComparer.Ordinal);
                foreach (var e in currentVault.Entries)
                    activeByFp[EntryContentFingerprint(e)] = e;

                var trashByFp = new Dictionary<string, TrashedEntry>(StringComparer.Ordinal);
                foreach (var tr in currentVault.Trash)
                    trashByFp[EntryContentFingerprint(tr.Entry)] = tr;

                int addedToList = 0;
                int restoredFromTrash = 0;
                int skippedAlreadyInList = 0;
                int addedToTrash = 0;
                int skippedTrashAlreadyInList = 0;
                int skippedTrashAlreadyInTrash = 0;

                // 1) Backup ACTIVE entries
                foreach (var e in backupVault.Entries)
                {
                    var fp = EntryContentFingerprint(e);

                    if (activeByFp.ContainsKey(fp))
                    {
                        // Already live in the current list → keep as-is
                        skippedAlreadyInList++;
                        continue;
                    }

                    if (trashByFp.TryGetValue(fp, out var trashed))
                    {
                        // Same content sits in current trash, but backup says it should be active
                        // → bring it back to the list (active wins over trash)
                        currentVault.Trash.Remove(trashed);
                        trashed.Entry.Touch();
                        currentVault.Entries.Add(trashed.Entry);
                        activeByFp[fp] = trashed.Entry;
                        trashByFp.Remove(fp);
                        restoredFromTrash++;
                        continue;
                    }

                    // Brand-new content → add to list
                    var clone = CloneEntry(e, newId: true);
                    currentVault.AddEntry(clone);
                    activeByFp[fp] = clone;
                    addedToList++;
                }

                // 2) Backup TRASH entries
                foreach (var tr in backupVault.Trash)
                {
                    var fp = EntryContentFingerprint(tr.Entry);

                    if (activeByFp.ContainsKey(fp))
                    {
                        // Already live in the list → never demote to trash from a backup
                        skippedTrashAlreadyInList++;
                        continue;
                    }

                    if (trashByFp.ContainsKey(fp))
                    {
                        // Already in trash → leave it
                        skippedTrashAlreadyInTrash++;
                        continue;
                    }

                    var clone = CloneEntry(tr.Entry, newId: true);
                    var item = new TrashedEntry { Entry = clone, DeletedAt = tr.DeletedAt };
                    currentVault.Trash.Add(item);
                    trashByFp[fp] = item;
                    addedToTrash++;
                }

                currentVault.UpdatedAt = DateTimeOffset.UtcNow;

                Entries.Clear();
                foreach (var e in currentVault.Entries)
                    Entries.Add(e);

                SelectedEntry = null;
                ApplyFilter(preserveSelectionId: null);
                await SaveVaultAsync();

                var totalSkipped = skippedAlreadyInList + skippedTrashAlreadyInList + skippedTrashAlreadyInTrash;
                var totalChanged = addedToList + restoredFromTrash + addedToTrash;

                StatusMessage = totalChanged > 0
                    ? $"Merged: +{addedToList} list, {restoredFromTrash} from trash, +{addedToTrash} trash."
                    : "Merge finished — nothing new to add.";

                // Build a clear breakdown so trash effects are visible
                var lines = new List<string>
                {
                    "Merge finished.",
                    "",
                    $"Added to list:              {addedToList}",
                    $"Restored from trash → list: {restoredFromTrash}",
                    $"Added to trash:             {addedToTrash}",
                    $"Skipped (already in list):  {skippedAlreadyInList}",
                };

                if (skippedTrashAlreadyInList > 0)
                    lines.Add($"Skipped trash (kept in list): {skippedTrashAlreadyInList}");
                if (skippedTrashAlreadyInTrash > 0)
                    lines.Add($"Skipped trash (already trash): {skippedTrashAlreadyInTrash}");

                lines.Add("");
                lines.Add($"Vault now: {currentVault.Entries.Count} entries · {currentVault.Trash.Count} in trash");
                lines.Add("");
                lines.Add(currentPath);

                if (restoredFromTrash > 0 || skippedTrashAlreadyInList > 0 || addedToTrash > 0)
                {
                    lines.Add("");
                    lines.Add("Note: trash affected this merge (see counts above).");
                }

                DialogService.Success(string.Join('\n', lines), "Restore complete");
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
    /// Full content fingerprint. Match only when every content field is exactly equal
    /// (Ordinal, case-sensitive). Id / dates / history / sort order are ignored.
    /// </summary>
    private static string EntryContentFingerprint(VaultEntry e)
    {
        static string S(string? s) => s ?? string.Empty;

        var tags = string.Join('\u001e',
            e.Tags.Select(t => S(t)).OrderBy(t => t, StringComparer.Ordinal));

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
