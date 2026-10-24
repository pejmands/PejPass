using PejPass.Domain.Entities;
using PejPass.Infrastructure.Import;

namespace PejPass.Infrastructure.Tests.Import;

public sealed class PlaintextTempJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "PejPass.PlaintextJournalTests",
        Guid.NewGuid().ToString("N"));

    private readonly PlaintextTempJournal _journal;
    private static readonly int[] expected = [1];
    private static readonly int[] expectedArray = [1];

    public PlaintextTempJournalTests()
    {
        Directory.CreateDirectory(_directory);
        _journal = new PlaintextTempJournal(Path.Combine(_directory, "journal.txt"));
    }

    private string NewTempPath(string name = "export.csv") =>
        Path.Combine(_directory, $".{name}.{Guid.NewGuid():N}.tmp");

    [Fact]
    public void DeleteLeftovers_RemovesJournaledTempFileAndClearsJournal()
    {
        var temp = NewTempPath();
        _journal.Add(temp);
        File.WriteAllText(temp, "name,password\nsecret,hunter2");

        Assert.Equal(1, _journal.DeleteLeftovers());

        Assert.False(File.Exists(temp));
        Assert.Empty(_journal.Pending());
    }

    [Fact]
    public void DeleteLeftovers_WhenFileIsAlreadyGone_ClearsEntryAndReportsZero()
    {
        _journal.Add(NewTempPath());

        Assert.Equal(0, _journal.DeleteLeftovers());
        Assert.Empty(_journal.Pending());
    }

    [Fact]
    public void DeleteLeftovers_NeverDeletesFilesThatDoNotLookLikeOurTempFiles()
    {
        var victims = new[]
        {
            Path.Combine(_directory, "photo.jpg"),
            Path.Combine(_directory, "notes.tmp"),
            Path.Combine(_directory, ".export.csv.not-a-guid.tmp"),
            Path.Combine(_directory, "export.csv." + Guid.NewGuid().ToString("N") + ".tmp")
        };

        foreach (var victim in victims)
        {
            File.WriteAllText(victim, "keep me");
            _journal.Add(victim);
        }

        Assert.Equal(0, _journal.DeleteLeftovers());

        Assert.All(victims, victim => Assert.True(File.Exists(victim)));
        Assert.Empty(_journal.Pending());
    }

    [Fact]
    public void DeleteLeftovers_IgnoresRelativePaths()
    {
        File.WriteAllText(
            Path.Combine(_directory, "journal.txt"),
            $".x.{Guid.NewGuid():N}.tmp{Environment.NewLine}");

        Assert.Equal(0, _journal.DeleteLeftovers());
        Assert.Empty(_journal.Pending());
    }

    [Fact]
    public void RemoveIfGone_KeepsEntryWhileFileStillExists()
    {
        var temp = NewTempPath();
        _journal.Add(temp);
        File.WriteAllText(temp, "x");

        _journal.RemoveIfGone(temp);
        Assert.Single(_journal.Pending());

        File.Delete(temp);
        _journal.RemoveIfGone(temp);
        Assert.Empty(_journal.Pending());
    }

    [Fact]
    public async Task Export_RegistersTempFileBeforeWritingAndClearsItAfterSuccess()
    {
        var destination = Path.Combine(_directory, "out.csv");
        var seen = new List<int>();

        await new CsvExportService(_journal).ExportToCsvAsync(
            destination,
            EntriesThatReportJournal(seen),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, seen);
        Assert.Empty(_journal.Pending());
        Assert.True(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_directory, ".*.tmp"));
    }

    [Fact]
    public async Task PejPassExport_RegistersTempFileBeforeWritingAndClearsItAfterSuccess()
    {
        var destination = Path.Combine(_directory, "out.pejpass.csv");
        var seen = new List<int>();

        await new CsvExportService(_journal).ExportPejPassCsvAsync(
            destination,
            EntriesThatReportJournal(seen),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedArray, seen);
        Assert.Empty(_journal.Pending());
        Assert.Empty(Directory.GetFiles(_directory, ".*.tmp"));
    }

    [Fact]
    public async Task Export_WhenEnumerationFails_LeavesNoTempFileAndNoJournalEntry()
    {
        var destination = Path.Combine(_directory, "out.csv");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CsvExportService(_journal).ExportToCsvAsync(
                destination,
                EntriesThatFail(),
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(destination));
        Assert.Empty(_journal.Pending());
        Assert.Empty(Directory.GetFiles(_directory, ".*.tmp"));
    }

    private IEnumerable<VaultEntry> EntriesThatReportJournal(List<int> seen)
    {
        seen.Add(_journal.Pending().Count);
        yield return new VaultEntry { Title = "Example", Password = "secret" };
    }

    private static IEnumerable<VaultEntry> EntriesThatFail()
    {
        yield return new VaultEntry { Title = "Partial", Password = "secret" };
        throw new InvalidOperationException("Simulated failure while enumerating entries.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }
}
