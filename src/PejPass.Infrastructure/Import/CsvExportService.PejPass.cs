using PejPass.Domain.Entities;
using System.Text;
using System.Text.Json;

namespace PejPass.Infrastructure.Import;

public sealed partial class CsvExportService
{
    public async Task ExportPejPassCsvAsync(
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
            // PejPass CSV is unencrypted; keep incomplete exports away from the destination.
            await using (var writer = new StreamWriter(
                tempPath,
                append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteAsync(
                    "name,url,username,password,note,totp_secret,tags,custom_fields,favorite,pejpass_format".AsMemory(),
                    ct).ConfigureAwait(false);
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
                    await WriteCommaAsync(writer, ct).ConfigureAwait(false);
                    await WriteFieldAsync(writer, entry.TotpSecret, ct).ConfigureAwait(false);
                    await WriteCommaAsync(writer, ct).ConfigureAwait(false);
                    await WriteFieldAsync(writer, JsonSerializer.Serialize(entry.Tags), ct).ConfigureAwait(false);
                    await WriteCommaAsync(writer, ct).ConfigureAwait(false);
                    await WriteFieldAsync(writer, JsonSerializer.Serialize(entry.CustomFields), ct).ConfigureAwait(false);
                    await WriteCommaAsync(writer, ct).ConfigureAwait(false);
                    await writer.WriteAsync((entry.IsFavorite ? "1" : "0").AsMemory(), ct).ConfigureAwait(false);
                    await WriteCommaAsync(writer, ct).ConfigureAwait(false);
                    await writer.WriteAsync("2".AsMemory(), ct).ConfigureAwait(false);
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

}