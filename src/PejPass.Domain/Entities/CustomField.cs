namespace PejPass.Domain.Entities;

/// <summary>
/// A user-defined key/value field attached to a VaultEntry.
/// </summary>
public sealed class CustomField
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// When true, the value is hidden in the main window (like a password)
    /// until the user explicitly reveals it.
    /// </summary>
    public bool IsSecret { get; set; }
}
