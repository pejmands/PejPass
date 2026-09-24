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
            version: 1,
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
            version: 1,
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
        var path = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(
                path,
                "PEJP"u8.ToArray());

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
            version: 1,
            saltLength: 16,
            includeCiphertext: false);

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
            version: 2,
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
        ushort saltLength,
        bool includeCiphertext = true)
    {
        var path = Path.GetTempFileName();

        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        stream.Write("PEJP"u8);
        stream.WriteByte(version);

        stream.Write(BitConverter.GetBytes(saltLength));

        stream.Write(new byte[saltLength]);
        stream.Write(new byte[12]);
        stream.Write(new byte[16]);

        if (includeCiphertext)
            stream.WriteByte(0);

        return path;
    }

    private sealed class FakeFileMover : IFileMover
    {
        public void Move(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            File.Move(sourcePath, destinationPath, overwrite);
        }
    }

    private sealed class FakeCryptoService : ICryptoService
    {
        public byte[] GenerateSalt(int length)
        {
            return new byte[length];
        }

        public byte[] DeriveKey(
            string password,
            byte[] salt)
        {
            return new byte[32];
        }

        public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
            byte[] plaintext,
            byte[] key)
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
            byte[] key)
        {
            return ciphertext;
        }

        public void ZeroMemory(byte[] data)
        {
            Array.Clear(data);
        }
    }
}
