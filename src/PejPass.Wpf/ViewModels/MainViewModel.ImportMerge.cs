using PejPass.Domain.Entities;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    private static ImportMergeResult MergeImportedEntries(
        Vault vault,
        IReadOnlyList<VaultEntry> imported,
        bool includeTags = true)
    {
        var activeByFp = new Dictionary<string, VaultEntry>(StringComparer.Ordinal);
        foreach (var entry in vault.Entries)
            activeByFp[EntryContentFingerprint(entry, includeTags)] = entry;

        var trashByFp = new Dictionary<string, TrashedEntry>(StringComparer.Ordinal);
        foreach (var trashed in vault.Trash)
            trashByFp[EntryContentFingerprint(trashed.Entry, includeTags)] = trashed;

        var addedToList = 0;
        var restoredFromTrash = 0;
        var skippedAlreadyInList = 0;

        foreach (var importedEntry in imported)
        {
            var fp = EntryContentFingerprint(importedEntry, includeTags);

            if (activeByFp.ContainsKey(fp))
            {
                skippedAlreadyInList++;
                continue;
            }

            if (trashByFp.TryGetValue(fp, out var trashed))
            {
                vault.Trash.Remove(trashed);
                trashed.Entry.Touch();
                vault.Entries.Add(trashed.Entry);

                activeByFp[fp] = trashed.Entry;
                trashByFp.Remove(fp);
                restoredFromTrash++;
                continue;
            }

            var clone = CloneImportedEntry(importedEntry);
            vault.AddEntry(clone);
            activeByFp[fp] = clone;
            addedToList++;
        }

        return new ImportMergeResult(
            addedToList,
            restoredFromTrash,
            skippedAlreadyInList);
    }

    private static VaultEntry CloneImportedEntry(VaultEntry source)
    {
        return new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = source.Title,
            Username = source.Username,
            Password = source.Password,
            Url = source.Url,
            TotpSecret = source.TotpSecret,
            Notes = source.Notes,
            Tags = [.. source.Tags],
            CustomFields = [.. source.CustomFields.Select(field => new CustomField
            {
                Name = field.Name,
                Value = field.Value,
                IsSecret = field.IsSecret
            })],
            PasswordHistory = [.. source.PasswordHistory.Select(history => new PasswordHistoryItem
            {
                Password = history.Password,
                ChangedAt = history.ChangedAt
            })],
            UsernameHistory = [.. source.UsernameHistory.Select(history => new UsernameHistoryItem
            {
                Username = history.Username,
                ChangedAt = history.ChangedAt
            })],
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private readonly record struct ImportMergeResult(
        int AddedToList,
        int RestoredFromTrash,
        int SkippedAlreadyInList);
}
