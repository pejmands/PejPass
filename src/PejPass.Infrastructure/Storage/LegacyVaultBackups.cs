namespace PejPass.Infrastructure.Storage;

public static class LegacyVaultBackups
{
    private static readonly int[] sourceArray = [1, 2];

    public static IReadOnlyList<string> Find(string vaultPath) =>
        [.. sourceArray.Select(version => $"{vaultPath}.v{version}.bak")
            .Where(File.Exists)];
}
