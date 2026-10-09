using PejPass.Domain.Entities;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PejPass.Application.Security;

/// <summary>
/// Owns the derived vault key and the metadata required to reuse it for the same vault.
/// </summary>
public sealed class VaultKeyMaterial : IDisposable
{
    private const int KeySizeBytes = 32;
    private const int MinimumSaltSizeBytes = 16;
    private const int MaximumSaltSizeBytes = 64;
    private const int HeaderSize = 19;
    private const byte PayloadVersion = 1;

    private static ReadOnlySpan<byte> PayloadMagic => "PPKM"u8;

    private readonly object _sync = new();
    private byte[]? _key;
    private byte[]? _salt;

    public Argon2Parameters KdfParameters { get; }

    public ReadOnlyMemory<byte> Key
    {
        get
        {
            lock (_sync)
                return _key ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial));
        }
    }

    public ReadOnlyMemory<byte> Salt
    {
        get
        {
            lock (_sync)
                return _salt ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial));
        }
    }

    public VaultKeyMaterial(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> salt,
        Argon2Parameters kdfParameters)
    {
        ArgumentNullException.ThrowIfNull(kdfParameters);

        if (key.Length != KeySizeBytes)
            throw new ArgumentException("Vault key must be 32 bytes.", nameof(key));

        if (salt.Length is < MinimumSaltSizeBytes or > MaximumSaltSizeBytes)
            throw new ArgumentException("Vault salt must be between 16 and 64 bytes.", nameof(salt));

        kdfParameters.Validate();

        _key = key.ToArray();
        _salt = salt.ToArray();
        KdfParameters = kdfParameters;
    }

    public byte[] CopyKey()
    {
        lock (_sync)
            return (_key ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial))).ToArray();
    }

    public byte[] CopySalt()
    {
        lock (_sync)
            return (_salt ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial))).ToArray();
    }

    public VaultKeyMaterial Clone()
    {
        lock (_sync)
        {
            var key = _key ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial));
            var salt = _salt ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial));
            return new VaultKeyMaterial(key, salt, KdfParameters);
        }
    }

    /// <summary>
    /// Serializes key material for immediate protection by the operating system.
    /// The caller must zero the returned buffer after use.
    /// </summary>
    public byte[] ToByteArray()
    {
        lock (_sync)
        {
            var key = _key ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial));
            var salt = _salt ?? throw new ObjectDisposedException(nameof(VaultKeyMaterial));
            var payload = new byte[HeaderSize + salt.Length + key.Length];

            PayloadMagic.CopyTo(payload);
            payload[4] = PayloadVersion;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), KdfParameters.MemorySizeKiB);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(9, 4), KdfParameters.Iterations);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(13, 4), KdfParameters.DegreeOfParallelism);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(17, 2), checked((ushort)salt.Length));
            salt.CopyTo(payload.AsSpan(HeaderSize));
            key.CopyTo(payload.AsSpan(HeaderSize + salt.Length));
            return payload;
        }
    }

    public static VaultKeyMaterial FromByteArray(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderSize + MinimumSaltSizeBytes + KeySizeBytes ||
            !payload[..4].SequenceEqual(PayloadMagic) ||
            payload[4] != PayloadVersion)
        {
            throw new InvalidDataException("Invalid vault key material payload.");
        }

        var parameters = new Argon2Parameters(
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(5, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(9, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(13, 4)));

        try
        {
            parameters.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException("Invalid KDF parameters in key material payload.", ex);
        }

        var saltLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(17, 2));
        if (saltLength is < MinimumSaltSizeBytes or > MaximumSaltSizeBytes ||
            payload.Length != HeaderSize + saltLength + KeySizeBytes)
        {
            throw new InvalidDataException("Invalid salt length in key material payload.");
        }

        var salt = payload.Slice(HeaderSize, saltLength);
        var key = payload.Slice(HeaderSize + saltLength, KeySizeBytes);
        return new VaultKeyMaterial(key, salt, parameters);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_key is not null)
            {
                CryptographicOperations.ZeroMemory(_key);
                _key = null;
            }

            if (_salt is not null)
            {
                CryptographicOperations.ZeroMemory(_salt);
                _salt = null;
            }
        }

        GC.SuppressFinalize(this);
    }
}
