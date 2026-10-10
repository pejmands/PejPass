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

    /// <summary>
    /// Ordered, case-sensitive comparison of name, raw value and secret flag.
    /// </summary>
    public static bool AreSequencesEqual(
        IReadOnlyList<CustomField>? left,
        IReadOnlyList<CustomField>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null || left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];

            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !string.Equals(a.Value, b.Value, StringComparison.Ordinal) ||
                a.IsSecret != b.IsSecret)
            {
                return false;
            }
        }

        return true;
    }
}
