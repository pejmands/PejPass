using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using System.Text;

namespace PejPass.Infrastructure.Import;

/// <summary>
/// Chrome/Edge-style CSV: name,url,username,password,note.
/// Output is plain text — never treat as a secure backup.
/// </summary>
public sealed partial class CsvExportService : ICsvExportService
{
    public async Task ExportToCsvAsync(
        string filePath,
        IEnumerable<VaultEntry> entries,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // UTF-8 with BOM helps Excel open non-ASCII correctly.
        using var writer = new StreamWriter(
            filePath,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteAsync("name,url,username,password,note".AsMemory(), ct).ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            await WriteFieldAsync(writer, entry.Title, ct).ConfigureAwait(false);
            await WriteCommaAsync(writer, ct).ConfigureAwait(false);
            await WriteFieldAsync(writer, entry.Url, ct).ConfigureAwait(false);
            await WriteCommaAsync(writer, ct).ConfigureAwait(false);
            await WriteFieldAsync(writer, entry.Username, ct).ConfigureAwait(false);
            await WriteCommaAsync(writer, ct).ConfigureAwait(false);
            await WriteFieldAsync(writer, entry.Password, ct).ConfigureAwait(false);
            await WriteCommaAsync(writer, ct).ConfigureAwait(false);
            await WriteFieldAsync(writer, entry.Notes, ct).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteFieldAsync(StreamWriter writer, string? value, CancellationToken ct)
    {
        var escaped = Escape(value);
        await writer.WriteAsync(escaped.AsMemory(), ct).ConfigureAwait(false);
    }

    private static Task WriteCommaAsync(StreamWriter writer, CancellationToken ct) =>
        writer.WriteAsync(",".AsMemory(), ct);

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
