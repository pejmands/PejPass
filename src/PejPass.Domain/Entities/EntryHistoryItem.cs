namespace PejPass.Domain.Entities;

/// <summary>
/// A snapshot of an entry at a specific point in time.
/// </summary>
public sealed class EntryHistoryItem
{
    public Guid EntryId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string TotpSecret { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public List<string> Tags { get; init; } = [];

    public List<CustomField> CustomFields { get; init; } = [];

    public int SortOrder { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;
}
