using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Sessions;

public sealed class VaultSessionTests
{
    [Fact]
    public async Task ClearWaitsUntilActiveSaveLeaseIsReleased()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var vault = new Vault();

        session.Open(
            vault,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            keyMaterial);

        var lease = await session.AcquireSaveLeaseAsync(
            session.Generation, vault, CancellationToken.None);
        Assert.NotNull(lease);

        var clearStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clearTask = Task.Run(() =>
        {
            clearStarted.SetResult();
            session.Clear();
        });

        await clearStarted.Task;
        await Task.Delay(50);
        Assert.False(clearTask.IsCompleted);

        lease.Dispose();
        await clearTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(session.IsActive);
        Assert.Null(await session.AcquireSaveLeaseAsync(
            session.Generation, vault, CancellationToken.None));
    }

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

    [Fact]
    public void ExternalFileChangeIsDetectedUntilDiskStampIsRefreshed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var vault = new Vault();

        try
        {
            File.WriteAllText(path, "initial");
            session.Open(vault, path, keyMaterial);

            Assert.False(session.HasExternalChanges());

            File.WriteAllText(path, "external change with a different length");

            Assert.True(session.HasExternalChanges());

            session.RefreshDiskStamp();

            Assert.False(session.HasExternalChanges());
        }
        finally
        {
            File.Delete(path);
        }
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
