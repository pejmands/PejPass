using PejPass.Application.Interfaces;
using PejPass.Infrastructure.Storage;
using System.IO;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreValidationTests
{
    [Fact]
    public async Task OpenAsync_WhenSaltLengthIsTooSmall_ThrowsInvalidDataException()
    {
        var path = CreateVaultFile(
            version: 2,
            saltLength: 15);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(path, "password"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenSaltLengthIsTooLarge_ThrowsInvalidDataException()
    {
        var path = CreateVaultFile(
            version: 2,
            saltLength: 65);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(path, "password"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenFileIsTooShort_ThrowsInvalidDataException()
    {
        var path = CreateVaultFile(
            version: 2,
            saltLength: 16,
            minimumHeaderOnly: true);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(path, "password"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenCiphertextIsEmpty_ThrowsInvalidDataException()
    {
        var path = CreateVaultFile(
            version: 2,
            saltLength: 16,
            ciphertextLength: 0);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(path, "password"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenVersionIsUnsupported_ThrowsNotSupportedException()
    {
        var path = CreateVaultFile(
            version: 3,
            saltLength: 16);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                store.OpenAsync(path, "password"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VaultStore CreateStore()
    {
        return new VaultStore(
            new FakeCryptoService(),
            new FakeFileMover());
    }

    private static string CreateVaultFile(
        byte version,
        int saltLength,
        bool minimumHeaderOnly = false,
        int ciphertextLength = 1)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.pejp");

        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        stream.Write("PEJP"u8);
        stream.WriteByte(version);
        stream.Write(
            BitConverter.GetBytes((ushort)saltLength));

        stream.Write(new byte[saltLength]);

        if (minimumHeaderOnly)
            return path;

        stream.Write(new byte[12]);
        stream.Write(new byte[16]);

        if (ciphertextLength > 0)
            stream.Write(new byte[ciphertextLength]);

        return path;
    }

    private sealed class FakeCryptoService : ICryptoService
    {
        public byte[] DeriveKey(
            string masterPassword,
            byte[] salt)
        {
            return new byte[32];
        }

        public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
            byte[] plaintext,
            byte[] key,
            byte[]? associatedData = null)
        {
            return (
                plaintext,
                new byte[12],
                new byte[16]);
        }

        public byte[] Decrypt(
            byte[] ciphertext,
            byte[] nonce,
            byte[] tag,
            byte[] key,
            byte[]? associatedData = null)
        {
            return ciphertext;
        }

        public byte[] GenerateSalt(int length = 16)
        {
            return new byte[length];
        }

        public void ZeroMemory(byte[] data)
        {
            Array.Clear(data);
        }
    }

    private sealed class FakeFileMover : IFileMover
    {
        public void Move(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
        }
    }
}
