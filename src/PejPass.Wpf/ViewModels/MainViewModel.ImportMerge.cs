using PejPass.Domain.Entities;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    private ImportMergeResult MergeImportedEntries(
        Vault vault,
        IReadOnlyList<VaultEntry> imported)
    {
        var activeByFp = new Dictionary<string, VaultEntry>(StringComparer.Ordinal);
        foreach (var entry in vault.Entries)
            activeByFp[EntryContentFingerprint(entry)] = entry;

        var trashByFp = new Dictionary<string, TrashedEntry>(StringComparer.Ordinal);
        foreach (var trashed in vault.Trash)
            trashByFp[EntryContentFingerprint(trashed.Entry)] = trashed;

        var addedToList = 0;
        var restoredFromTrash = 0;
        var skippedAlreadyInList = 0;

        foreach (var importedEntry in imported)
        {
            var fp = EntryContentFingerprint(importedEntry);

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

    private static string EntryContentFingerprint(VaultEntry entry)
    {
        static string S(string? value) => value ?? string.Empty;

        var tags = string.Join(
            '\u001e',
            entry.Tags
                .Select(S)
                .OrderBy(value => value, StringComparer.Ordinal));

        var customFields = string.Join(
            '\u001e',
            entry.CustomFields
                .Select(field =>
                    $"{S(field.Name)}\u001d{S(field.Value)}\u001d{(field.IsSecret ? '1' : '0')}")
                .OrderBy(value => value, StringComparer.Ordinal));

        return string.Join(
            '\u001f',
            S(entry.Title),
            S(entry.Username),
            S(entry.Password),
            S(entry.Url),
            S(entry.TotpSecret),
            S(entry.Notes),
            tags,
            customFields,
            entry.IsFavorite ? "1" : "0");
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
