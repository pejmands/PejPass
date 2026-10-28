using CsvHelper;
using PejPass.Domain.Entities;
using System.Globalization;
using System.Text.Json;

namespace PejPass.Infrastructure.Import;

public sealed partial class BrowserImportService
{
    public async Task<IReadOnlyList<VaultEntry>> ImportPejPassCsvAsync(
        string filePath,
        CancellationToken ct = default)
    {
        using var csv = await OpenCsvAsync(filePath, ct);

        var headerFields = csv.Headers.ToList();
        ValidateCsvRecord(headerFields, "header");

        var header = headerFields
            .Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.Ordinal);

        var required = new[]
        {
            "name", "url", "username", "password", "note",
            "totp_secret", "tags", "custom_fields", "pejpass_format"
        };

        var missing = required.Where(name => !header.ContainsKey(name)).ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                "This is not a PejPass CSV file. Missing columns: "
                + string.Join(", ", missing));
        }

        var entries = new List<VaultEntry>();
        var recordCount = 0;

        while (await csv.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            if (++recordCount > MaxCsvRecords)
            {
                throw new InvalidDataException(
                    $"CSV import exceeds the limit of {MaxCsvRecords} records.");
            }

            if (csv.FieldCount > MaxCsvColumns)
            {
                throw new InvalidDataException(
                    $"record {recordCount} exceeds the {MaxCsvColumns}-column limit.");
            }

            var cols = Enumerable
                .Range(0, csv.FieldCount)
                .Select(csv.GetString)
                .ToList();

            ValidateCsvRecord(
                cols,
                $"record {recordCount}",
                headerFields,
                GetCol(cols, header["name"]));

            if (cols.Count == 0)
                continue;

            var format = GetCol(cols, header["pejpass_format"]);

            if (format is not ("1" or "2"))
            {
                throw new InvalidDataException(
                    "Unsupported or invalid PejPass CSV format version.");
            }

            var isFavorite = format == "2"
                && header.TryGetValue("favorite", out var favoriteIndex)
                && string.Equals(
                    GetCol(cols, favoriteIndex),
                    "1",
                    StringComparison.Ordinal);

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
                throw new InvalidDataException(
                    "Invalid tags or custom fields data in PejPass CSV.",
                    ex);
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
                CustomFields = customFields,
                IsFavorite = isFavorite
            });
        }

        return entries;
    }

    private static async Task<CsvReader> OpenCsvAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        var stream = File.OpenRead(filePath);
        CsvReader? csv = null;

        try
        {
            ValidateCsvFileSize(stream);

            csv = CsvReader.Create(stream, new CsvReaderOptions
            {
                HasHeader = true,
                Separator = ',',
                SkipEmptyLines = true,
                FormatProvider = CultureInfo.InvariantCulture,
                MalformedData = CsvMalformedDataHandling.Recover,
                OnMalformedRecord = _ => throw new InvalidDataException(
                    "CSV contains malformed quoting or field data.")
            });

            await csv.ReadHeaderAsync(ct);

            if (csv.Headers.Count == 0)
                throw new InvalidDataException("The CSV file is empty.");

            return csv;
        }
        catch
        {
            if (csv is not null)
                csv.Dispose();
            else
                await stream.DisposeAsync();

            throw;
        }
    }
}
