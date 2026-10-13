using PejPass.Domain.Entities;

namespace PejPass.Domain.Tests;

public sealed class VaultReplaceContentTests
{
    [Fact]
    public void ReplaceContentFrom_CopiesBackupContentAndHistory_WhilePreservingCurrentKdfParameters()
    {
        var currentKdf = new Argon2Parameters(
            MemorySizeKiB: 32768,
            Iterations: 2,
            DegreeOfParallelism: 2);
        var backupKdf = new Argon2Parameters(
            MemorySizeKiB: 65536,
            Iterations: 3,
            DegreeOfParallelism: 4);
        var currentVaultId = Guid.NewGuid();
        var currentCreatedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var backupEntryId = Guid.NewGuid();
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-2);

        var currentVault = new Vault
        {
            Id = currentVaultId,
            CreatedAt = currentCreatedAt,
            Name = "Current vault",
            KdfParameters = currentKdf,
            Entries = [new VaultEntry { Title = "Old current entry" }],
            History = [new EntryHistoryItem { Title = "Old current history" }]
        };

        var backupHistory = new EntryHistoryItem
        {
            EntryId = backupEntryId,
            Title = "Previous backup title",
            Password = "previous-backup-password",
            Tags = ["previous"],
            CustomFields =
            [
                new CustomField
                {
                    Name = "Recovery",
                    Value = "previous-secret",
                    IsSecret = true
                }
            ]
        };

        var backupVault = new Vault
        {
            Name = "Backup vault",
            KdfParameters = backupKdf,
            Entries =
            [
                new VaultEntry
                {
                    Id = backupEntryId,
                    Title = "Backup entry",
                    Password = "backup-password",
                    Tags = ["backup"],
                    CustomFields =
                    [
                        new CustomField
                        {
                            Name = "Token",
                            Value = "backup-secret",
                            IsSecret = true
                        }
                    ]
                }
            ],
            History = [backupHistory],
            Trash =
            [
                new TrashedEntry
                {
                    Entry = new VaultEntry
                    {
                        Title = "Trashed backup entry",
                        Password = "trashed-password"
                    },
                    DeletedAt = deletedAt
                }
            ]
        };

        currentVault.ReplaceContentFrom(backupVault);

        Assert.Equal(currentVaultId, currentVault.Id);
        Assert.Equal(currentCreatedAt, currentVault.CreatedAt);
        Assert.Equal(currentKdf, currentVault.KdfParameters);
        Assert.Equal(backupKdf, backupVault.KdfParameters);
        Assert.Equal("Backup vault", currentVault.Name);

        var restoredEntry = Assert.Single(currentVault.Entries);
        Assert.Equal(backupEntryId, restoredEntry.Id);
        Assert.Equal("Backup entry", restoredEntry.Title);
        Assert.Equal("backup-password", restoredEntry.Password);
        Assert.Equal("backup-secret", Assert.Single(restoredEntry.CustomFields).Value);

        var restoredHistory = Assert.Single(currentVault.History);
        Assert.Equal(backupEntryId, restoredHistory.EntryId);
        Assert.Equal("Previous backup title", restoredHistory.Title);
        Assert.Equal("previous-backup-password", restoredHistory.Password);
        Assert.NotSame(backupHistory, restoredHistory);
        Assert.NotSame(backupHistory.Tags, restoredHistory.Tags);
        Assert.NotSame(backupHistory.CustomFields[0], restoredHistory.CustomFields[0]);

        var restoredTrash = Assert.Single(currentVault.Trash);
        Assert.Equal("Trashed backup entry", restoredTrash.Entry.Title);
        Assert.Equal(deletedAt, restoredTrash.DeletedAt);
        Assert.NotSame(backupVault.Trash[0].Entry, restoredTrash.Entry);

        backupHistory.Tags.Add("mutated");
        backupHistory.CustomFields[0].Value = "mutated-secret";

        Assert.Equal(["previous"], restoredHistory.Tags);
        Assert.Equal("previous-secret", restoredHistory.CustomFields[0].Value);
    }
}
