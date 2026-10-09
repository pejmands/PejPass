using PejPass.Wpf.Services;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PejPass.Wpf.Tests;

public sealed class ExternalVaultPathValidatorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"PejPass.PathValidation.{Guid.NewGuid():N}");

    public ExternalVaultPathValidatorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void TryValidate_AcceptsExistingVaultWithSupportedHeader()
    {
        var path = CreateVaultFile("valid.pejpass", version: 3);

        var result = ExternalVaultPathValidator.TryValidate(path, out var canonicalPath);

        Assert.True(result);
        Assert.Equal(Path.GetFullPath(path), canonicalPath);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void TryValidate_AcceptsAllSupportedVersions(byte version)
    {
        var path = CreateVaultFile($"version-{version}.pejpass", version);

        Assert.True(ExternalVaultPathValidator.TryValidate(path, out _));
    }

    [Fact]
    public void TryValidate_RejectsUnsupportedVersion()
    {
        var path = CreateVaultFile("unsupported.pejpass", version: 99);

        Assert.False(ExternalVaultPathValidator.TryValidate(path, out var canonicalPath));
        Assert.Equal(string.Empty, canonicalPath);
    }

    [Fact]
    public void TryValidate_RejectsInvalidMagic()
    {
        var path = CreateVaultFile("invalid-magic.pejpass", version: 3, magic: "NOPE");

        Assert.False(ExternalVaultPathValidator.TryValidate(path, out _));
    }

    [Fact]
    public void TryValidate_RejectsInvalidVersionThreeAlgorithm()
    {
        var path = CreateVaultFile("invalid-algorithm.pejpass", version: 3, algorithmId: 2);

        Assert.False(ExternalVaultPathValidator.TryValidate(path, out _));
    }

    [Fact]
    public void TryValidate_RejectsWrongExtension()
    {
        var path = CreateVaultFile("vault.bin", version: 3);

        Assert.False(ExternalVaultPathValidator.TryValidate(path, out _));
    }


    [Fact]
    public void TryValidate_RejectsRelativePath()
    {
        var path = CreateVaultFile("relative.pejpass", version: 3);
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, path);

        Assert.False(ExternalVaultPathValidator.TryValidate(relativePath, out _));
    }

    [Fact]
    public void TryValidate_RejectsMissingFile()
    {
        var path = Path.Combine(_directory, "missing.pejpass");

        Assert.False(ExternalVaultPathValidator.TryValidate(path, out _));
    }

    [Fact]
    public void TryValidate_RejectsTruncatedHeader()
    {
        var path = Path.Combine(_directory, "truncated.pejpass");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("PEJP\u0003"));

        Assert.False(ExternalVaultPathValidator.TryValidate(path, out _));
    }

    private string CreateVaultFile(
        string fileName,
        byte version,
        string magic = "PEJP",
        byte algorithmId = 1)
    {
        var saltLengthOffset = version == 3 ? 18 : 5;
        var totalLength = saltLengthOffset + sizeof(ushort) + 16 + 12 + 16 + 1;
        var bytes = new byte[totalLength];

        Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 0);
        bytes[4] = version;

        if (version == 3)
            bytes[5] = algorithmId;

        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(saltLengthOffset, sizeof(ushort)),
            16);

        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }
}
