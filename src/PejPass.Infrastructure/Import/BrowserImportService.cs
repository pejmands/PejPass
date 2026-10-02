using CsvHelper;
using CsvHelper.Configuration;
using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using System.Globalization;
using System.Text;

namespace PejPass.Infrastructure.Import;

/// <summary>
/// Imports passwords from common browser CSV exports (Chrome, Edge, Firefox, etc.).
/// No external CSV library — simple and robust enough for these formats.
/// </summary>
public sealed partial class BrowserImportService : IBrowserImportService
{
    public async Task<IReadOnlyList<VaultEntry>> ImportFromCsvAsync(
    string filePath,
    CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            BadDataFound = null
        });

        await csv.ReadAsync();
        csv.ReadHeader();

        var header = csv.HeaderRecord?.ToList() ?? [];

        var map = BuildColumnMap(header);

        if (map.PasswordIndex < 0)
            throw new InvalidDataException("Could not find a password column in the CSV header.");

        var entries = new List<VaultEntry>();

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            var cols = csv.Parser.Record?.ToList() ?? [];

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
                Notes = notes?.Trim() ?? string.Empty,
                Tags = ["imported"]
            });
        }

        return entries;
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

        // Chrome/Edge often use "name" for title and "url" for url
        // Firefox uses "url", "username", "password"

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
