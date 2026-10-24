namespace PejPass.Infrastructure.Storage;

public static class LegacyVaultBackups
{
    private static readonly int[] legacyVersions = [1, 2];

    public static IReadOnlyList<string> Find(string vaultPath) =>
        [.. legacyVersions.Select(version => $"{vaultPath}.v{version}.bak")
            .Where(File.Exists)];
}
