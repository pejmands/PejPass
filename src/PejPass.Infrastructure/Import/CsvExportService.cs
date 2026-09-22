using System.Text;
using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;

namespace PejPass.Infrastructure.Import;

/// <summary>
/// Chrome/Edge-style CSV: name,url,username,password,note.
/// Output is plain text — never treat as a secure backup.
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    public async Task ExportToCsvAsync(
        string filePath,
        IEnumerable<VaultEntry> entries,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name,url,username,password,note");

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(Escape(e.Title)).Append(',')
              .Append(Escape(e.Url)).Append(',')
              .Append(Escape(e.Username)).Append(',')
              .Append(Escape(e.Password)).Append(',')
              .Append(Escape(e.Notes))
              .AppendLine();
        }

        // UTF-8 with BOM helps Excel open non-ASCII correctly
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(filePath, sb.ToString(), utf8Bom, ct).ConfigureAwait(false);
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        var needsQuotes = value.Contains(',') || value.Contains('"') ||
                          value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes)
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
