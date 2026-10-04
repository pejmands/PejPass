using System.Text.Json;
using System.Text.Json.Serialization;

namespace PejPass.Wpf.Records;

/// <summary>
/// Remote update.json — same idea as PejTools:
/// { "version", "downloadUrl", "notes", optional "released" }.
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Optional publish date of the remote build (e.g. "October 5, 2026").
    /// </summary>
    [JsonPropertyName("released")]
    public string? Released { get; init; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Accepts either a plain string (PejTools) or a structured object for What's New.
    /// </summary>
    [JsonPropertyName("notes")]
    [JsonConverter(typeof(UpdateNotesJsonConverter))]
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

    /// <summary>
    /// When the remote file used a plain string notes field.
    /// </summary>
    public string? PlainText { get; init; }
}

/// <summary>
/// PejTools uses "notes": "Latest release".
/// PejPass What's New prefers structured notes.
/// </summary>
public sealed class UpdateNotesJsonConverter : JsonConverter<UpdateNotes?>
{
    public override UpdateNotes? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
            {
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return new UpdateNotes
                {
                    PlainText = text,
                    Improved = [text]
                };
            }

            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;

                return new UpdateNotes
                {
                    Added = ReadStringList(root, "added"),
                    Improved = ReadStringList(root, "improved"),
                    Fixed = ReadStringList(root, "fixed")
                };
            }

            default:
                throw new JsonException(
                    $"Unexpected token for notes: {reader.TokenType}");
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        UpdateNotes? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (!string.IsNullOrWhiteSpace(value.PlainText)
            && value.Added.Count == 0
            && value.Improved.Count <= 1
            && value.Fixed.Count == 0)
        {
            writer.WriteStringValue(value.PlainText);
            return;
        }

        writer.WriteStartObject();
        WriteStringArray(writer, "added", value.Added);
        WriteStringArray(writer, "improved", value.Improved);
        WriteStringArray(writer, "fixed", value.Fixed);
        writer.WriteEndObject();
    }

    private static List<string> ReadStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop)
            || prop.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s);
            }
        }

        return list;
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string name,
        List<string> items)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var item in items)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
