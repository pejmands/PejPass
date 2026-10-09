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
        // CSV is unencrypted; UTF-8 with BOM helps Excel display non-ASCII text.
        using var writer = new StreamWriter(
            filePath,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(
            "name,url,username,password,note,totp_secret,tags,custom_fields,favorite,pejpass_format".AsMemory(),
            ct).ConfigureAwait(false);

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
            await writer.WriteLineAsync(ct).ConfigureAwait(false);
        }
    }
}
