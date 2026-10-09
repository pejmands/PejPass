using System.IO;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

public sealed class UpdateManifestValidationTests
{
    [Fact]
    public void ValidateUpdateManifest_AcceptsManifestWithinLimits()
    {
        var manifest = new UpdateManifest
        {
            Version = "1.2.0",
            Released = "October 10, 2026",
            DownloadUrl = "https://example.com/PejPass.zip",
            Sha256 = new string('a', 64),
            Signature = Convert.ToBase64String(new byte[64]),
            Releases =
            [
                new ManifestRelease
                {
                    Version = "1.2.0",
                    Released = "October 10, 2026",
                    Notes = new UpdateNotes { Added = ["A new feature"] }
                }
            ]
        };

        UpdateService.ValidateUpdateManifest(manifest);
    }

    [Fact]
    public void ValidateUpdateManifest_RejectsTooManyReleases()
    {
        var manifest = new UpdateManifest
        {
            Releases = Enumerable.Range(0, 1_001)
                .Select(index => new ManifestRelease { Version = $"1.0.{index}" })
                .ToList()
        };

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateManifest(manifest));
    }

    [Fact]
    public void ValidateUpdateManifest_RejectsOversizedReleaseNoteItem()
    {
        var manifest = new UpdateManifest
        {
            Notes = new UpdateNotes
            {
                Added = [new string('x', 4_097)]
            }
        };

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateManifest(manifest));
    }

    [Fact]
    public void ValidateUpdateManifest_RejectsTooManyItemsInOneCategory()
    {
        var manifest = new UpdateManifest
        {
            Notes = new UpdateNotes
            {
                Added = Enumerable.Repeat("note", 101).ToList()
            }
        };

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateManifest(manifest));
    }

    [Fact]
    public void ValidateUpdateManifest_AcceptsAggregateTextAbovePreviousLimit()
    {
        var longNotes = Enumerable.Repeat(new string('x', 3_600), 100).ToList();
        var manifest = new UpdateManifest
        {
            Notes = new UpdateNotes
            {
                Added = longNotes,
                Improved = longNotes,
                Fixed = longNotes
            }
        };

        UpdateService.ValidateUpdateManifest(manifest);
    }

    [Fact]
    public void ValidateUpdateManifest_RejectsExcessiveAggregateText()
    {
        var longNotes = Enumerable.Repeat(new string('x', 4_096), 100).ToList();
        var manifest = new UpdateManifest
        {
            Releases =
            [
                new ManifestRelease
                {
                    Version = "1.0.1",
                    Notes = new UpdateNotes
                    {
                        Added = longNotes,
                        Improved = longNotes,
                        Fixed = longNotes
                    }
                },
                new ManifestRelease
                {
                    Version = "1.0.0",
                    Notes = new UpdateNotes
                    {
                        Added = longNotes,
                        Improved = longNotes,
                        Fixed = longNotes
                    }
                }
            ]
        };

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateManifest(manifest));
    }
}
