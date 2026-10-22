namespace PejPass.Infrastructure.Storage;

/// <summary>
/// Removes leftovers of interrupted saves: ".{vault file name}.{32 hex}.tmp" next to the vault.
/// Only files that match that exact pattern and are older than <see cref="MinimumAge"/> are deleted,
/// so a save that is running right now is never touched. Never throws.
/// </summary>
public static class VaultTempFileCleaner
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(1);

    private const string Suffix = ".tmp";
    private const int GuidLength = 32;

    public static int DeleteStale(string vaultPath, DateTime? utcNow = null)
    {
        try
        {
            var fullPath = Path.GetFullPath(vaultPath);
            var directory = Path.GetDirectoryName(fullPath);

            if (directory is null || !Directory.Exists(directory))
                return 0;

            var prefix = "." + Path.GetFileName(fullPath) + ".";
            var now = utcNow ?? DateTime.UtcNow;
            var deleted = 0;

            foreach (var file in Directory.EnumerateFiles(directory, prefix + "*" + Suffix))
            {
                if (!IsTempFileName(Path.GetFileName(file), prefix))
                    continue;

                try
                {
                    if (now - File.GetLastWriteTimeUtc(file) < MinimumAge)
                        continue;

                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    // Locked or already gone; try again next time.
                }
            }

            return deleted;
        }
        catch
        {
            return 0;
        }
    }

    internal static bool IsTempFileName(string fileName, string prefix)
    {
        return fileName.Length == prefix.Length + GuidLength + Suffix.Length &&
               fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) &&
               IsHex(fileName.AsSpan(prefix.Length, GuidLength));
    }

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }
}
