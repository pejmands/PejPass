using PejPass.Wpf.Services;
using System.IO;
using System.IO.Compression;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateArchiveValidationTests
{
    [Fact]
    public void ValidateUpdateArchive_AcceptsArchiveWithinLimits()
    {
        using var stream = CreateArchive(("PejPass.exe", new byte[8]));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        UpdateService.ValidateUpdateArchive(
            archive,
            maxEntries: 2,
            maxTotalUncompressedBytes: 16,
            maxEntryUncompressedBytes: 8);
    }

    [Fact]
    public void ValidateUpdateArchive_RejectsEmptyArchive()
    {
        using var stream = CreateArchive();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateArchive(archive));
    }

    [Fact]
    public void ValidateUpdateArchive_RejectsTooManyEntries()
    {
        using var stream = CreateArchive(
            ("one.txt", new byte[1]),
            ("two.txt", new byte[1]));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateArchive(
                archive,
                maxEntries: 1,
                maxTotalUncompressedBytes: 10,
                maxEntryUncompressedBytes: 10));
    }

    [Fact]
    public void ValidateUpdateArchive_RejectsOversizedEntry()
    {
        using var stream = CreateArchive(("large.txt", new byte[11]));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateArchive(
                archive,
                maxEntries: 10,
                maxTotalUncompressedBytes: 100,
                maxEntryUncompressedBytes: 10));
    }

    [Fact]
    public void ValidateUpdateArchive_RejectsExcessiveTotalUncompressedSize()
    {
        using var stream = CreateArchive(
            ("one.txt", new byte[6]),
            ("two.txt", new byte[5]));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ValidateUpdateArchive(
                archive,
                maxEntries: 10,
                maxTotalUncompressedBytes: 10,
                maxEntryUncompressedBytes: 10));
    }

    private static MemoryStream CreateArchive(params (string Name, byte[] Data)[] entries)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name);
                using var entryStream = entry.Open();
                entryStream.Write(item.Data);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
