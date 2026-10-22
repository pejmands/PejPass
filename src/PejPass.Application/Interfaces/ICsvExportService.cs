using PejPass.Domain.Entities;

namespace PejPass.Application.Interfaces;

/// <summary>
/// Writes entries as browser-compatible or PejPass-specific unencrypted CSV.
/// Neither format is a secure backup — prefer encrypted .pejpass.
/// </summary>
public interface ICsvExportService
{
    Task ExportToCsvAsync(
        string filePath,
        IEnumerable<VaultEntry> entries,
        CancellationToken ct = default);

    Task ExportPejPassCsvAsync(
        string filePath,
        IEnumerable<VaultEntry> entries,
        CancellationToken ct = default);
}
