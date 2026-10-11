using PejPass.Domain.Entities;
using System.Text.Json;

namespace PejPass.Domain.Tests;

public sealed class LegacyCredentialHistoryTests
{
    // Shape written by older PejPass versions.
    private const string LegacyEntryJson = """
    {
      "Id": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
      "Title": "Example",
      "Password": "current-password",
      "PasswordHistory": [
        { "Password": "old-secret-password", "ChangedAt": "2026-01-01T00:00:00+00:00" }
      ],
      "UsernameHistory": [
        { "Username": "old-user", "ChangedAt": "2026-01-01T00:00:00+00:00" }
      ]
    }
    """;

    [Fact]
    public void Deserialize_EntryWithLegacyHistoryProperties_StillLoads()
    {
        var entry = JsonSerializer.Deserialize<VaultEntry>(LegacyEntryJson);

        Assert.NotNull(entry);
        Assert.Equal("Example", entry.Title);
        Assert.Equal("current-password", entry.Password);
    }

    [Fact]
    public void Serialize_AfterLoadingLegacyEntry_DropsOldCredentials()
    {
        var entry = JsonSerializer.Deserialize<VaultEntry>(LegacyEntryJson)!;

        var json = JsonSerializer.Serialize(entry);

        Assert.DoesNotContain("old-secret-password", json);
        Assert.DoesNotContain("old-user", json);
        Assert.DoesNotContain("PasswordHistory", json);
        Assert.DoesNotContain("UsernameHistory", json);
    }
}
