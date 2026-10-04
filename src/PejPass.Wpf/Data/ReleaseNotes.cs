using PejPass.Wpf.Records;

namespace PejPass.Wpf.Data;

/// <summary>
/// Legacy placeholder. Changelog is served from update.json and
/// cached under LocalAppData\PejPass\update-manifest.json.
/// Kept empty so callers never show fabricated feature lists.
/// </summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } = [];
}
