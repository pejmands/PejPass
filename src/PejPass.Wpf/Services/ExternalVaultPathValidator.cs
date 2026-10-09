using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PejPass.Wpf.Services;

internal static class ExternalVaultPathValidator
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PEJP");

    private const byte LegacyVersion = 1;
    private const byte PreviousVersion = 2;
    private const byte CurrentVersion = 3;
    private const byte Argon2idAlgorithmId = 1;
    private const int SaltLength = 16;
    private const int MaxSaltLength = 64;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static bool TryValidate(string? path, out string canonicalPath)
    {
        canonicalPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var candidate = path.Trim().Trim('"');

        if (!Path.IsPathFullyQualified(candidate))
            return false;

        try
        {
            canonicalPath = Path.GetFullPath(candidate);
        }
        catch
        {
            canonicalPath = string.Empty;
            return false;
        }

        if (!Path.IsPathFullyQualified(canonicalPath) ||
            !string.Equals(
                Path.GetExtension(canonicalPath),
                ".pejpass",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(canonicalPath))
        {
            canonicalPath = string.Empty;
            return false;
        }

        try
        {
            using var stream = new FileStream(
                canonicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            if (stream.Length < Magic.Length + sizeof(byte))
                return Fail(out canonicalPath);

            Span<byte> prefix = stackalloc byte[Magic.Length + sizeof(byte)];
            stream.ReadExactly(prefix);

            if (!prefix[..Magic.Length].SequenceEqual(Magic))
                return Fail(out canonicalPath);

            var version = prefix[Magic.Length];
            if (version is not (LegacyVersion or PreviousVersion or CurrentVersion))
                return Fail(out canonicalPath);

            var saltLengthOffset = version == CurrentVersion ? 18 : 5;

            if (version == CurrentVersion)
            {
                if (stream.Length < 20)
                    return Fail(out canonicalPath);

                stream.Position = 5;
                if (stream.ReadByte() != Argon2idAlgorithmId)
                    return Fail(out canonicalPath);
            }

            if (stream.Length < saltLengthOffset + sizeof(ushort))
                return Fail(out canonicalPath);

            stream.Position = saltLengthOffset;
            Span<byte> saltLengthBytes = stackalloc byte[sizeof(ushort)];
            stream.ReadExactly(saltLengthBytes);

            var saltLength = BinaryPrimitives.ReadUInt16LittleEndian(saltLengthBytes);
            if (saltLength < SaltLength || saltLength > MaxSaltLength)
                return Fail(out canonicalPath);

            var requiredLength =
                saltLengthOffset + sizeof(ushort) + saltLength +
                NonceLength + TagLength + 1;

            if (stream.Length < requiredLength)
                return Fail(out canonicalPath);

            return true;
        }
        catch
        {
            canonicalPath = string.Empty;
            return false;
        }
    }

    private static bool Fail(out string canonicalPath)
    {
        canonicalPath = string.Empty;
        return false;
    }
}
