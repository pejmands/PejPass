using PejPass.Infrastructure.Crypto;
using System.Security.Cryptography;

namespace PejPass.Infrastructure.Tests.Crypto;

public sealed class CryptoServiceTests
{
    [Fact]
    public void Decrypt_WhenAssociatedDataIsModified_ThrowsAuthenticationTagMismatchException()
    {
        var crypto = new CryptoService();

        var plaintext = "PejPass test"u8.ToArray();
        var key = crypto.DeriveKey(
            "password",
            crypto.GenerateSalt());

        var associatedData = "PEJP-v1-metadata"u8.ToArray();

        try
        {
            var (ciphertext, nonce, tag) = crypto.Encrypt(
                plaintext,
                key,
                associatedData);

            associatedData[0] ^= 0x01;

            Assert.Throws<AuthenticationTagMismatchException>(() =>
                crypto.Decrypt(
                    ciphertext,
                    nonce,
                    tag,
                    key,
                    associatedData));
        }
        finally
        {
            crypto.ZeroMemory(key);
        }
    }
}
