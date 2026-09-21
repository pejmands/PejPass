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

    /// <summary>
    /// Base32-encoded TOTP secret (empty if not configured).
    /// </summary>
    public string TotpSecret { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public List<CustomField> CustomFields { get; set; } = [];

    /// <summary>Previous passwords (newest first). Never written into Notes.</summary>
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = [];

    /// <summary>Previous usernames (newest first). Never written into Notes.</summary>
    public List<UsernameHistoryItem> UsernameHistory { get; set; } = [];

    /// <summary>Pinned to top of sorted lists (Bitwarden/1Password style).</summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Manual order rank (lower = higher). Used when sort mode is Manual.
    /// </summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public const int MaxPasswordHistory = 20;
    public const int MaxUsernameHistory = 20;

    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records the current password into history before replacing it.
    /// </summary>
    public void PushPasswordHistory(string previousPassword)
    {
        if (string.IsNullOrEmpty(previousPassword))
            return;

        if (PasswordHistory.Count > 0 &&
            string.Equals(PasswordHistory[0].Password, previousPassword, StringComparison.Ordinal))
            return;

        PasswordHistory.Insert(0, new PasswordHistoryItem
        {
            Password = previousPassword,
            ChangedAt = DateTimeOffset.UtcNow
        });

        if (PasswordHistory.Count > MaxPasswordHistory)
            PasswordHistory.RemoveRange(MaxPasswordHistory, PasswordHistory.Count - MaxPasswordHistory);
    }

    /// <summary>
    /// Records the current username into history before replacing it.
    /// </summary>
    public void PushUsernameHistory(string previousUsername)
    {
        if (string.IsNullOrEmpty(previousUsername))
            return;

        if (UsernameHistory.Count > 0 &&
            string.Equals(UsernameHistory[0].Username, previousUsername, StringComparison.Ordinal))
            return;

        UsernameHistory.Insert(0, new UsernameHistoryItem
        {
            Username = previousUsername,
            ChangedAt = DateTimeOffset.UtcNow
        });

        if (UsernameHistory.Count > MaxUsernameHistory)
            UsernameHistory.RemoveRange(MaxUsernameHistory, UsernameHistory.Count - MaxUsernameHistory);
    }
}
