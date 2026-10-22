using PejPass.Infrastructure.Storage;

namespace PejPass.Infrastructure.Tests.Storage;

public sealed class LegacyVaultBackupsTests
{
    [Fact]
    public void Find_ReturnsOnlyExistingV1AndV2Backups()
    {
        var vaultPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");
        var v1Backup = $"{vaultPath}.v1.bak";
        var v2Backup = $"{vaultPath}.v2.bak";
        var v3Backup = $"{vaultPath}.v3.bak";
        var extendedBackup = $"{vaultPath}.v1.bak.extra";

        try
        {
            File.WriteAllText(v1Backup, "v1");
            File.WriteAllText(v2Backup, "v2");
            File.WriteAllText(v3Backup, "v3");
            File.WriteAllText(extendedBackup, "extra");

            Assert.Equal(
                new[] { v1Backup, v2Backup },
                LegacyVaultBackups.Find(vaultPath));
        }
        finally
        {
            foreach (var path in new[] { v1Backup, v2Backup, v3Backup, extendedBackup })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    [Fact]
    public void Find_ReturnsEmptyWhenNoLegacyBackupsExist()
    {
        var vaultPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");

        Assert.Empty(LegacyVaultBackups.Find(vaultPath));
    }
}
