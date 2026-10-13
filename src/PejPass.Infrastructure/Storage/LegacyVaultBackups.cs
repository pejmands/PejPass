using System.IO;

namespace PejPass.Infrastructure.Storage;

public static class LegacyVaultBackups
{
    public static IReadOnlyList<string> Find(string vaultPath) =>
        [.. new[] { 1, 2 }
            .Select(version => $"{vaultPath}.v{version}.bak")
            .Where(File.Exists)];
}
