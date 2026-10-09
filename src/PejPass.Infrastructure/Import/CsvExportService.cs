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
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(entries);
        ct.ThrowIfCancellationRequested();

        var destinationPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(destinationPath)!;
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            // Write to a sibling temp file so cancellation/failure cannot leave a partial export
            // at the destination or destroy a previously existing export.
            await using (var writer = new StreamWriter(
                tempPath,
                append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
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

                await writer.FlushAsync(ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(tempPath);
            throw;
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

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; never mask the original export error.
        }
    }
}
