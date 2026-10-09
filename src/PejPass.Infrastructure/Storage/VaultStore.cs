using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace PejPass.Infrastructure.Storage;

/// <summary>
/// Simple encrypted vault file format:
/// [4 bytes magic "PEJP"]
/// [1 byte version]
/// [Argon2id algorithm and parameters in version 3]
/// [salt length + salt]
/// [nonce]
/// [tag]
/// [ciphertext of JSON-serialized Vault]
/// </summary>
public sealed class VaultStore(
    ICryptoService crypto,
    IFileMover fileMover) : IVaultStore
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

    private readonly ICryptoService _crypto = crypto;
    private readonly IFileMover _fileMover = fileMover;

    public bool Exists(string path) => File.Exists(path);

    public async Task EnsureWritableAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Vault file not found.", path);

        ct.ThrowIfCancellationRequested();

        try
        {
            await using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1,
                useAsync: true);
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException("The vault file is not writable. It may be locked or access may be denied.");
        }
        catch (IOException)
        {
            throw new IOException("The vault file is currently locked or unavailable for writing.");
        }
    }

    public async Task CreateAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        var kdfParameters = vault.KdfParameters;
        kdfParameters.Validate();

        var salt = _crypto.GenerateSalt(SaltLength);
        var key = _crypto.DeriveKey(
            masterPassword,
            salt,
            kdfParameters);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(vault);
            var associatedData = BuildAssociatedDataV3(
                kdfParameters,
                salt);

            var (ciphertext, nonce, tag) = _crypto.Encrypt(
                json,
                key,
                associatedData);

            await using var fs = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            await WriteVaultAsync(
                fs,
                salt,
                kdfParameters,
                nonce,
                tag,
                ciphertext,
                ct);
            await fs.FlushAsync(ct);
            fs.Flush(flushToDisk: true);
        }
        finally
        {
            _crypto.ZeroMemory(key);
        }
    }

    public async Task<Vault> OpenAsync(
    string path,
    string masterPassword,
    CancellationToken ct = default)
    {
        Vault vault;
        byte versionValue;

        await using (var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            var minimumHeaderLength =
                Magic.Length +
                sizeof(byte) +
                sizeof(ushort) +
                SaltLength +
                NonceLength +
                TagLength +
                1;

            if (fs.Length < minimumHeaderLength)
                throw new InvalidDataException(
                    "Vault file is too short.");

            var magic = new byte[Magic.Length];
            await fs.ReadExactlyAsync(magic, ct);

            if (!magic.AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException(
                    "Not a valid PejPass vault file.");

            var version = new byte[1];
            await fs.ReadExactlyAsync(version, ct);

            versionValue = version[0];

            if (versionValue != LegacyVersion &&
                versionValue != PreviousVersion &&
                versionValue != CurrentVersion)
            {
                throw new NotSupportedException(
                    $"Unsupported vault version: {versionValue}");
            }

            if (versionValue == CurrentVersion &&
                fs.Length < Magic.Length + sizeof(byte) + 13 + sizeof(ushort) +
                            SaltLength + NonceLength + TagLength + 1)
            {
                throw new InvalidDataException("Vault file is too short for the version 3 header.");
            }

            var kdfParameters = Argon2Parameters.Default;

            if (versionValue == CurrentVersion)
            {
                var kdfHeader = new byte[13];
                await fs.ReadExactlyAsync(kdfHeader, ct);

                if (kdfHeader[0] != Argon2idAlgorithmId)
                    throw new NotSupportedException(
                        $"Unsupported key derivation algorithm: {kdfHeader[0]}");

                kdfParameters = new Argon2Parameters(
                    BinaryPrimitives.ReadInt32LittleEndian(kdfHeader.AsSpan(1, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(kdfHeader.AsSpan(5, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(kdfHeader.AsSpan(9, 4)));

                try
                {
                    kdfParameters.Validate();
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new InvalidDataException(
                        "Vault contains invalid Argon2 parameters.",
                        ex);
                }
            }

            var saltLenBytes = new byte[sizeof(ushort)];
            await fs.ReadExactlyAsync(saltLenBytes, ct);

            var saltLen = BitConverter.ToUInt16(saltLenBytes);

            if (saltLen < SaltLength || saltLen > MaxSaltLength)
                throw new InvalidDataException(
                    $"Invalid salt length: {saltLen}.");

            if (fs.Length - fs.Position <
                saltLen + NonceLength + TagLength + 1)
            {
                throw new InvalidDataException(
                    "Vault file is truncated.");
            }

            var salt = new byte[saltLen];
            await fs.ReadExactlyAsync(salt, ct);

            var associatedData = versionValue switch
            {
                CurrentVersion => BuildAssociatedDataV3(kdfParameters, salt),
                PreviousVersion => BuildAssociatedDataV2(versionValue, salt),
                _ => null
            };

            var nonce = new byte[NonceLength];
            await fs.ReadExactlyAsync(nonce, ct);

            var tag = new byte[TagLength];
            await fs.ReadExactlyAsync(tag, ct);

            var ciphertextLength = fs.Length - fs.Position;

            if (ciphertextLength < 1)
                throw new InvalidDataException(
                    "Vault ciphertext is empty.");

            if (ciphertextLength > int.MaxValue)
                throw new InvalidDataException(
                    "Vault ciphertext is too large.");

            var ciphertext = new byte[(int)ciphertextLength];
            await fs.ReadExactlyAsync(ciphertext, ct);

            var key = _crypto.DeriveKey(
                masterPassword,
                salt,
                kdfParameters);

            try
            {
                var plaintext = _crypto.Decrypt(
                    ciphertext,
                    nonce,
                    tag,
                    key,
                    associatedData);

                vault = JsonSerializer.Deserialize<Vault>(plaintext)
                        ?? throw new InvalidDataException(
                            "Vault data is corrupted.");

                if (versionValue == CurrentVersion &&
                    vault.KdfParameters != kdfParameters)
                {
                    throw new InvalidDataException(
                        "Vault KDF parameters do not match the authenticated header.");
                }

                vault.KdfParameters = kdfParameters;
            }
            finally
            {
                _crypto.ZeroMemory(key);
            }
        }

        if (versionValue != CurrentVersion)
        {
            await SaveAsync(
                path,
                masterPassword,
                vault,
                ct);
        }

        return vault;
    }

    public async Task SaveAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(
                            Path.GetFullPath(path))
                        ?? throw new InvalidOperationException(
                            "Vault directory could not be determined.");

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        var kdfParameters = vault.KdfParameters;
        kdfParameters.Validate();

        var salt = _crypto.GenerateSalt(SaltLength);
        var key = _crypto.DeriveKey(
            masterPassword,
            salt,
            kdfParameters);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(vault);
            var associatedData = BuildAssociatedDataV3(
                kdfParameters,
                salt);

            var (ciphertext, nonce, tag) = _crypto.Encrypt(
                json,
                key,
                associatedData);

            await using (var fs = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await WriteVaultAsync(
                    fs,
                    salt,
                    kdfParameters,
                    nonce,
                    tag,
                    ciphertext,
                    ct);

                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }

            _fileMover.Move(
                tempPath,
                path,
                overwrite: true);
        }
        finally
        {
            _crypto.ZeroMemory(key);

            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Do not hide the original save exception if cleanup fails.
            }
        }
    }

    private static byte[] BuildAssociatedDataV2(
        byte version,
        byte[] salt)
    {
        using var ms = new MemoryStream();

        ms.Write(Magic);
        ms.WriteByte(version);
        ms.Write(BitConverter.GetBytes((ushort)salt.Length));
        ms.Write(salt);

        return ms.ToArray();
    }

    private static byte[] BuildAssociatedDataV3(
        Argon2Parameters parameters,
        byte[] salt)
    {
        using var ms = new MemoryStream();
        var parameterBytes = new byte[12];

        BinaryPrimitives.WriteInt32LittleEndian(parameterBytes.AsSpan(0, 4), parameters.MemorySizeKiB);
        BinaryPrimitives.WriteInt32LittleEndian(parameterBytes.AsSpan(4, 4), parameters.Iterations);
        BinaryPrimitives.WriteInt32LittleEndian(parameterBytes.AsSpan(8, 4), parameters.DegreeOfParallelism);

        ms.Write(Magic);
        ms.WriteByte(CurrentVersion);
        ms.WriteByte(Argon2idAlgorithmId);
        ms.Write(parameterBytes);
        ms.Write(BitConverter.GetBytes((ushort)salt.Length));
        ms.Write(salt);

        return ms.ToArray();
    }

    private static async Task WriteVaultAsync(
        FileStream fs,
        byte[] salt,
        Argon2Parameters parameters,
        byte[] nonce,
        byte[] tag,
        byte[] ciphertext,
        CancellationToken ct)
    {
        var parameterBytes = new byte[12];

        BinaryPrimitives.WriteInt32LittleEndian(parameterBytes.AsSpan(0, 4), parameters.MemorySizeKiB);
        BinaryPrimitives.WriteInt32LittleEndian(parameterBytes.AsSpan(4, 4), parameters.Iterations);
        BinaryPrimitives.WriteInt32LittleEndian(parameterBytes.AsSpan(8, 4), parameters.DegreeOfParallelism);

        await fs.WriteAsync(Magic, ct);
        await fs.WriteAsync(new byte[] { CurrentVersion, Argon2idAlgorithmId }, ct);
        await fs.WriteAsync(parameterBytes, ct);
        await fs.WriteAsync(
            BitConverter.GetBytes((ushort)salt.Length),
            ct);

        await fs.WriteAsync(salt, ct);
        await fs.WriteAsync(nonce, ct);
        await fs.WriteAsync(tag, ct);
        await fs.WriteAsync(ciphertext, ct);
    }
}
