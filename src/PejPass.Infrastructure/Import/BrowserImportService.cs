using CsvHelper;
using CsvHelper.Configuration;
using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using System.Globalization;
using System.Text;

namespace PejPass.Infrastructure.Import;

/// <summary>
/// Imports passwords from common browser CSV exports (Chrome, Edge, Firefox, etc.).
/// </summary>
public sealed partial class BrowserImportService : IBrowserImportService
{
    private const long MaxCsvFileBytes = 64L * 1024 * 1024;
    private const int MaxCsvRecords = 100_000;
    private const int MaxCsvColumns = 128;
    private const int MaxCsvFieldCharacters = 1_048_576;

    public async Task<IReadOnlyList<VaultEntry>> ImportFromCsvAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        await using var stream = File.OpenRead(filePath);
        ValidateCsvFileSize(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            BadDataFound = _ => throw new InvalidDataException("CSV contains malformed quoting or field data.")
        });

        ct.ThrowIfCancellationRequested();
        if (!await csv.ReadAsync())
            throw new InvalidDataException("The CSV file is empty.");

        csv.ReadHeader();
        var header = csv.HeaderRecord?.ToList() ?? [];
        ValidateCsvRecord(header, "header");

        var map = BuildColumnMap(header);

        if (map.PasswordIndex < 0)
            throw new InvalidDataException("Could not find a password column in the CSV header.");

        var entries = new List<VaultEntry>();
        var recordCount = 0;

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            if (++recordCount > MaxCsvRecords)
                throw new InvalidDataException($"CSV import exceeds the limit of {MaxCsvRecords} records.");

            var cols = csv.Parser.Record?.ToList() ?? [];
            ValidateCsvRecord(cols, $"record {recordCount}");

            if (cols.Count == 0)
                continue;

            var password = GetCol(cols, map.PasswordIndex);

            if (string.IsNullOrEmpty(password))
                continue;

            var title = GetCol(cols, map.TitleIndex);
            var username = GetCol(cols, map.UsernameIndex);
            var url = GetCol(cols, map.UrlIndex);
            var notes = GetCol(cols, map.NotesIndex);

            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    var uri = new Uri(url);
                    title = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    title = url;
                }
            }

            if (string.IsNullOrWhiteSpace(title))
                title = "Imported Entry";

            entries.Add(new VaultEntry
            {
                Title = title.Trim(),
                Username = username?.Trim() ?? string.Empty,
                Password = password,
                Url = url?.Trim() ?? string.Empty,
                Notes = notes?.Trim() ?? string.Empty
            });
        }

        return entries;
    }

    private static void ValidateCsvFileSize(FileStream stream)
    {
        if (stream.Length > MaxCsvFileBytes)
            throw new InvalidDataException($"CSV file exceeds the {MaxCsvFileBytes / (1024 * 1024)} MiB size limit.");
    }

    private static void ValidateCsvRecord(IReadOnlyList<string> fields, string recordDescription)
    {
        if (fields.Count > MaxCsvColumns)
            throw new InvalidDataException($"{recordDescription} exceeds the {MaxCsvColumns}-column limit.");

        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Length > MaxCsvFieldCharacters)
                throw new InvalidDataException(
                    $"{recordDescription} contains a field exceeding the {MaxCsvFieldCharacters}-character limit.");
        }
    }

    private static ColumnMap BuildColumnMap(List<string> header)
    {
        var map = new ColumnMap();

        for (int i = 0; i < header.Count; i++)
        {
            var h = header[i].Trim().ToLowerInvariant();

            // Title / Name
            if (map.TitleIndex < 0 && (h is "name" or "title" or "hostname" or "site" or "login_uri"))
                map.TitleIndex = i;

            // URL
            if (map.UrlIndex < 0 && (h is "url" or "origin" or "login_uri" or "formactionorigin" or "http://hostname" or "hostname"))
                map.UrlIndex = i;

            // Username
            if (map.UsernameIndex < 0 && (h is "username" or "user" or "login" or "login_username" or "email"))
                map.UsernameIndex = i;

            // Password
            if (map.PasswordIndex < 0 && (h is "password" or "login_password" or "pass"))
                map.PasswordIndex = i;

            // Notes
            if (map.NotesIndex < 0 && (h is "note" or "notes" or "comment" or "extra"))
                map.NotesIndex = i;
        }

        return map;
    }

    private static string? GetCol(List<string> cols, int index)
    {
        if (index < 0 || index >= cols.Count)
            return null;
        return cols[index];
    }

    private sealed class ColumnMap
    {
        public int TitleIndex { get; set; } = -1;
        public int UrlIndex { get; set; } = -1;
        public int UsernameIndex { get; set; } = -1;
        public int PasswordIndex { get; set; } = -1;
        public int NotesIndex { get; set; } = -1;
    }
}
