namespace PejPass.Domain.Health;

public static class VaultHealthRules
{
    public static TimeSpan StaleThreshold { get; } = TimeSpan.FromDays(365);
}
