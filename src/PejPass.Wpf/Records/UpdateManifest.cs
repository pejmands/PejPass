using System.Text.Json.Serialization;

namespace PejPass.Wpf.Records;

/// <summary>
/// Shape of the remote update.json file.
/// Compatible with the PejTools-style manifest (version + downloadUrl + notes).
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("released")]
    public string? Released { get; init; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Structured notes (preferred).
    /// </summary>
    [JsonPropertyName("notes")]
    public UpdateNotes? Notes { get; init; }

    /// <summary>
    /// Plain-text notes fallback (PejTools style: "notes": "Latest release").
    /// Used only when structured Notes is absent.
    /// </summary>
    [JsonPropertyName("notesText")]
    public string? NotesText { get; init; }
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
