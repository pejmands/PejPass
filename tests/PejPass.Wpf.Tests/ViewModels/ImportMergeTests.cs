using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class ImportMergeTests
{
    private const string Totp = "JBSWY3DPEHPK3PXP";
    private static readonly string[] expected = ["work"];

    private static VaultEntry BrowserRow(
        string title = "Example",
        string username = "alice",
        string password = "sample-password",
        string url = "https://example.com",
        string notes = "") =>
        new()
        {
            Title = title,
            Username = username,
            Password = password,
            Url = url,
            Notes = notes
        };

    private static VaultEntry EnrichedEntry() =>
        new()
        {
            Title = "Example",
            Username = "alice",
            Password = "sample-password",
            Url = "https://example.com",
            TotpSecret = Totp,
            Tags = ["work"],
            CustomFields =
            [
                new CustomField { Name = "Recovery code", Value = "sample-value", IsSecret = true }
            ]
        };

    [Fact]
    public void BrowserImport_SkipsEntryThatHasTotpTagsAndCustomFields()
    {
        var vault = new Vault();
        vault.AddEntry(EnrichedEntry());

        var result = MainViewModel.MergeImportedEntries(
            vault, [BrowserRow()], EntryFingerprintScope.BrowserFields);

        Assert.Equal(0, result.AddedToList);
        Assert.Equal(1, result.SkippedAlreadyInList);
        Assert.Single(vault.Entries);
    }

    [Fact]
    public void BrowserImport_RunTwice_DoesNotCreateDuplicates()
    {
        var vault = new Vault();
        IReadOnlyList<VaultEntry> csv =
        [
            BrowserRow(),
            BrowserRow("Other", "bob", "sample-password-2", "https://other.example")
        ];

        var first = MainViewModel.MergeImportedEntries(vault, csv, EntryFingerprintScope.BrowserFields);
        var second = MainViewModel.MergeImportedEntries(vault, csv, EntryFingerprintScope.BrowserFields);

        Assert.Equal(2, first.AddedToList);
        Assert.Equal(0, second.AddedToList);
        Assert.Equal(2, second.SkippedAlreadyInList);
        Assert.Equal(2, vault.Entries.Count);
    }

    [Fact]
    public void BrowserImport_AddsEntryWhenAnyBrowserFieldDiffers()
    {
        var vault = new Vault();
        vault.AddEntry(EnrichedEntry());

        var result = MainViewModel.MergeImportedEntries(
            vault, [BrowserRow(password: "different-sample-password")], EntryFingerprintScope.BrowserFields);

        Assert.Equal(1, result.AddedToList);
        Assert.Equal(2, vault.Entries.Count);
    }

    [Fact]
    public void BrowserImport_RestoresMatchingEntryFromTrash_EvenWithTotp()
    {
        var vault = new Vault();
        var entry = EnrichedEntry();
        vault.AddEntry(entry);
        Assert.True(vault.SoftDelete(entry.Id));

        var result = MainViewModel.MergeImportedEntries(
            vault, [BrowserRow()], EntryFingerprintScope.BrowserFields);

        Assert.Equal(1, result.RestoredFromTrash);
        Assert.Equal(0, result.AddedToList);
        Assert.Empty(vault.Trash);

        var restored = Assert.Single(vault.Entries);
        Assert.Equal(entry.Id, restored.Id);
        Assert.Equal(Totp, restored.TotpSecret);
        Assert.Equal(expected, restored.Tags);
        Assert.Equal("Recovery code", Assert.Single(restored.CustomFields).Name);
    }

    [Fact]
    public void BrowserImport_SameRowTwiceInOneFile_IsAddedOnce()
    {
        var vault = new Vault();

        var result = MainViewModel.MergeImportedEntries(
            vault, [BrowserRow(), BrowserRow()], EntryFingerprintScope.BrowserFields);

        Assert.Equal(1, result.AddedToList);
        Assert.Equal(1, result.SkippedAlreadyInList);
        Assert.Single(vault.Entries);
    }

    [Fact]
    public void FullScope_StillTreatsDifferentTotpAsADifferentEntry()
    {
        var vault = new Vault();
        vault.AddEntry(EnrichedEntry());

        var withoutTotp = EnrichedEntry();
        withoutTotp.TotpSecret = string.Empty;

        var result = MainViewModel.MergeImportedEntries(vault, [withoutTotp]);

        Assert.Equal(1, result.AddedToList);
        Assert.Equal(2, vault.Entries.Count);
    }

    [Fact]
    public void Fingerprint_BrowserScope_IgnoresTotpTagsAndCustomFields()
    {
        Assert.Equal(
            MainViewModel.EntryContentFingerprint(BrowserRow(), EntryFingerprintScope.BrowserFields),
            MainViewModel.EntryContentFingerprint(EnrichedEntry(), EntryFingerprintScope.BrowserFields));

        Assert.NotEqual(
            MainViewModel.EntryContentFingerprint(BrowserRow()),
            MainViewModel.EntryContentFingerprint(EnrichedEntry()));
    }

    [Fact]
    public void Fingerprint_BrowserScope_DoesNotConfuseFieldBoundaries()
    {
        var left = BrowserRow(title: "ab", username: "c");
        var right = BrowserRow(title: "a", username: "bc");

        Assert.NotEqual(
            MainViewModel.EntryContentFingerprint(left, EntryFingerprintScope.BrowserFields),
            MainViewModel.EntryContentFingerprint(right, EntryFingerprintScope.BrowserFields));
    }
}
