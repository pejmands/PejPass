namespace PejPass.Domain.Entities;

/// <summary>
/// A single credential entry inside the vault.
/// </summary>
public sealed class VaultEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The actual secret. Never log or display unless explicitly requested.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
