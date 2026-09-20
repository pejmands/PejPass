namespace PejPass.Domain.Entities;

/// <summary>
/// In-memory representation of the decrypted vault.
/// The master key is never stored here.
/// </summary>
public sealed class Vault
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Personal Vault";

    public List<VaultEntry> Entries { get; set; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void AddEntry(VaultEntry entry)
    {
        Entries.Add(entry);
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
