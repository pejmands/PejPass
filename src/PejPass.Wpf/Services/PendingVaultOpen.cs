using System.IO;

namespace PejPass.Wpf.Services;

/// <summary>
/// Tiny IPC: second instance writes a vault path; the first instance reads it on activation.
/// </summary>
internal static class PendingVaultOpen
{
    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass",
            "pending-open.txt");

    public static void Write(string vaultPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, vaultPath.Trim());
        }
        catch
        {
            // ignore IPC failures
        }
    }

    public static string? ReadAndClear()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var text = File.ReadAllText(FilePath).Trim();
            try { File.Delete(FilePath); } catch { /* ignore */ }

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
