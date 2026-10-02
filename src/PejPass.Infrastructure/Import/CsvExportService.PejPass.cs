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
        var sb = new StringBuilder();
        sb.AppendLine("name,url,username,password,note,totp_secret,tags,custom_fields,favorite,pejpass_format");

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(Escape(entry.Title)).Append(',')
              .Append(Escape(entry.Url)).Append(',')
              .Append(Escape(entry.Username)).Append(',')
              .Append(Escape(entry.Password)).Append(',')
              .Append(Escape(entry.Notes)).Append(',')
              .Append(Escape(entry.TotpSecret)).Append(',')
              .Append(Escape(JsonSerializer.Serialize(entry.Tags))).Append(',')
              .Append(Escape(JsonSerializer.Serialize(entry.CustomFields))).Append(',')
              .Append(entry.IsFavorite ? "1" : "0").Append(',')
              .Append('2')
              .AppendLine();
        }

        // CSV is unencrypted; UTF-8 with BOM helps Excel display non-ASCII text.
        await File.WriteAllTextAsync(
            filePath,
            sb.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            ct).ConfigureAwait(false);
    }
}
