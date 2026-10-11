using System.Text.Json;

namespace PejPass.Application.Security;

/// <summary>
/// The exact bytes signed by UpdateSigner and verified by the app.
/// This source is compiled into both projects so the implementations cannot drift.
/// Changing the output invalidates every existing signature.
/// </summary>
public static class UpdateSigningPayload
{
    public const string Schema = "PejPass.UpdateSignature.v1";

    public static byte[] Build(string version, string? released, string? downloadUrl, string? sha256)
    {
        ArgumentNullException.ThrowIfNull(version);

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("version", version.Trim());
            writer.WriteString("released", released?.Trim());
            writer.WriteString("downloadUrl", downloadUrl?.Trim());
            writer.WriteString("sha256", sha256?.Trim().ToLowerInvariant());
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
