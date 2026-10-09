using PejPass.Application.Interfaces;
using PejPass.Application.Security;
using PejPass.Domain.Entities;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PejPass.Infrastructure.Storage;

/// <summary>
/// Encrypted vault file format:
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
    private const long MaxVaultFileBytes = 128L * 1024 * 1024;

    private const int MaxVaultEntries = 100_000;
    private const int MaxVaultHistoryItems = 1_000_000;
    private const int MaxCollectionItemsPerEntry = 256;
    private const int MaxTextCharacters = 1_048_576;
    private const int MaxTagCharacters = 4_096;

    private static void ValidateVaultStructure(Vault vault)
    {
        if (vault.Entries is null || vault.Trash is null || vault.History is null)
            throw new InvalidDataException("Vault contains a missing required collection.");

        if (vault.Entries.Count + vault.Trash.Count > MaxVaultEntries)
            throw new InvalidDataException($"Vault exceeds the limit of {MaxVaultEntries} active and trashed entries.");

        if (vault.History.Count > MaxVaultHistoryItems)
            throw new InvalidDataException($"Vault exceeds the limit of {MaxVaultHistoryItems} history items.");

        ValidateText(vault.Name, "vault name");

        foreach (var entry in vault.Entries)
            ValidateEntry(entry);

        foreach (var trashedEntry in vault.Trash)
        {
            if (trashedEntry?.Entry is null)
                throw new InvalidDataException("Vault contains an invalid trash entry.");

            ValidateEntry(trashedEntry.Entry);
        }

        foreach (var history in vault.History)
        {
            if (history is null)
                throw new InvalidDataException("Vault contains an invalid history item.");

            ValidateText(history.Title, "history title");
            ValidateText(history.Username, "history username");
            ValidateText(history.Password, "history password");
            ValidateText(history.Url, "history URL");
            ValidateText(history.TotpSecret, "history TOTP secret");
            ValidateText(history.Notes, "history notes");
            ValidateTags(history.Tags);
            ValidateCustomFields(history.CustomFields);
        }
    }

    private static void ValidateEntry(VaultEntry? entry)
    {
        if (entry is null)
            throw new InvalidDataException("Vault contains an invalid entry.");

        ValidateText(entry.Title, "entry title");
        ValidateText(entry.Username, "entry username");
        ValidateText(entry.Password, "entry password");
        ValidateText(entry.Url, "entry URL");
        ValidateText(entry.TotpSecret, "entry TOTP secret");
        ValidateText(entry.Notes, "entry notes");
        ValidateTags(entry.Tags);
        ValidateCustomFields(entry.CustomFields);

        if (entry.PasswordHistory is null || entry.UsernameHistory is null)
            throw new InvalidDataException("Vault entry contains a missing history collection.");

        if (entry.PasswordHistory.Count > VaultEntry.MaxPasswordHistory ||
            entry.UsernameHistory.Count > VaultEntry.MaxUsernameHistory)
            throw new InvalidDataException("Vault entry exceeds the supported credential history limit.");

        foreach (var item in entry.PasswordHistory)
        {
            if (item is null)
                throw new InvalidDataException("Vault entry contains an invalid password history item.");

            ValidateText(item.Password, "historical password");
        }

        foreach (var item in entry.UsernameHistory)
        {
            if (item is null)
                throw new InvalidDataException("Vault entry contains an invalid username history item.");

            ValidateText(item.Username, "historical username");
        }
    }

    private static void ValidateTags(List<string>? tags)
    {
        if (tags is null || tags.Count > MaxCollectionItemsPerEntry)
            throw new InvalidDataException("Vault contains an invalid or oversized tag collection.");

        foreach (var tag in tags)
        {
            if (tag is null || tag.Length > MaxTagCharacters)
                throw new InvalidDataException("Vault contains an invalid or oversized tag.");
        }
    }

    private static void ValidateCustomFields(List<CustomField>? fields)
    {
        if (fields is null || fields.Count > MaxCollectionItemsPerEntry)
            throw new InvalidDataException("Vault contains an invalid or oversized custom-field collection.");

        foreach (var field in fields)
        {
            if (field is null)
                throw new InvalidDataException("Vault contains an invalid custom field.");

            ValidateText(field.Name, "custom-field name");
            ValidateText(field.Value, "custom-field value");
        }
    }

    private static void ValidateText(string? value, string fieldName)
    {
        if (value is null || value.Length > MaxTextCharacters)
            throw new InvalidDataException($"Vault contains an invalid or oversized {fieldName}.");
    }

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
        using var session = await CreateSessionAsync(path, masterPassword, vault, ct);
    }

    public async Task<VaultSessionData> CreateSessionAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(vault);

        var kdfParameters = vault.KdfParameters;
        kdfParameters.Validate();

        var salt = _crypto.GenerateSalt(SaltLength);
        byte[]? key = null;

        try
        {
            key = _crypto.DeriveKey(masterPassword, salt, kdfParameters);
            using var material = new VaultKeyMaterial(key, salt, kdfParameters);
            var encrypted = EncryptVault(vault, material);
            var headerSalt = material.CopySalt();

            try
            {
                await using var fs = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);

                await WriteVaultAsync(
                    fs,
                    headerSalt,
                    kdfParameters,
                    encrypted.Nonce,
                    encrypted.Tag,
                    encrypted.Ciphertext,
                    ct);

                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }
            finally
            {
                _crypto.ZeroMemory(headerSalt);
                ClearEncryptedPayload(encrypted);
            }

            return new VaultSessionData(vault, material.Clone());
        }
        finally
        {
            if (key is not null)
                _crypto.ZeroMemory(key);
            _crypto.ZeroMemory(salt);
        }
    }

    public Task<VaultSessionData> OpenSessionAsync(
        string path,
        string masterPassword,
        CancellationToken ct = default) =>
        OpenCoreAsync(
            path,
            (salt, parameters) => _crypto.DeriveKey(masterPassword, salt, parameters),
            expectedMaterial: null,
            ct);

    public async Task<Vault> OpenAsync(
        string path,
        string masterPassword,
        CancellationToken ct = default)
    {
        using var session = await OpenSessionAsync(path, masterPassword, ct);
        return session.Vault;
    }

    public async Task<Vault> OpenWithKeyAsync(
        string path,
        VaultKeyMaterial keyMaterial,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);

        using var session = await OpenCoreAsync(
            path,
            (_, _) => keyMaterial.CopyKey(),
            keyMaterial,
            ct);

        return session.Vault;
    }

    private async Task<VaultSessionData> OpenCoreAsync(
        string path,
        Func<byte[], Argon2Parameters, byte[]> keyFactory,
        VaultKeyMaterial? expectedMaterial,
        CancellationToken ct)
    {
        Vault? vault = null;
        VaultKeyMaterial? material = null;
        byte versionValue = 0;
        byte[]? salt = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? ciphertext = null;
        byte[]? key = null;
        byte[]? plaintext = null;

        try
        {
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

                if (fs.Length > MaxVaultFileBytes)
                    throw new InvalidDataException("Vault file exceeds the maximum supported size.");

                if (fs.Length < minimumHeaderLength)
                    throw new InvalidDataException("Vault file is too short.");

                var magic = new byte[Magic.Length];
                await fs.ReadExactlyAsync(magic, ct);

                if (!magic.AsSpan().SequenceEqual(Magic))
                    throw new InvalidDataException("Not a valid PejPass vault file.");

                var version = new byte[1];
                await fs.ReadExactlyAsync(version);
                versionValue = version[0];

                if (versionValue != LegacyVersion &&
                    versionValue != PreviousVersion &&
                    versionValue != CurrentVersion)
                {
                    throw new NotSupportedException($"Unsupported vault version: {versionValue}");
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
                        throw new InvalidDataException("Vault contains invalid Argon2 parameters.", ex);
                    }
                }

                var saltLenBytes = new byte[sizeof(ushort)];
                await fs.ReadExactlyAsync(saltLenBytes, ct);

                var saltLen = BitConverter.ToUInt16(saltLenBytes);
                if (saltLen < SaltLength || saltLen > MaxSaltLength)
                    throw new InvalidDataException($"Invalid salt length: {saltLen}.");

                if (fs.Length - fs.Position < saltLen + NonceLength + TagLength + 1)
                    throw new InvalidDataException("Vault file is truncated.");

                salt = new byte[saltLen];
                await fs.ReadExactlyAsync(salt, ct);

                if (expectedMaterial is not null)
                {
                    if (expectedMaterial.KdfParameters != kdfParameters ||
                        !CryptographicOperations.FixedTimeEquals(expectedMaterial.Salt.Span, salt))
                    {
                        throw new AuthenticationTagMismatchException();
                    }
                }

                var associatedData = versionValue switch
                {
                    CurrentVersion => BuildAssociatedDataV3(kdfParameters, salt),
                    PreviousVersion => BuildAssociatedDataV2(versionValue, salt),
                    _ => null
                };

                nonce = new byte[NonceLength];
                await fs.ReadExactlyAsync(nonce, ct);

                tag = new byte[TagLength];
                await fs.ReadExactlyAsync(tag, ct);

                var ciphertextLength = fs.Length - fs.Position;
                if (ciphertextLength < 1)
                    throw new InvalidDataException("Vault ciphertext is empty.");

                if (ciphertextLength > int.MaxValue)
                    throw new InvalidDataException("Vault ciphertext is too large.");

                ciphertext = new byte[(int)ciphertextLength];
                await fs.ReadExactlyAsync(ciphertext, ct);

                try
                {
                    key = keyFactory(salt, kdfParameters);
                    plaintext = _crypto.Decrypt(ciphertext, nonce, tag, key, associatedData);
                    vault = JsonSerializer.Deserialize<Vault>(plaintext)
                            ?? throw new InvalidDataException("Vault data is corrupted.");

                    ValidateVaultStructure(vault);

                    if (versionValue == CurrentVersion &&
                        vault.KdfParameters != kdfParameters)
                    {
                        throw new InvalidDataException(
                            "Vault KDF parameters do not match the authenticated header.");
                    }

                    vault.KdfParameters = kdfParameters;
                    material = expectedMaterial?.Clone()
                        ?? new VaultKeyMaterial(key, salt, kdfParameters);
                }
                finally
                {
                    if (key is not null)
                    {
                        _crypto.ZeroMemory(key);
                        key = null;
                    }

                    if (plaintext is not null)
                    {
                        _crypto.ZeroMemory(plaintext);
                        plaintext = null;
                    }

                    if (associatedData is not null)
                        _crypto.ZeroMemory(associatedData);
                }
            }

            if (versionValue != CurrentVersion)
                await SaveAsync(path, material!, vault!, ct);

            var result = new VaultSessionData(vault!, material!);
            material = null;
            return result;
        }
        catch
        {
            material?.Dispose();
            throw;
        }
        finally
        {
            if (key is not null)
                _crypto.ZeroMemory(key);
            if (plaintext is not null)
                _crypto.ZeroMemory(plaintext);
            if (salt is not null)
                _crypto.ZeroMemory(salt);
            if (nonce is not null)
                _crypto.ZeroMemory(nonce);
            if (tag is not null)
                _crypto.ZeroMemory(tag);
            if (ciphertext is not null)
                _crypto.ZeroMemory(ciphertext);
        }
    }

    public async Task SaveAsync(
        string path,
        string masterPassword,
        Vault vault,
        CancellationToken ct = default)
    {
        using var material = await SaveWithNewPasswordAsync(
            path,
            masterPassword,
            vault,
            vault.KdfParameters,
            ct);
    }

    public async Task<VaultKeyMaterial> SaveWithNewPasswordAsync(
        string path,
        string masterPassword,
        Vault vault,
        Argon2Parameters kdfParameters,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(kdfParameters);
        kdfParameters.Validate();

        if (vault.KdfParameters != kdfParameters)
            throw new ArgumentException("KDF parameters must match the vault metadata.", nameof(kdfParameters));

        var salt = _crypto.GenerateSalt(SaltLength);
        byte[]? key = null;

        try
        {
            key = _crypto.DeriveKey(masterPassword, salt, kdfParameters);
            using var material = new VaultKeyMaterial(key, salt, kdfParameters);
            await SaveAsync(path, material, vault, ct);
            return material.Clone();
        }
        finally
        {
            if (key is not null)
                _crypto.ZeroMemory(key);
            _crypto.ZeroMemory(salt);
        }
    }

    public async Task SaveAsync(
        string path,
        VaultKeyMaterial keyMaterial,
        Vault vault,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ArgumentNullException.ThrowIfNull(vault);

        var kdfParameters = keyMaterial.KdfParameters;
        kdfParameters.Validate();

        if (vault.KdfParameters != kdfParameters)
            throw new InvalidOperationException("Vault KDF parameters do not match the active key material.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? throw new InvalidOperationException("Vault directory could not be determined.");

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        var salt = keyMaterial.CopySalt();
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag)? encrypted = null;

        try
        {
            encrypted = EncryptVault(vault, keyMaterial);

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
                    encrypted.Value.Nonce,
                    encrypted.Value.Tag,
                    encrypted.Value.Ciphertext,
                    ct);

                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }

            _fileMover.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _crypto.ZeroMemory(salt);
            if (encrypted is not null)
                ClearEncryptedPayload(encrypted.Value);

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

    private (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptVault(
        Vault vault,
        VaultKeyMaterial keyMaterial)
    {
        ValidateVaultStructure(vault);

        byte[]? key = null;
        byte[]? salt = null;
        byte[]? json = null;
        byte[]? associatedData = null;

        try
        {
            key = keyMaterial.CopyKey();
            salt = keyMaterial.CopySalt();
            json = JsonSerializer.SerializeToUtf8Bytes(vault);
            associatedData = BuildAssociatedDataV3(keyMaterial.KdfParameters, salt);
            return _crypto.Encrypt(json, key, associatedData);
        }
        finally
        {
            if (key is not null)
                _crypto.ZeroMemory(key);
            if (salt is not null)
                _crypto.ZeroMemory(salt);
            if (json is not null)
                _crypto.ZeroMemory(json);
            if (associatedData is not null)
                _crypto.ZeroMemory(associatedData);
        }
    }

    private void ClearEncryptedPayload((byte[] Ciphertext, byte[] Nonce, byte[] Tag) encrypted)
    {
        _crypto.ZeroMemory(encrypted.Ciphertext);
        _crypto.ZeroMemory(encrypted.Nonce);
        _crypto.ZeroMemory(encrypted.Tag);
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
        await fs.WriteAsync(BitConverter.GetBytes((ushort)salt.Length), ct);

        await fs.WriteAsync(salt, ct);
        await fs.WriteAsync(nonce, ct);
        await fs.WriteAsync(tag, ct);
        await fs.WriteAsync(ciphertext, ct);
    }
}
