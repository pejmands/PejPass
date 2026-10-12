using System.IO;

namespace PejPass.Infrastructure.Import;

/// <summary>
/// Remembers unencrypted temp files (CSV exports in progress) so that leftovers of a killed
/// process can be deleted on the next start. Every operation is best-effort and never throws.
/// </summary>
public sealed class PlaintextTempJournal
{
    private const string Suffix = ".tmp";
    private const int GuidLength = 32;

    private readonly string _journalPath;
    private readonly object _sync = new();

    public PlaintextTempJournal(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        _journalPath = Path.GetFullPath(journalPath);
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass",
            "pending-plaintext-temp.txt");

    public static PlaintextTempJournal Default { get; } = new(DefaultPath);

    /// <summary>Call before the temp file is created.</summary>
    public void Add(string tempPath)
    {
        lock (_sync)
        {
            try
            {
                var fullPath = Path.GetFullPath(tempPath);
                Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
                File.AppendAllLines(_journalPath, [fullPath]);
            }
            catch
            {
                // Journaling must never break an export.
            }
        }
    }

    /// <summary>Forgets the entry only after the file is gone (moved or deleted).</summary>
    public void RemoveIfGone(string tempPath)
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(tempPath))
                    return;

                var fullPath = Path.GetFullPath(tempPath);
                Rewrite(Read().Where(line =>
                    !string.Equals(line, fullPath, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                // Best effort; a stale journal entry will be retried on the next start.
            }
        }
    }

    public IReadOnlyList<string> Pending()
    {
        lock (_sync)
        {
            try
            {
                return Read();
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Deletes journaled temp files that still exist. Entries that do not match the expected
    /// temp-file pattern are dropped without touching the referenced file.
    /// </summary>
    public int DeleteLeftovers()
    {
        lock (_sync)
        {
            try
            {
                var remaining = new List<string>();
                var deleted = 0;

                foreach (var path in Read())
                {
                    if (!IsOurTempPath(path))
                        continue;

                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            deleted++;
                        }
                    }
                    catch
                    {
                        remaining.Add(path); // Retry locked files on the next start.
                    }
                }

                Rewrite(remaining);
                return deleted;
            }
            catch
            {
                return 0;
            }
        }
    }

    private List<string> Read()
    {
        if (!File.Exists(_journalPath))
            return [];

        return [.. File.ReadAllLines(_journalPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)];
    }

    private void Rewrite(IEnumerable<string> lines)
    {
        var list = lines.ToList();

        if (list.Count == 0)
        {
            if (File.Exists(_journalPath))
                File.Delete(_journalPath);

            return;
        }

        File.WriteAllLines(_journalPath, list);
    }

    /// <summary>Absolute path whose file name is ".{name}.{32 hex}.tmp".</summary>
    private static bool IsOurTempPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var name = Path.GetFileName(path);

        if (!name.StartsWith('.') ||
            !name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var core = name[..^Suffix.Length];
        var dot = core.Length - (GuidLength + 1);

        if (dot < 2 || core[dot] != '.')
            return false;

        foreach (var c in core.AsSpan(dot + 1))
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }
}
