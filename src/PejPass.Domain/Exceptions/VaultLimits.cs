namespace PejPass.Domain.Exceptions;

/// <summary>Single source of truth for the vault size limits enforced by VaultStore and shown to users.</summary>
public static class VaultLimits
{
    /// <summary>Active plus trashed entries.</summary>
    public const int MaxEntries = 100_000;

    public const int MaxHistoryItems = 1_000_000;

    /// <summary>Tags, or custom fields, per entry.</summary>
    public const int MaxCollectionItemsPerEntry = 256;

    public const int MaxTextCharacters = 1_048_576;

    public const int MaxTagCharacters = 4_096;

    /// <summary>Maximum editor text needed to represent the maximum number of maximum-length tags separated by comma-space.</summary>
    public const int MaxTagsTextCharacters =
        MaxTagCharacters * MaxCollectionItemsPerEntry + 2 * (MaxCollectionItemsPerEntry - 1);
}
