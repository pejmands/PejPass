using PejPass.Domain.Entities;

namespace PejPass.Domain.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void UpdateEntry_CreatesFullSnapshot()
    {
        var vault = new Vault();

        var entry = CreateEntry(
            title: "Original",
            username: "user",
            password: "password-1",
            url: "https://example.com",
            totpSecret: "TOTP-1",
            notes: "Original notes",
            tags: ["personal", "important"],
            customFields:
            [
                new CustomField
                {
                    Name = "Recovery",
                    Value = "secret",
                    IsSecret = true
                }
            ]);

        vault.AddEntry(entry);

        var updated = CloneEntry(
            entry,
            title: "Updated",
            username: "new-user",
            password: "password-2",
            notes: "Updated notes",
            tags: ["work"],
            customFields:
            [
                new CustomField
                {
                    Name = "Department",
                    Value = "Engineering"
                }
            ]);

        var result = vault.UpdateEntry(updated);

        Assert.True(result);
        Assert.Single(vault.History);

        var snapshot = vault.History[0];

        Assert.Equal(entry.Id, snapshot.EntryId);
        Assert.Equal("Original", snapshot.Title);
        Assert.Equal("user", snapshot.Username);
        Assert.Equal("password-1", snapshot.Password);
        Assert.Equal("https://example.com", snapshot.Url);
        Assert.Equal("TOTP-1", snapshot.TotpSecret);
        Assert.Equal("Original notes", snapshot.Notes);
        Assert.Equal(["personal", "important"], snapshot.Tags);

        var customField = Assert.Single(snapshot.CustomFields);
        Assert.Equal("Recovery", customField.Name);
        Assert.Equal("secret", customField.Value);
        Assert.True(customField.IsSecret);

        Assert.Equal(entry.IsFavorite, snapshot.IsFavorite);
        Assert.Equal(entry.SortOrder, snapshot.SortOrder);
        Assert.Equal(entry.CreatedAt, snapshot.CreatedAt);
    }

    [Fact]
    public void UpdateEntry_WithNoChanges_DoesNotCreateHistory()
    {
        var vault = new Vault();

        var entry = CreateEntry();
        vault.AddEntry(entry);

        var updated = CloneEntry(entry);

        var result = vault.UpdateEntry(updated);

        Assert.False(result);
        Assert.Empty(vault.History);
        Assert.Same(entry, vault.Entries[0]);
    }

    [Fact]
    public void HistorySnapshot_IsIndependentFromEntryCollections()
    {
        var vault = new Vault();

        var entry = CreateEntry(
            tags: ["one"],
            customFields:
            [
                new CustomField
                {
                    Name = "Field",
                    Value = "Value",
                    IsSecret = true
                }
            ]);

        vault.AddEntry(entry);

        var updated = CloneEntry(entry, title: "Updated");

        vault.UpdateEntry(updated);

        entry.Tags.Add("two");
        entry.CustomFields[0].Value = "Changed";

        var snapshot = Assert.Single(vault.History);

        Assert.Equal(["one"], snapshot.Tags);
        Assert.Equal("Value", snapshot.CustomFields[0].Value);
    }

    [Fact]
    public void UpdateEntry_KeepsOnlyTenHistorySnapshotsPerEntry()
    {
        var vault = new Vault();

        var entry = CreateEntry(title: "Version 0");
        vault.AddEntry(entry);

        for (var i = 1; i <= 11; i++)
        {
            var updated = CloneEntry(
                vault.FindEntry(entry.Id)!,
                title: $"Version {i}");

            Assert.True(vault.UpdateEntry(updated));
        }

        var history = vault.History
            .Where(h => h.EntryId == entry.Id)
            .OrderByDescending(h => h.ChangedAt)
            .ToList();

        Assert.Equal(Vault.MaxHistoryPerEntry, history.Count);

        Assert.DoesNotContain(
            history,
            h => h.Title == "Version 0");

        Assert.Contains(
            history,
            h => h.Title == "Version 1");

        Assert.Contains(
            history,
            h => h.Title == "Version 10");
    }

    [Fact]
    public void RestoreHistory_RestoresSnapshotAndPreservesCurrentStateInHistory()
    {
        var vault = new Vault();

        var original = CreateEntry(
            title: "Original",
            username: "user-1",
            password: "password-1",
            notes: "Original notes",
            tags: ["original"],
            customFields:
            [
                new CustomField
                {
                    Name = "Field",
                    Value = "Original",
                    IsSecret = true
                }
            ]);

        vault.AddEntry(original);

        var updated = CloneEntry(
            original,
            title: "Updated",
            username: "user-2",
            password: "password-2",
            notes: "Updated notes",
            tags: ["updated"],
            customFields:
            [
                new CustomField
                {
                    Name = "Field",
                    Value = "Updated",
                    IsSecret = false
                }
            ]);

        Assert.True(vault.UpdateEntry(updated));

        var snapshot = Assert.Single(vault.History);

        Assert.True(vault.RestoreHistory(snapshot));

        var restored = Assert.Single(vault.Entries);

        Assert.Equal("Original", restored.Title);
        Assert.Equal("user-1", restored.Username);
        Assert.Equal("password-1", restored.Password);
        Assert.Equal("Original notes", restored.Notes);
        Assert.Equal(["original"], restored.Tags);

        var restoredField = Assert.Single(restored.CustomFields);
        Assert.Equal("Original", restoredField.Value);
        Assert.True(restoredField.IsSecret);

        Assert.Equal(2, vault.History.Count);
        Assert.Contains(vault.History, h => h.Title == "Updated");
    }

    [Fact]
    public void RestoreHistory_ForMissingEntry_ReturnsFalse()
    {
        var vault = new Vault();

        var history = new EntryHistoryItem
        {
            EntryId = Guid.NewGuid(),
            Title = "Missing"
        };

        var result = vault.RestoreHistory(history);

        Assert.False(result);
        Assert.Empty(vault.History);
    }

    [Fact]
    public void PurgeFromTrash_RemovesEntryHistory()
    {
        var vault = new Vault();

        var entry = CreateEntry();
        vault.AddEntry(entry);

        var updated = CloneEntry(entry, title: "Updated");
        Assert.True(vault.UpdateEntry(updated));

        Assert.Single(vault.History);

        Assert.True(vault.SoftDelete(entry.Id));
        Assert.True(vault.PurgeFromTrash(entry.Id));

        Assert.Empty(vault.History);
    }

    [Fact]
    public void EmptyTrash_RemovesHistoryForTrashedEntries()
    {
        var vault = new Vault();

        var entry = CreateEntry();
        vault.AddEntry(entry);

        var updated = CloneEntry(entry, title: "Updated");
        Assert.True(vault.UpdateEntry(updated));

        Assert.True(vault.SoftDelete(entry.Id));
        Assert.Single(vault.History);

        vault.EmptyTrash();

        Assert.Empty(vault.History);
        Assert.Empty(vault.Trash);
    }

    private static VaultEntry CreateEntry(
        string title = "Test Entry",
        string username = "user",
        string password = "password",
        string url = "https://example.com",
        string totpSecret = "TOTP",
        string notes = "Notes",
        List<string>? tags = null,
        List<CustomField>? customFields = null)
    {
        return new VaultEntry
        {
            Title = title,
            Username = username,
            Password = password,
            Url = url,
            TotpSecret = totpSecret,
            Notes = notes,
            Tags = tags ?? ["test"],
            CustomFields = customFields ?? []
        };
    }

    private static VaultEntry CloneEntry(
        VaultEntry source,
        string? title = null,
        string? username = null,
        string? password = null,
        string? notes = null,
        List<string>? tags = null,
        List<CustomField>? customFields = null)
    {
        return new VaultEntry
        {
            Id = source.Id,
            Title = title ?? source.Title,
            Username = username ?? source.Username,
            Password = password ?? source.Password,
            Url = source.Url,
            TotpSecret = source.TotpSecret,
            Notes = notes ?? source.Notes,
            Tags = tags ?? [.. source.Tags],
            CustomFields = customFields ?? [.. source.CustomFields.Select(field => new CustomField
            {
                Name = field.Name,
                Value = field.Value,
                IsSecret = field.IsSecret
            })],
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            CreatedAt = source.CreatedAt
        };
    }
}
