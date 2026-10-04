using System.Text.Json.Serialization;

namespace PejPass.Wpf.Records;

/// <summary>
/// Shape of the remote update.json file.
/// Keep this contract stable; clients depend on it.
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("released")]
    public string? Released { get; init; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("notes")]
    public UpdateNotes? Notes { get; init; }
}

public sealed class UpdateNotes
{
    [JsonPropertyName("added")]
    public List<string> Added { get; init; } = [];

    [JsonPropertyName("improved")]
    public List<string> Improved { get; init; } = [];

    [JsonPropertyName("fixed")]
    public List<string> Fixed { get; init; } = [];
}
