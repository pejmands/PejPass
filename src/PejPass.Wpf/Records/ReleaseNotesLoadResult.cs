namespace PejPass.Wpf.Records;

/// <summary>
/// Release notes returned by the update service and whether they came from the network.
/// </summary>
public sealed record ReleaseNotesLoadResult(
    IReadOnlyList<ReleaseNote> Notes,
    bool IsFromNetwork);
