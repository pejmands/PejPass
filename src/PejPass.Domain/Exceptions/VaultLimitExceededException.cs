namespace PejPass.Domain.Exceptions;

public enum VaultLimit
{
    HistoryItems,
    Entries,
    TextLength,
    CollectionItems,
    TagLength
}

/// <summary>Indicates that a vault exceeds a supported size or collection limit.</summary>
public sealed class VaultLimitExceededException(VaultLimit limit, string message)
    : Exception(message)
{
    public VaultLimit Limit { get; } = limit;

    /// <summary>What exceeded the limit, for example "entry notes" or "tags".</summary>
    public string? Subject { get; init; }

    /// <summary>Title of the entry that exceeded the limit (shortened), when known.</summary>
    public string? EntryTitle { get; set; }
}
