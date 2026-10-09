using PejPass.Application.Security;
using PejPass.Domain.Entities;
using System.Security.Cryptography;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

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
