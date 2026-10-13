using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Sessions;

public sealed class VaultSessionTests
{
    [Fact]
    public void ClearInvalidatesPreviouslyCapturedSession()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var vault = new Vault();

        session.Open(
            vault,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            keyMaterial);

        var generation = session.Generation;

        Assert.True(session.IsCurrent(generation, vault));

        session.Clear();

        Assert.False(session.IsCurrent(generation, vault));
        Assert.NotEqual(generation, session.Generation);
    }

    [Fact]
    public void DiskStamp_DetectsExternalLengthAndTimestampChangesAndCanBeRefreshed()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var vaultPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");

        try
        {
            File.WriteAllBytes(vaultPath, [1, 2, 3]);

            session.Open(new Vault(), vaultPath, keyMaterial);
            var generation = session.Generation;

            Assert.False(session.HasExternalFileChanges(generation, vaultPath));

            File.WriteAllBytes(vaultPath, [1, 2, 3, 4]);
            Assert.True(session.HasExternalFileChanges(generation, vaultPath));

            Assert.True(session.TryRefreshDiskStamp(generation, vaultPath));
            Assert.False(session.HasExternalFileChanges(generation, vaultPath));

            var changedTimestamp = File.GetLastWriteTimeUtc(vaultPath).AddMinutes(-2);
            File.WriteAllBytes(vaultPath, [4, 3, 2, 1]);
            File.SetLastWriteTimeUtc(vaultPath, changedTimestamp);

            Assert.True(session.HasExternalFileChanges(generation, vaultPath));
        }
        finally
        {
            if (File.Exists(vaultPath))
                File.Delete(vaultPath);
        }
    }

    [Fact]
    public void ReplacingVaultInvalidatesPriorVaultIdentity()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var originalVault = new Vault();
        var replacementVault = new Vault();

        session.Open(
            originalVault,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            keyMaterial);

        var generation = session.Generation;

        session.ReplaceVault(replacementVault);

        Assert.False(session.IsCurrent(generation, originalVault));
        Assert.True(session.IsCurrent(session.Generation, replacementVault));
    }

    [Fact]
    public void UpdatingKeyMaterialInvalidatesPreviouslyCapturedSession()
    {
        using var session = new VaultSession();
        using var originalMaterial = CreateMaterial();
        using var replacementMaterial = CreateMaterial();
        var vault = new Vault();

        session.Open(
            vault,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            originalMaterial);

        var generation = session.Generation;

        Assert.True(session.TryUpdateKeyMaterial(replacementMaterial));

        Assert.False(session.IsCurrent(generation, vault));
        Assert.True(session.IsCurrent(session.Generation, vault));
    }


    [Fact]
    public async Task DelayedOperationCannotTreatReplacementSessionAsCurrent()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var originalVault = new Vault();
        var replacementVault = new Vault();

        session.Open(
            originalVault,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            keyMaterial);

        var capturedGeneration = session.Generation;
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var delayedOperation = Task.Run(async () =>
        {
            operationStarted.SetResult();
            await continueOperation.Task;

            // Match the guard used by production continuations after asynchronous work.
            return session.IsCurrent(capturedGeneration, originalVault);
        });

        await operationStarted.Task;
        session.ReplaceVault(replacementVault);
        continueOperation.SetResult();

        Assert.False(await delayedOperation);
        Assert.Same(replacementVault, session.Vault);
        Assert.False(session.IsCurrent(capturedGeneration, originalVault));
        Assert.True(session.IsCurrent(session.Generation, replacementVault));
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = new byte[32];
        var salt = new byte[16];

        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(salt);

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
