namespace PejPass.Application.Interfaces;

/// <summary>
/// Abstraction over key derivation and authenticated encryption.
/// </summary>
public interface ICryptoService
{
    /// <summary>
    /// Derives a 256-bit key from the master password using Argon2id.
    /// </summary>
    byte[] DeriveKey(string masterPassword, byte[] salt);

    /// <summary>
    /// Encrypts plaintext with AES-256-GCM. Returns (ciphertext, nonce, tag).
    /// </summary>
    (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
        byte[] plaintext,
        byte[] key,
        byte[]? associatedData = null);

    /// <summary>
    /// Decrypts data encrypted with Encrypt. Throws on authentication failure.
    /// </summary>
    byte[] Decrypt(
        byte[] ciphertext,
        byte[] nonce,
        byte[] tag,
        byte[] key,
        byte[]? associatedData = null);

    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// </summary>
    byte[] GenerateSalt(int length = 16);

    /// <summary>
    /// Securely zeros a byte array.
    /// </summary>
    void ZeroMemory(byte[] data);
}
