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
            IsFavorite = entry.IsFavorite,
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
            left.IsFavorite != right.IsFavorite ||
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

    public bool RestoreHistory(EntryHistoryItem historyItem)
    {
        ArgumentNullException.ThrowIfNull(historyItem);

        var index = Entries.FindIndex(e => e.Id == historyItem.EntryId);
        if (index < 0)
            return false;

        var current = Entries[index];

        AddHistory(current);

        Entries[index] = new VaultEntry
        {
            Id = historyItem.EntryId,
            Title = historyItem.Title,
            Username = historyItem.Username,
            Password = historyItem.Password,
            Url = historyItem.Url,
            TotpSecret = historyItem.TotpSecret,
            Notes = historyItem.Notes,
            Tags = [.. historyItem.Tags],
            CustomFields = [.. historyItem.CustomFields
                .Select(field => new CustomField
                {
                    Name = field.Name,
                    Value = field.Value,
                    IsSecret = field.IsSecret
                })],
            IsFavorite = historyItem.IsFavorite,
            SortOrder = historyItem.SortOrder,
            CreatedAt = historyItem.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Touch();

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
