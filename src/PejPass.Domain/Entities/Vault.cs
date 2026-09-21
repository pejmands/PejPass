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

    /// <summary>Soft-deleted entries (recoverable until purged).</summary>
    public List<TrashedEntry> Trash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Entries older than this in trash are permanently removed.</summary>
    public static TimeSpan TrashRetention { get; } = TimeSpan.FromDays(30);

    public void AddEntry(VaultEntry entry)
    {
        Entries.Add(entry);
        Touch();
    }

    /// <summary>Move entry to trash (soft delete).</summary>
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
        if (removed) Touch();
        return removed;
    }

    public int PurgeExpiredTrash(DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow) - TrashRetention;
        var before = Trash.Count;
        Trash.RemoveAll(t => t.DeletedAt < cutoff);
        var removed = before - Trash.Count;
        if (removed > 0) Touch();
        return removed;
    }

    public void EmptyTrash()
    {
        if (Trash.Count == 0) return;
        Trash.Clear();
        Touch();
    }

    /// <summary>Legacy hard remove — prefer SoftDelete.</summary>
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
