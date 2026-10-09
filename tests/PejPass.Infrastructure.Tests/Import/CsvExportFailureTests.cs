using PejPass.Domain.Entities;
using PejPass.Infrastructure.Import;

namespace PejPass.Infrastructure.Tests.Import;

public sealed class CsvExportFailureTests
{
    [Fact]
    public async Task ExportToCsvAsync_WhenEnumerationFails_PreservesExistingFileAndDeletesTemporaryFile()
    {
        await AssertFailedExportPreservesDestinationAsync(
            ".csv",
            (service, path, entries) => service.ExportToCsvAsync(path, entries));
    }

    [Fact]
    public async Task ExportPejPassCsvAsync_WhenEnumerationFails_PreservesExistingFileAndDeletesTemporaryFile()
    {
        await AssertFailedExportPreservesDestinationAsync(
            ".pejpass.csv",
            (service, path, entries) => service.ExportPejPassCsvAsync(path, entries));
    }

    private static async Task AssertFailedExportPreservesDestinationAsync(
        string extension,
        Func<CsvExportService, string, IEnumerable<VaultEntry>, Task> export)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PejPass-CsvExport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "existing" + extension);
        const string existingContent = "previous export";

        try
        {
            await File.WriteAllTextAsync(file, existingContent);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                export(new CsvExportService(), file, EntriesThatFailAfterOneEntry()));

            Assert.Equal(existingContent, await File.ReadAllTextAsync(file));
            Assert.Empty(Directory.GetFiles(directory, $".{Path.GetFileName(file)}.*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<VaultEntry> EntriesThatFailAfterOneEntry()
    {
        yield return new VaultEntry
        {
            Title = "Partial entry",
            Username = "user",
            Password = "sensitive-password"
        };

        throw new InvalidOperationException("Simulated failure while enumerating entries.");
    }
}
