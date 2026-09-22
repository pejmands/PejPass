using PejPass.Domain.Entities;

namespace PejPass.Application.Interfaces;

/// <summary>
/// Writes entries as browser-compatible unencrypted CSV.
/// Not a secure backup — prefer encrypted .pejpass.
/// </summary>
public interface ICsvExportService
{
    Task ExportToCsvAsync(
        string filePath,
        IEnumerable<VaultEntry> entries,
        CancellationToken ct = default);
}
