using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using System.Text;
using System.Text.Json;

namespace PejPass.Infrastructure.Storage;

/// <summary>
/// Simple encrypted vault file format:
/// [4 bytes magic "PEJP"]
/// [1 byte version]
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

    private const byte CurrentVersion = 1;
    private const int SaltLength = 16;
    private const int MaxSaltLength = 64;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly ICryptoService _crypto = crypto;
    private readonly IFileMover _fileMover = fileMover;

    public bool Exists(string path) => File.Exists(path);

    public async Task CreateAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        var salt = _crypto.GenerateSalt(SaltLength);
        var key = _crypto.DeriveKey(masterPassword, salt);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(vault);
            var (ciphertext, nonce, tag) = _crypto.Encrypt(json, key);

            await using var fs = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            await WriteVaultAsync(fs, salt, nonce, tag, ciphertext, ct);
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
        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

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

        if (version[0] != CurrentVersion)
            throw new NotSupportedException(
                $"Unsupported vault version: {version[0]}");

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

        var key = _crypto.DeriveKey(masterPassword, salt);

        try
        {
            var plaintext = _crypto.Decrypt(
                ciphertext,
                nonce,
                tag,
                key);

            var vault = JsonSerializer.Deserialize<Vault>(plaintext)
                        ?? throw new InvalidDataException(
                            "Vault data is corrupted.");

            return vault;
        }
        finally
        {
            _crypto.ZeroMemory(key);
        }
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

        var salt = _crypto.GenerateSalt(SaltLength);
        var key = _crypto.DeriveKey(masterPassword, salt);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(vault);
            var (ciphertext, nonce, tag) = _crypto.Encrypt(json, key);

            await using (var fs = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await WriteVaultAsync(
                    fs,
                    salt,
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

    private static async Task WriteVaultAsync(
        FileStream fs,
        byte[] salt,
        byte[] nonce,
        byte[] tag,
        byte[] ciphertext,
        CancellationToken ct)
    {
        await fs.WriteAsync(Magic, ct);
        await fs.WriteAsync(
            new byte[] { CurrentVersion },
            ct);

        await fs.WriteAsync(
            BitConverter.GetBytes((ushort)salt.Length),
            ct);

        await fs.WriteAsync(salt, ct);
        await fs.WriteAsync(nonce, ct);
        await fs.WriteAsync(tag, ct);
        await fs.WriteAsync(ciphertext, ct);
    }
}
