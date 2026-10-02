using CsvHelper;
using CsvHelper.Configuration;
using PejPass.Domain.Entities;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PejPass.Infrastructure.Import;

public sealed partial class BrowserImportService
{
    public async Task<IReadOnlyList<VaultEntry>> ImportPejPassCsvAsync(
        string filePath,
        CancellationToken ct = default)
    {
        var (csv, reader, stream) = await OpenCsvAsync(filePath);
        using (stream)
        using (reader)
        using (csv)
        {
            var header = csv.HeaderRecord?
                .Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
                .ToDictionary(x => x.Name, x => x.Index, StringComparer.Ordinal)
                ?? new Dictionary<string, int>(StringComparer.Ordinal);

            var required = new[]
            {
                "name", "url", "username", "password", "note",
                "totp_secret", "tags", "custom_fields", "pejpass_format"
            };

            var missing = required.Where(name => !header.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException(
                    "This is not a PejPass CSV file. Missing columns: " + string.Join(", ", missing));

            var entries = new List<VaultEntry>();

            while (await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                var cols = csv.Parser.Record?.ToList() ?? [];
                if (cols.Count == 0)
                    continue;

                if (!string.Equals(GetCol(cols, header["pejpass_format"]), "1", StringComparison.Ordinal))
                    throw new InvalidDataException("Unsupported or invalid PejPass CSV format version.");

                List<string> tags;
                List<CustomField> customFields;

                try
                {
                    var tagsJson = GetCol(cols, header["tags"]);
                    var fieldsJson = GetCol(cols, header["custom_fields"]);

                    tags = string.IsNullOrWhiteSpace(tagsJson)
                        ? []
                        : JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];

                    customFields = string.IsNullOrWhiteSpace(fieldsJson)
                        ? []
                        : JsonSerializer.Deserialize<List<CustomField>>(fieldsJson) ?? [];
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("Invalid tags or custom fields data in PejPass CSV.", ex);
                }

                entries.Add(new VaultEntry
                {
                    Title = GetCol(cols, header["name"]) ?? string.Empty,
                    Url = GetCol(cols, header["url"]) ?? string.Empty,
                    Username = GetCol(cols, header["username"]) ?? string.Empty,
                    Password = GetCol(cols, header["password"]) ?? string.Empty,
                    Notes = GetCol(cols, header["note"]) ?? string.Empty,
                    TotpSecret = GetCol(cols, header["totp_secret"]) ?? string.Empty,
                    Tags = tags,
                    CustomFields = customFields
                });
            }

            return entries;
        }
    }

    private static async Task<(CsvReader Csv, StreamReader Reader, FileStream Stream)> OpenCsvAsync(
        string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        var stream = File.OpenRead(filePath);
        var reader = new StreamReader(stream, Encoding.UTF8);
        var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            BadDataFound = null
        });

        try
        {
            if (!await csv.ReadAsync())
                throw new InvalidDataException("The CSV file is empty.");

            csv.ReadHeader();
            return (csv, reader, stream);
        }
        catch
        {
            csv.Dispose();
            reader.Dispose();
            await stream.DisposeAsync();
            throw;
        }
    }
}
