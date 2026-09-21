namespace PejPass.Domain.Entities;

/// <summary>Previous password value kept inside the encrypted vault.</summary>
public sealed class PasswordHistoryItem
{
    public string Password { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
