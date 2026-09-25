namespace PejPass.Domain.Entities;

/// <summary>
/// In-memory representation of the decrypted vault.
/// The master key is never stored here.
/// </summary>
public sealed class Vault
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Personal Vault";

    public List<VaultEntry> Entries { get; set; } = [];

    public List<EntryHistoryItem> History { get; set; } = [];

    /// <summary>Soft-deleted entries (recoverable until purged).</summary>
    public List<TrashedEntry> Trash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Entries older than this in trash are permanently removed.</summary>
    public static TimeSpan TrashRetention { get; } = TimeSpan.FromDays(30);

    public const int MaxHistoryPerEntry = 10;

    public void AddEntry(VaultEntry entry)
    {
        Entries.Add(entry);
        Touch();
    }

    public bool UpdateEntry(VaultEntry updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        var index = Entries.FindIndex(e => e.Id == updated.Id);
        if (index < 0)
            return false;

        var current = Entries[index];

        if (AreEntriesEqual(current, updated))
            return false;

        AddHistory(current);
        Entries[index] = updated;
        Touch();

        return true;
    }

    public bool SetFavorite(Guid entryId, bool isFavorite)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null || entry.IsFavorite == isFavorite)
            return false;

        entry.IsFavorite = isFavorite;
        Touch();

        return true;
    }

    public void AddHistory(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        History.Insert(0, new EntryHistoryItem
        {
            EntryId = entry.Id,
            Title = entry.Title,
            Username = entry.Username,
            Password = entry.Password,
            Url = entry.Url,
            TotpSecret = entry.TotpSecret,
            Notes = entry.Notes,
            Tags = [.. entry.Tags],
            CustomFields = [.. entry.CustomFields
                .Select(field => new CustomField
                {
                    Name = field.Name,
                    Value = field.Value,
                    IsSecret = field.IsSecret
                })],
            SortOrder = entry.SortOrder,
            CreatedAt = entry.CreatedAt,
            ChangedAt = DateTimeOffset.UtcNow
        });

        TrimHistory(entry.Id);
        Touch();
    }

    private void TrimHistory(Guid entryId)
    {
        var entryHistory = History
            .Where(h => h.EntryId == entryId)
            .OrderByDescending(h => h.ChangedAt)
            .Skip(MaxHistoryPerEntry)
            .ToList();

        foreach (var item in entryHistory)
            History.Remove(item);
    }

    private static bool AreEntriesEqual(VaultEntry left, VaultEntry right)
    {
        if (left.Id != right.Id ||
            !string.Equals(left.Title, right.Title, StringComparison.Ordinal) ||
            !string.Equals(left.Username, right.Username, StringComparison.Ordinal) ||
            !string.Equals(left.Password, right.Password, StringComparison.Ordinal) ||
            !string.Equals(left.Url, right.Url, StringComparison.Ordinal) ||
            !string.Equals(left.TotpSecret, right.TotpSecret, StringComparison.Ordinal) ||
            !string.Equals(left.Notes, right.Notes, StringComparison.Ordinal) ||
            left.SortOrder != right.SortOrder ||
            left.CreatedAt != right.CreatedAt)
        {
            return false;
        }

        if (!left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal))
            return false;

        if (left.CustomFields.Count != right.CustomFields.Count)
            return false;

        for (var i = 0; i < left.CustomFields.Count; i++)
        {
            var a = left.CustomFields[i];
            var b = right.CustomFields[i];

            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !string.Equals(a.Value, b.Value, StringComparison.Ordinal) ||
                a.IsSecret != b.IsSecret)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHistoryCurrent(
    VaultEntry entry,
    EntryHistoryItem history)
    {
        if (!string.Equals(entry.Title, history.Title, StringComparison.Ordinal) ||
            !string.Equals(entry.Username, history.Username, StringComparison.Ordinal) ||
            !string.Equals(entry.Password, history.Password, StringComparison.Ordinal) ||
            !string.Equals(entry.Url, history.Url, StringComparison.Ordinal) ||
            !string.Equals(entry.TotpSecret, history.TotpSecret, StringComparison.Ordinal) ||
            !string.Equals(entry.Notes, history.Notes, StringComparison.Ordinal) ||
            entry.SortOrder != history.SortOrder ||
            entry.CreatedAt != history.CreatedAt)
        {
            return false;
        }

        if (!entry.Tags.SequenceEqual(
                history.Tags,
                StringComparer.Ordinal))
        {
            return false;
        }

        if (entry.CustomFields.Count != history.CustomFields.Count)
            return false;

        for (var i = 0; i < entry.CustomFields.Count; i++)
        {
            var current = entry.CustomFields[i];
            var snapshot = history.CustomFields[i];

            if (!string.Equals(
                    current.Name,
                    snapshot.Name,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    current.Value,
                    snapshot.Value,
                    StringComparison.Ordinal) ||
                current.IsSecret != snapshot.IsSecret)
            {
                return false;
            }
        }

        return true;
    }

    public bool RestoreHistory(EntryHistoryItem historyItem)
    {
        ArgumentNullException.ThrowIfNull(historyItem);

        var fields = new[]
        {
        EntryHistoryField.Title,
        EntryHistoryField.Username,
        EntryHistoryField.Password,
        EntryHistoryField.Url,
        EntryHistoryField.TotpSecret,
        EntryHistoryField.Notes,
        EntryHistoryField.Tags,
        EntryHistoryField.CustomFields
    };

        return RestoreHistoryFields(historyItem, fields);
    }

    public bool RestoreHistoryFields(
        EntryHistoryItem historyItem,
        IEnumerable<EntryHistoryField> fields)
    {
        ArgumentNullException.ThrowIfNull(historyItem);
        ArgumentNullException.ThrowIfNull(fields);

        var entry = Entries.FirstOrDefault(
            e => e.Id == historyItem.EntryId);

        if (entry is null)
            return false;

        var selectedFields = fields
            .Distinct()
            .ToList();

        if (selectedFields.Count == 0)
            return false;

        var changedFields = selectedFields
            .Where(selectedField =>
                !IsHistoryFieldEqual(
                    entry,
                    historyItem,
                    selectedField))
            .ToList();

        if (changedFields.Count == 0)
            return false;

        // Create one snapshot of the current state before applying all changes.
        AddHistory(entry);

        foreach (var selectedField in changedFields)
        {
            switch (selectedField)
            {
                case EntryHistoryField.Title:
                    entry.Title = historyItem.Title;
                    break;

                case EntryHistoryField.Username:
                    entry.PushUsernameHistory(entry.Username);
                    entry.Username = historyItem.Username;
                    break;

                case EntryHistoryField.Password:
                    entry.PushPasswordHistory(entry.Password);
                    entry.Password = historyItem.Password;
                    break;

                case EntryHistoryField.Url:
                    entry.Url = historyItem.Url;
                    break;

                case EntryHistoryField.TotpSecret:
                    entry.TotpSecret = historyItem.TotpSecret;
                    break;

                case EntryHistoryField.Notes:
                    entry.Notes = historyItem.Notes;
                    break;

                case EntryHistoryField.Tags:
                    entry.Tags = [.. historyItem.Tags];
                    break;

                case EntryHistoryField.CustomFields:
                    entry.CustomFields = [.. historyItem.CustomFields
                    .Select(customField => new CustomField
                    {
                        Name = customField.Name,
                        Value = customField.Value,
                        IsSecret = customField.IsSecret
                    })];
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(fields),
                        selectedField,
                        null);
            }
        }

        entry.Touch();
        Touch();

        return true;
    }

    public bool RestoreHistoryField(
        EntryHistoryItem historyItem,
        EntryHistoryField selectedField)
    {
        return RestoreHistoryFields(
            historyItem,
            [selectedField]);
    }

    private static bool IsHistoryFieldEqual(
        VaultEntry entry,
        EntryHistoryItem historyItem,
        EntryHistoryField selectedField)
    {
        return selectedField switch
        {
            EntryHistoryField.Title =>
                string.Equals(
                    entry.Title,
                    historyItem.Title,
                    StringComparison.Ordinal),

            EntryHistoryField.Username =>
                string.Equals(
                    entry.Username,
                    historyItem.Username,
                    StringComparison.Ordinal),

            EntryHistoryField.Password =>
                string.Equals(
                    entry.Password,
                    historyItem.Password,
                    StringComparison.Ordinal),

            EntryHistoryField.Url =>
                string.Equals(
                    entry.Url,
                    historyItem.Url,
                    StringComparison.Ordinal),

            EntryHistoryField.TotpSecret =>
                string.Equals(
                    entry.TotpSecret,
                    historyItem.TotpSecret,
                    StringComparison.Ordinal),

            EntryHistoryField.Notes =>
                string.Equals(
                    entry.Notes,
                    historyItem.Notes,
                    StringComparison.Ordinal),

            EntryHistoryField.Tags =>
                entry.Tags.SequenceEqual(
                    historyItem.Tags,
                    StringComparer.Ordinal),

            EntryHistoryField.CustomFields =>
                CustomFieldsEqual(
                    entry.CustomFields,
                    historyItem.CustomFields),

            _ => false
        };
    }

    private static bool CustomFieldsEqual(
        List<CustomField> left,
        List<CustomField> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(
                    left[i].Name,
                    right[i].Name,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    left[i].Value,
                    right[i].Value,
                    StringComparison.Ordinal) ||
                left[i].IsSecret != right[i].IsSecret)
            {
                return false;
            }
        }

        return true;
    }

    public bool SoftDelete(Guid entryId)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return false;

        Entries.Remove(entry);
        Trash.Add(new TrashedEntry { Entry = entry, DeletedAt = DateTimeOffset.UtcNow });
        Touch();
        return true;
    }

    public bool RestoreFromTrash(Guid entryId)
    {
        var item = Trash.FirstOrDefault(t => t.Entry.Id == entryId);
        if (item is null) return false;

        Trash.Remove(item);
        item.Entry.Touch();
        Entries.Add(item.Entry);
        Touch();
        return true;
    }

    public bool PurgeFromTrash(Guid entryId)
    {
        var removed = Trash.RemoveAll(t => t.Entry.Id == entryId) > 0;

        if (removed)
        {
            History.RemoveAll(h => h.EntryId == entryId);
            Touch();
        }

        return removed;
    }

    public int PurgeExpiredTrash(DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow) - TrashRetention;

        var expiredEntryIds = Trash
            .Where(t => t.DeletedAt < cutoff)
            .Select(t => t.Entry.Id)
            .ToHashSet();

        if (expiredEntryIds.Count == 0)
            return 0;

        Trash.RemoveAll(t => expiredEntryIds.Contains(t.Entry.Id));

        History.RemoveAll(h => expiredEntryIds.Contains(h.EntryId));

        Touch();

        return expiredEntryIds.Count;
    }

    public void EmptyTrash()
    {
        if (Trash.Count == 0) return;

        foreach (var item in Trash)
            History.RemoveAll(h => h.EntryId == item.Entry.Id);

        Trash.Clear();
        Touch();
    }

    public bool RemoveEntry(Guid entryId)
    {
        var removed = Entries.RemoveAll(e => e.Id == entryId) > 0;
        if (removed) Touch();
        return removed;
    }

    public VaultEntry? FindEntry(Guid entryId) =>
        Entries.FirstOrDefault(e => e.Id == entryId);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
