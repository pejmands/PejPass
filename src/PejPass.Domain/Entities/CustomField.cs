namespace PejPass.Domain.Entities;

/// <summary>
/// A user-defined key/value field attached to a VaultEntry.
/// </summary>
public sealed class CustomField
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
