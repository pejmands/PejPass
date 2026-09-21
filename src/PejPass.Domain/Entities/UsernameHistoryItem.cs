namespace PejPass.Domain.Entities;

/// <summary>Previous username value kept inside the encrypted vault.</summary>
public sealed class UsernameHistoryItem
{
    public string Username { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
