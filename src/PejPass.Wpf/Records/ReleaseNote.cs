namespace PejPass.Wpf.Records;

public sealed record ReleaseNote
{
    public required string Version { get; init; }

    public required string Date { get; init; }

    public required IReadOnlyList<string> Added { get; init; }

    public required IReadOnlyList<string> Improved { get; init; }

    public required IReadOnlyList<string> Fixed { get; init; }
}
