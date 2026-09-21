using PejPass.Domain.Entities;

namespace PejPass.Application.Interfaces;

public interface IBrowserImportService
{
    /// <summary>
    /// Parses a browser-exported password CSV and returns VaultEntry list.
    /// Supports Chrome, Edge, Firefox and generic name/url/username/password columns.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> ImportFromCsvAsync(string filePath, CancellationToken ct = default);
}
