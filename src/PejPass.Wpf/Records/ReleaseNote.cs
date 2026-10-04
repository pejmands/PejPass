namespace PejPass.Wpf.Records;

/// <summary>
/// One version card in the What's New window.
/// </summary>
public sealed record ReleaseNote
{
    public required string Version { get; init; }

    public required string Date { get; init; }

    public required IReadOnlyList<string> Added { get; init; }

    public required IReadOnlyList<string> Improved { get; init; }

    public required IReadOnlyList<string> Fixed { get; init; }

    /// <summary>
    /// Newest entry in the loaded changelog.
    /// </summary>
    public bool IsLatest { get; init; }

    /// <summary>
    /// Matches the version of the running executable.
    /// </summary>
    public bool IsInstalled { get; init; }

    /// <summary>
    /// True when this entry is newer than the installed build.
    /// </summary>
    public bool IsAvailableUpdate { get; init; }
}
