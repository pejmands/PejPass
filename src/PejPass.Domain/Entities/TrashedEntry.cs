namespace PejPass.Domain.Entities;

/// <summary>Soft-deleted entry kept for recovery.</summary>
public sealed class TrashedEntry
{
    public VaultEntry Entry { get; set; } = null!;

    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;
}
