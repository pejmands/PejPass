using Konscious.Security.Cryptography;
using PejPass.Application.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace PejPass.Infrastructure.Crypto;

/// <summary>
/// Production-ready crypto implementation:
/// Argon2id for key derivation + AES-256-GCM for authenticated encryption.
/// </summary>
public sealed class CryptoService : ICryptoService
{
    // Conservative but strong defaults (adjustable later via settings)
    private const int Argon2MemorySizeKb = 65536;   // 64 MiB
    private const int Argon2Iterations = 3;
    private const int Argon2DegreeOfParallelism = 4;
    private const int KeySizeBytes = 32;             // 256-bit
    private const int NonceSizeBytes = 12;           // GCM standard
    private const int TagSizeBytes = 16;

    public byte[] DeriveKey(string masterPassword, byte[] salt)
    {
        if (string.IsNullOrEmpty(masterPassword))
            throw new ArgumentException("Master password cannot be empty.", nameof(masterPassword));
        if (salt is null || salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes.", nameof(salt));

        var passwordBytes = Encoding.UTF8.GetBytes(masterPassword);

        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = Argon2DegreeOfParallelism,
                MemorySize = Argon2MemorySizeKb,
                Iterations = Argon2Iterations
            };

            return argon2.GetBytes(KeySizeBytes);
        }
        finally
        {
            ZeroMemory(passwordBytes);
        }
    }

    public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
    byte[] plaintext,
    byte[] key,
    byte[]? associatedData = null)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException("Key must be 32 bytes.", nameof(key));

        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(
            nonce,
            plaintext,
            ciphertext,
            tag,
            associatedData);

        return (ciphertext, nonce, tag);
    }

    public byte[] Decrypt(
        byte[] ciphertext,
        byte[] nonce,
        byte[] tag,
        byte[] key,
        byte[]? associatedData = null)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException("Key must be 32 bytes.", nameof(key));

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(
            nonce,
            ciphertext,
            tag,
            plaintext,
            associatedData);

        return plaintext;
    }

    public byte[] GenerateSalt(int length = 16)
    {
        var salt = new byte[length];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    public void ZeroMemory(byte[] data)
    {
        if (data is null) return;
        CryptographicOperations.ZeroMemory(data);
    }
}
