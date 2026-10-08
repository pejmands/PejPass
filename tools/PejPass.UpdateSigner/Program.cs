using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run -- <update.json> <private-key.pem>");
    return 1;
}

var manifestPath = Path.GetFullPath(args[0]);
var privateKeyPath = Path.GetFullPath(args[1]);

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Manifest not found: {manifestPath}");
    return 1;
}

if (!File.Exists(privateKeyPath))
{
    Console.Error.WriteLine($"Private key not found: {privateKeyPath}");
    return 1;
}

try
{
    var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))
        as JsonObject
        ?? throw new InvalidDataException("Manifest root must be a JSON object.");

    var version = GetRequiredString(root, "version");
    var downloadUrl = GetRequiredString(root, "downloadUrl");
    var sha256 = GetRequiredString(root, "sha256");

    if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
        || uri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidDataException("downloadUrl must use HTTPS.");

    if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        throw new InvalidDataException("sha256 must be exactly 64 hexadecimal characters.");

    var released = GetOptionalString(root, "released");

    var payload = BuildSigningPayload(version, released, downloadUrl, sha256);

    using var key = ECDsa.Create();
    key.ImportFromPem(await File.ReadAllTextAsync(privateKeyPath));

    var signature = key.SignData(
        payload,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.Rfc3279DerSequence);

    root["signature"] = Convert.ToBase64String(signature);

    var json = root.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true
    });

    await File.WriteAllTextAsync(
        manifestPath,
        json + Environment.NewLine);

    Console.WriteLine("Manifest signed successfully.");
    Console.WriteLine($"Version: {version}");
    Console.WriteLine($"Signature: {Convert.ToBase64String(signature)}");

    return 0;
}
catch (CryptographicException ex)
{
    Console.Error.WriteLine($"Cryptographic error: {ex.Message}");
    return 1;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"Invalid JSON: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static string GetRequiredString(JsonObject root, string name)
{
    var value = GetOptionalString(root, name);

    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException($"Missing required manifest field: {name}.");

    return value;
}

static string? GetOptionalString(JsonObject root, string name)
{
    return root[name]?.GetValue<string>()?.Trim();
}

static byte[] BuildSigningPayload(
    string version,
    string? released,
    string downloadUrl,
    string sha256)
{
    using var stream = new MemoryStream();
    using var writer = new Utf8JsonWriter(stream);

    writer.WriteStartObject();
    writer.WriteString("schema", "PejPass.UpdateSignature.v1");
    writer.WriteString("version", version.Trim());
    writer.WriteString("released", released?.Trim());
    writer.WriteString("downloadUrl", downloadUrl.Trim());
    writer.WriteString("sha256", sha256.Trim().ToLowerInvariant());
    writer.WriteEndObject();
    writer.Flush();

    return stream.ToArray();
}
