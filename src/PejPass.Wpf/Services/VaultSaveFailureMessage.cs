using System.IO;

namespace PejPass.Wpf.Services;

internal static class VaultSaveFailureMessage
{
    private const string HistoryLimitMessage =
        "The vault has reached the maximum supported number of history snapshots. Delete older history snapshots or delete all history, then try again.";

    public static string For(Exception exception, string fallbackMessage)
    {
        if (exception is InvalidDataException &&
            exception.Message.Contains("history items", StringComparison.OrdinalIgnoreCase))
        {
            return HistoryLimitMessage;
        }

        return fallbackMessage;
    }
}
