using PejPass.Domain.Entities;
using PejPass.Infrastructure.Import;

namespace PejPass.Infrastructure.Tests.Import;

public sealed class PejPassCsvRoundTripTests
{
    [Fact]
    public async Task ExportAndImport_PreservesTotpTagsAndCustomFields()
    {
        var source = new VaultEntry
        {
            Title = "Example, account",
            Url = "https://example.com",
            Username = "pejman",
            Password = "p,ass\"word",
            Notes = "line one\nline two",
            TotpSecret = "JBSWY3DPEHPK3PXP",
            Tags = ["Personal", "Social", "Music"],
            CustomFields =
            [
                new CustomField { Name = "Recovery code", Value = "123,456", IsSecret = true },
                new CustomField { Name = "Department", Value = "Research", IsSecret = false }
            ]
        };

        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pejpass.csv");
        try
        {
            await new CsvExportService().ExportPejPassCsvAsync(file, [source], TestContext.Current.CancellationToken);
            var imported = Assert.Single(await new BrowserImportService().ImportPejPassCsvAsync(file, TestContext.Current.CancellationToken));

            Assert.Equal(source.Title, imported.Title);
            Assert.Equal(source.Url, imported.Url);
            Assert.Equal(source.Username, imported.Username);
            Assert.Equal(source.Password, imported.Password);
            Assert.Equal(source.Notes, imported.Notes);
            Assert.Equal(source.TotpSecret, imported.TotpSecret);
            Assert.Equal(source.Tags, imported.Tags);
            Assert.Equal(source.CustomFields.Count, imported.CustomFields.Count);

            for (var i = 0; i < source.CustomFields.Count; i++)
            {
                Assert.Equal(source.CustomFields[i].Name, imported.CustomFields[i].Name);
                Assert.Equal(source.CustomFields[i].Value, imported.CustomFields[i].Value);
                Assert.Equal(source.CustomFields[i].IsSecret, imported.CustomFields[i].IsSecret);
            }
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
