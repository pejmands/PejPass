using PejPass.Application.Interfaces;
using PejPass.Domain.Entities;
using PejPass.Infrastructure.Storage;
using System.IO;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class VaultStoreValidationTests
{
    [Fact]
    public async Task OpenAsync_WhenSaltLengthIsTooSmall_ThrowsInvalidDataException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = CreateVaultFile(
            version: 2,
            saltLength: 15);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(
                    path,
                    "password",
                    cancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenSaltLengthIsTooLarge_ThrowsInvalidDataException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = CreateVaultFile(
            version: 2,
            saltLength: 65);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(
                    path,
                    "password",
                    cancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenFileIsTooShort_ThrowsInvalidDataException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = CreateVaultFile(
            version: 2,
            saltLength: 16,
            minimumHeaderOnly: true);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(
                    path,
                    "password",
                    cancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenCiphertextIsEmpty_ThrowsInvalidDataException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = CreateVaultFile(
            version: 2,
            saltLength: 16,
            ciphertextLength: 0);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(
                    path,
                    "password",
                    cancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenVersionIsUnsupported_ThrowsNotSupportedException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = CreateVaultFile(
            version: 4,
            saltLength: 16);

        try
        {
            var store = CreateStore();

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                store.OpenAsync(
                    path,
                    "password",
                    cancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(262145, 3, 4)]
    [InlineData(65536, 0, 4)]
    [InlineData(65536, 11, 4)]
    [InlineData(65536, 3, 0)]
    [InlineData(65536, 3, 9)]
    [InlineData(8191, 3, 2)]
    public async Task OpenAsync_WhenArgon2ParametersAreInvalid_RejectsBeforeDerivingKey(
        int memorySizeKiB,
        int iterations,
        int degreeOfParallelism)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = CreateV3VaultFile(memorySizeKiB, iterations, degreeOfParallelism);
        var crypto = new FakeCryptoService();
        var store = new VaultStore(crypto, new FakeFileMover());

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.OpenAsync(path, "password", cancellationToken));

            Assert.Equal(0, crypto.DeriveKeyCallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenRequiredCollectionIsNull_ThrowsInvalidDataException()
    {
        var path = CreateV2VaultFile("{\"Name\":\"Test\",\"Entries\":null,\"Trash\":[],\"History\":[]}");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CreateStore().OpenAsync(path, "password", TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenEntryFieldExceedsLimit_ThrowsInvalidDataException()
    {
        var oversizedTitle = new string('x', 1_048_577);
        var json = System.Text.Json.JsonSerializer.Serialize(new Vault
        {
            Entries = [new VaultEntry { Title = oversizedTitle }]
        });
        var path = CreateV2VaultFile(json);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CreateStore().OpenAsync(path, "password", TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenEntryCountExceedsLimit_ThrowsInvalidDataException()
    {
        var entries = new List<VaultEntry>(100_001);
        for (var i = 0; i < 100_001; i++)
            entries.Add(new VaultEntry { Title = "Entry" + i });

        var json = System.Text.Json.JsonSerializer.Serialize(new Vault { Entries = entries });
        var path = CreateV2VaultFile(json);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CreateStore().OpenAsync(path, "password", TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateSessionAsync_WhenEntryFieldExceedsLimit_DoesNotCreateVaultFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        var vault = new Vault
        {
            Entries = [new VaultEntry { Title = new string('x', 1_048_577) }]
        };

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CreateStore().CreateSessionAsync(
                    path,
                    "password",
                    vault,
                    TestContext.Current.CancellationToken));

            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenKeyDerivationFails_ClearsReadSalt()
    {
        var path = CreateV2VaultFile("{}");
        var crypto = new FakeCryptoService { ThrowOnDeriveKey = true };
        var store = new VaultStore(crypto, new FakeFileMover());

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.OpenAsync(path, "password", TestContext.Current.CancellationToken));

            Assert.NotNull(crypto.LastDerivedSalt);
            Assert.All(crypto.LastDerivedSalt!, value => Assert.Equal(0, value));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateSessionAsync_WhenKeyDerivationFails_ClearsGeneratedSalt()
    {
        var crypto = new FakeCryptoService { ThrowOnDeriveKey = true };
        var store = new VaultStore(crypto, new FakeFileMover());
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateSessionAsync(
                path,
                "password",
                new Vault(),
                TestContext.Current.CancellationToken));

        Assert.NotNull(crypto.LastGeneratedSalt);
        Assert.All(crypto.LastGeneratedSalt!, value => Assert.Equal(0, value));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SaveWithNewPasswordAsync_WhenKeyDerivationFails_ClearsGeneratedSalt()
    {
        var crypto = new FakeCryptoService { ThrowOnDeriveKey = true };
        var store = new VaultStore(crypto, new FakeFileMover());
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        var vault = new Vault();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveWithNewPasswordAsync(
                path,
                "password",
                vault,
                vault.KdfParameters,
                TestContext.Current.CancellationToken));

        Assert.NotNull(crypto.LastGeneratedSalt);
        Assert.All(crypto.LastGeneratedSalt!, value => Assert.Equal(0, value));
        Assert.False(File.Exists(path));
    }

    private static string CreateV2VaultFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write("PEJP"u8);
        stream.WriteByte(2);
        stream.Write(BitConverter.GetBytes((ushort)16));
        stream.Write(new byte[16 + 12 + 16]);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
        return path;
    }

    private static string CreateV3VaultFile(
        int memorySizeKiB,
        int iterations = 3,
        int degreeOfParallelism = 4)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejp");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        stream.Write("PEJP"u8);
        stream.WriteByte(3);
        stream.WriteByte(1);
        stream.Write(BitConverter.GetBytes(memorySizeKiB));
        stream.Write(BitConverter.GetBytes(iterations));
        stream.Write(BitConverter.GetBytes(degreeOfParallelism));
        stream.Write(BitConverter.GetBytes((ushort)16));
        stream.Write(new byte[16 + 12 + 16 + 1]);

        return path;
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
        public int DeriveKeyCallCount { get; private set; }

        public bool ThrowOnDeriveKey { get; init; }

        public byte[]? LastGeneratedSalt { get; private set; }

        public byte[]? LastDerivedSalt { get; private set; }

        public byte[] DeriveKey(
            string masterPassword,
            byte[] salt,
            Argon2Parameters? parameters = null)
        {
            DeriveKeyCallCount++;
            LastDerivedSalt = salt;
            if (ThrowOnDeriveKey)
                throw new InvalidOperationException("Simulated key derivation failure.");

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
            var salt = Enumerable.Repeat((byte)0x5A, length).ToArray();
            LastGeneratedSalt = salt;
            return salt;
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
