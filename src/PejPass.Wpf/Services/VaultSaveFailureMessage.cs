using PejPass.Domain.Exceptions;

namespace PejPass.Wpf.Services;

internal static class VaultSaveFailureMessage
{
    private const string HistoryLimitMessage =
        "The vault has reached the maximum supported number of history snapshots. Delete older history snapshots or delete all history, then try again.";

    public static string For(Exception exception, string fallbackMessage) =>
        exception is VaultLimitExceededException limit
            ? Describe(limit, fallbackMessage)
            : fallbackMessage;

    private static string Describe(VaultLimitExceededException ex, string fallbackMessage)
    {
        var entry = string.IsNullOrWhiteSpace(ex.EntryTitle)
            ? "an entry"
            : $"the entry \"{ex.EntryTitle}\"";

        return ex.Limit switch
        {
            VaultLimit.HistoryItems => HistoryLimitMessage,

            VaultLimit.Entries =>
                $"The vault has reached the maximum supported number of entries ({VaultLimits.MaxEntries:N0}, including entries in Trash). " +
                "Empty the Trash or delete entries you no longer need, then try again.",

            VaultLimit.TextLength =>
                $"A field exceeds the vault's maximum text length ({ex.Subject ?? "text field"}" +
                (string.IsNullOrWhiteSpace(ex.EntryTitle) ? string.Empty : $" in \"{ex.EntryTitle}\"") +
                $"). Each text value can contain at most {VaultLimits.MaxTextCharacters:N0} characters. Shorten it and try again.",

            VaultLimit.CollectionItems =>
                $"{Capitalize(entry)} has too many {ex.Subject ?? "tags or custom fields"} " +
                $"(the limit is {VaultLimits.MaxCollectionItemsPerEntry:N0}). Remove some and try again.",

            VaultLimit.TagLength =>
                $"A tag in {entry} is too long (the limit is {VaultLimits.MaxTagCharacters:N0} characters). Shorten it and try again.",

            _ => fallbackMessage
        };
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
