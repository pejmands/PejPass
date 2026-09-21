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
public sealed class VaultStore(ICryptoService crypto) : IVaultStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PEJP");
    private const byte CurrentVersion = 1;

    private readonly ICryptoService _crypto = crypto;

    public bool Exists(string path) => File.Exists(path);

    public async Task CreateAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default)
    {
        var salt = _crypto.GenerateSalt(16);
        var key = _crypto.DeriveKey(masterPassword, salt);

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(vault);
            var (ciphertext, nonce, tag) = _crypto.Encrypt(json, key);

            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await fs.WriteAsync(Magic, ct);
            await fs.WriteAsync(new byte[] { CurrentVersion }, ct);
            await fs.WriteAsync(BitConverter.GetBytes((ushort)salt.Length), ct);
            await fs.WriteAsync(salt, ct);
            await fs.WriteAsync(nonce, ct);
            await fs.WriteAsync(tag, ct);
            await fs.WriteAsync(ciphertext, ct);
        }
        finally
        {
            _crypto.ZeroMemory(key);
        }
    }

    public async Task<Vault> OpenAsync(string path, string masterPassword, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var magic = new byte[4];
        await fs.ReadExactlyAsync(magic, ct);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Not a valid PejPass vault file.");

        var version = new byte[1];
        await fs.ReadExactlyAsync(version, ct);
        if (version[0] != CurrentVersion)
            throw new NotSupportedException($"Unsupported vault version: {version[0]}");

        var saltLenBytes = new byte[2];
        await fs.ReadExactlyAsync(saltLenBytes, ct);
        var saltLen = BitConverter.ToUInt16(saltLenBytes);
        var salt = new byte[saltLen];
        await fs.ReadExactlyAsync(salt, ct);

        var nonce = new byte[12];
        await fs.ReadExactlyAsync(nonce, ct);

        var tag = new byte[16];
        await fs.ReadExactlyAsync(tag, ct);

        var ciphertext = new byte[fs.Length - fs.Position];
        await fs.ReadExactlyAsync(ciphertext, ct);

        var key = _crypto.DeriveKey(masterPassword, salt);

        try
        {
            var plaintext = _crypto.Decrypt(ciphertext, nonce, tag, key);
            var vault = JsonSerializer.Deserialize<Vault>(plaintext)
                        ?? throw new InvalidDataException("Vault data is corrupted.");
            return vault;
        }
        finally
        {
            _crypto.ZeroMemory(key);
        }
    }

    public async Task SaveAsync(string path, string masterPassword, Vault vault, CancellationToken ct = default)
    {
        // For simplicity we recreate the file (same as Create).
        // Later we can add atomic write (temp + replace).
        await CreateAsync(path, masterPassword, vault, ct);
    }
}
