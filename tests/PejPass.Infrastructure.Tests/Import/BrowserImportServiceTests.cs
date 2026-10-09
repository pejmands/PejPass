using PejPass.Infrastructure.Import;
using System.IO;

namespace PejPass.Infrastructure.Tests.Import;

public sealed class BrowserImportServiceTests
{
    private readonly BrowserImportService _service = new();

    [Fact]
    public async Task ImportCsv_WithMultilineNotes_PreservesNewlines()
    {
        var csv = """
        name,url,username,password,notes
        Google,https://google.com,user,pass,"line one
        line two
        line three"
        """;

        var file = CreateTempCsv(csv);

        var result = await _service.ImportFromCsvAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(
            "line one\nline two\nline three",
            result[0].Notes.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ImportCsv_WithEscapedQuotes_PreservesQuotes()
    {
        var csv =
            "name,password,notes\n" +
            "Test,123,\"say \"\"hello\"\"\"";

        var file = CreateTempCsv(csv);

        var result = await _service.ImportFromCsvAsync(file, TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal("say \"hello\"", item.Notes);
    }

    [Fact]
    public async Task ImportCsv_WithCommaInsideQuotedField_PreservesField()
    {
        var csv = """
        name,password,notes
        Test,123,"one,two,three"
        """;

        var file = CreateTempCsv(csv);

        var result = await _service.ImportFromCsvAsync(file, TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal("one,two,three", item.Notes);
    }

    [Fact]
    public async Task ImportCsv_WhenFileExceedsSizeLimit_RejectsBeforeParsing()
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            await using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
                stream.SetLength(64L * 1024 * 1024 + 1);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.ImportFromCsvAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public async Task ImportCsv_WhenFieldExceedsCharacterLimit_RejectsRecord()
    {
        var csv = "name,password,notes\nTest,123," + new string('x', 1_048_577);
        var file = CreateTempCsv(csv);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.ImportFromCsvAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public async Task ImportCsv_WhenRecordCountExceedsLimit_RejectsImport()
    {
        var csv = new System.Text.StringBuilder("name,password\n");
        for (var i = 0; i < 100_001; i++)
            csv.Append("Test,pass\n");

        var file = CreateTempCsv(csv.ToString());
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.ImportFromCsvAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private static string CreateTempCsv(string content)
    {
        var file = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.csv");

        File.WriteAllText(file, content);

        return file;
    }
}
