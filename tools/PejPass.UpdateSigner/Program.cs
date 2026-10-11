using PejPass.Application.Security;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

return Signer.Run(args);

internal sealed class SignerException(string message) : Exception(message);

internal static class Signer
{
    private const string Usage = """
        Usage:
          Sign:    dotnet run --project tools/PejPass.UpdateSigner -- <update.json> <private-key.pem> [--package <PejPass.zip>] [--public-key <base64>]
          Verify:  dotnet run --project tools/PejPass.UpdateSigner -- <update.json> --verify-only [--package <PejPass.zip>] [--public-key <base64>]

        --package      compute SHA-256 from the release zip and check its layout and executable version.
        --public-key   app-trusted key (Base64 SubjectPublicKeyInfo); defaults to the key embedded in the app source.
        --verify-only  verify an existing signed manifest without writing it.
        """;

    private const long MaxManifestBytes = 4L * 1024 * 1024;
    private const int MaxManifestTextCharacters = 2_000_000;
    private const int MaxReleases = 1_000;
    private const int MaxNoteItems = 100;
    private const int MaxNoteChars = 4_096;
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private const int MaxArchiveEntries = 4_096;
    private const long MaxEntryBytes = 256L * 1024 * 1024;
    private const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record Fields(string Version, string? Released, string DownloadUrl, string? Sha256);

    private sealed record Options(
        string ManifestPath,
        string? PrivateKeyPath,
        string? PackagePath,
        string? PublicKey,
        bool VerifyOnly)
    {
        public static Options Parse(string[] args)
        {
            var positional = new List<string>();
            string? package = null;
            string? publicKey = null;
            var verifyOnly = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--package":
                        if (package is not null)
                            throw new SignerException("--package may be specified only once.");
                        package = Next(args, ref i, "--package");
                        break;
                    case "--public-key":
                        if (publicKey is not null)
                            throw new SignerException("--public-key may be specified only once.");
                        publicKey = Next(args, ref i, "--public-key");
                        break;
                    case "--verify-only":
                        if (verifyOnly)
                            throw new SignerException("--verify-only may be specified only once.");
                        verifyOnly = true;
                        break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal))
                            throw new SignerException($"Unknown option '{args[i]}'.\n\n{Usage}");
                        positional.Add(args[i]);
                        break;
                }
            }

            if (positional.Count != (verifyOnly ? 1 : 2))
                throw new SignerException(Usage);

            return new Options(
                positional[0],
                verifyOnly ? null : positional[1],
                package,
                publicKey,
                verifyOnly);
        }

        private static string Next(string[] args, ref int i, string option) =>
            i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : throw new SignerException($"{option} needs a value.");
    }

    public static int Run(string[] args)
    {
        try
        {
            return Execute(Options.Parse(args));
        }
        catch (SignerException ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or CryptographicException or FormatException or InvalidDataException
                                       or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Execute(Options options)
    {
        var manifestPath = Path.GetFullPath(options.ManifestPath);
        if (!File.Exists(manifestPath))
            throw new SignerException($"Manifest not found: {manifestPath}");

        var manifestInfo = new FileInfo(manifestPath);
        if (manifestInfo.Length <= 0 || manifestInfo.Length > MaxManifestBytes)
            throw new SignerException("The manifest is empty or larger than the 4 MiB the app accepts.");

        var manifestText = File.ReadAllText(manifestPath);
        if (manifestText.Length > MaxManifestTextCharacters)
            throw new SignerException("The manifest text is larger than the app accepts.");

        var root = JsonNode.Parse(manifestText) as JsonObject
            ?? throw new SignerException("Manifest root must be a JSON object.");

        var warnings = new List<string>();
        var fields = ReadFields(root);
        CheckManifest(root, fields, warnings);

        if (options.PackagePath is not null)
        {
            var packageSha = InspectPackage(options.PackagePath, fields.Version, warnings);
            if (!string.Equals(fields.Sha256, packageSha, StringComparison.OrdinalIgnoreCase))
            {
                if (options.VerifyOnly)
                {
                    throw new SignerException(
                        $"sha256 in the manifest ({fields.Sha256 ?? "missing"}) does not match the package ({packageSha}).");
                }

                Console.WriteLine($"sha256 updated: {fields.Sha256 ?? "(missing)"} -> {packageSha}");
            }

            root["sha256"] = packageSha;
            fields = ReadFields(root);

            var urlFile = Path.GetFileName(new Uri(fields.DownloadUrl).AbsolutePath);
            if (!string.Equals(urlFile, Path.GetFileName(options.PackagePath), StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"downloadUrl ends with '{urlFile}' but the package file is '{Path.GetFileName(options.PackagePath)}'.");
            }
        }

        if (fields.Sha256 is null)
            throw new SignerException("The manifest has no sha256. Pass --package <zip> to compute it.");
        if (!IsSha256(fields.Sha256))
            throw new SignerException("sha256 must be exactly 64 hexadecimal characters.");

        using var trusted = LoadTrustedKey(options.PublicKey, out var trustedSource);
        Console.WriteLine($"App trusts key {Fingerprint(trusted)} ({trustedSource}).");

        foreach (var warning in warnings)
            Console.Error.WriteLine("WARNING: " + warning);

        if (options.VerifyOnly)
        {
            VerifyManifestText(manifestText, trusted, "Manifest");
            Console.WriteLine($"OK: the signature of version {fields.Version} verifies with the app's key.");
            return 0;
        }

        var privateKeyPath = Path.GetFullPath(options.PrivateKeyPath!);
        if (!File.Exists(privateKeyPath))
            throw new SignerException($"Private key not found: {privateKeyPath}");

        using var signingKey = ECDsa.Create();
        signingKey.ImportFromPem(File.ReadAllText(privateKeyPath));

        if (!SameKey(signingKey, trusted))
        {
            throw new SignerException(
                "The private key does NOT match the public key trusted by the app.\n" +
                $"  private key  -> {Fingerprint(signingKey)}\n" +
                $"  app trusts   -> {Fingerprint(trusted)}\n" +
                "Every user would reject this update. Nothing was written.");
        }

        var payload = UpdateSigningPayload.Build(
            fields.Version,
            fields.Released,
            fields.DownloadUrl,
            fields.Sha256);

        var signature = Convert.ToBase64String(signingKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));

        root["signature"] = signature;
        var json = root.ToJsonString(JsonOptions) + Environment.NewLine;

        if (Encoding.UTF8.GetByteCount(json) > MaxManifestBytes
            || json.Length > MaxManifestTextCharacters)
        {
            throw new SignerException("The signed manifest would exceed the app's size limits.");
        }

        // Write beside the target, verify the actual bytes, then replace the manifest atomically.
        var tempPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            VerifyManifestText(File.ReadAllText(tempPath), trusted, "Written manifest");
            File.Move(tempPath, manifestPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }

        Console.WriteLine("Manifest signed and verified.");
        Console.WriteLine($"  version   {fields.Version}");
        Console.WriteLine($"  released  {fields.Released ?? "(none)"}");
        Console.WriteLine($"  url       {fields.DownloadUrl}");
        Console.WriteLine($"  sha256    {fields.Sha256.ToLowerInvariant()}");
        Console.WriteLine("Publish the zip first, then update.json.");
        return 0;
    }

    // Manifest validation

    private static Fields ReadFields(JsonObject root)
    {
        var version = RequiredString(root, "version");
        if (!Version.TryParse(Normalize(version), out _))
            throw new SignerException($"version '{version}' is not a valid version number (for example 1.2.0).");

        var url = RequiredString(root, "downloadUrl");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new SignerException("downloadUrl must be an absolute HTTPS URL without credentials.");
        }

        return new Fields(version, OptionalString(root, "released"), url, OptionalString(root, "sha256"));
    }

    private static void CheckManifest(JsonObject root, Fields fields, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(fields.Released))
            warnings.Add("'released' is empty, so the update dialog shows no date.");

        if (!fields.DownloadUrl.Contains(Normalize(fields.Version), StringComparison.OrdinalIgnoreCase))
            warnings.Add($"downloadUrl does not contain the version '{Normalize(fields.Version)}'. Is it a stale URL?");

        if (root.TryGetPropertyValue("releases", out var releasesNode) && releasesNode is not null)
        {
            if (releasesNode is not JsonArray releases)
                throw new SignerException("'releases' must be an array.");
            if (releases.Count > MaxReleases)
                throw new SignerException($"'releases' has more than {MaxReleases} entries.");

            var containsCurrent = false;
            foreach (var item in releases)
            {
                if (item is not JsonObject release)
                    throw new SignerException("Every entry in 'releases' must be an object.");

                var releaseVersion = OptionalString(release, "version");
                if (string.IsNullOrWhiteSpace(releaseVersion))
                {
                    warnings.Add("A releases entry has no version and will be ignored by the app.");
                    continue;
                }

                if (!Version.TryParse(Normalize(releaseVersion), out var parsed))
                    throw new SignerException($"releases: '{releaseVersion}' is not a valid version number.");

                if (Version.TryParse(Normalize(fields.Version), out var current) && Same(parsed, current))
                    containsCurrent = true;

                CheckNotes(release, $"releases[{releaseVersion}]", warnings);
            }

            if (!containsCurrent)
                warnings.Add($"releases has no entry for {fields.Version}; What's New will use the root notes.");
        }

        CheckNotes(root, "root", warnings);
    }

    private static void CheckNotes(JsonObject owner, string where, List<string> warnings)
    {
        if (!owner.TryGetPropertyValue("notes", out var notes) || notes is null)
            return;

        if (notes is JsonValue value)
        {
            if (!value.TryGetValue<string>(out var text))
                throw new SignerException($"{where}: notes must be a string or an object.");
            if (text.Length > MaxNoteChars)
                throw new SignerException($"{where}: notes is longer than {MaxNoteChars} characters.");
            return;
        }

        if (notes is not JsonObject categories)
            throw new SignerException($"{where}: notes must be a string or an object.");

        foreach (var category in new[] { "added", "improved", "fixed" })
        {
            if (!categories.TryGetPropertyValue(category, out var listNode) || listNode is null)
                continue;
            if (listNode is not JsonArray items)
                throw new SignerException($"{where}: notes.{category} must be an array of strings.");
            if (items.Count > MaxNoteItems)
                throw new SignerException($"{where}: notes.{category} has more than {MaxNoteItems} items.");

            foreach (var item in items)
            {
                if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var text))
                {
                    warnings.Add($"{where}: a non-string item in notes.{category} will be ignored by the app.");
                    continue;
                }

                if (text.Length > MaxNoteChars)
                    throw new SignerException($"{where}: an item in notes.{category} is longer than {MaxNoteChars} characters.");
            }
        }
    }

    private static string RequiredString(JsonObject root, string name) =>
        OptionalString(root, name) is { Length: > 0 } value
            ? value
            : throw new SignerException($"Missing required manifest field: {name}.");

    private static string? OptionalString(JsonObject root, string name)
    {
        if (!root.TryGetPropertyValue(name, out var node) || node is null)
            return null;

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return text.Trim();

        throw new SignerException($"Manifest field '{name}' must be a string.");
    }

    private static void VerifyManifestText(string text, ECDsa trusted, string label)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxManifestBytes || text.Length > MaxManifestTextCharacters)
            throw new SignerException($"{label}: manifest exceeds the app's size limits.");

        var root = JsonNode.Parse(text) as JsonObject
            ?? throw new SignerException($"{label}: root must be a JSON object.");
        var fields = ReadFields(root);

        if (fields.Sha256 is null || !IsSha256(fields.Sha256))
            throw new SignerException($"{label}: sha256 is missing or invalid.");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(RequiredString(root, "signature"));
        }
        catch (FormatException)
        {
            throw new SignerException($"{label}: the signature is not valid Base64.");
        }

        var payload = UpdateSigningPayload.Build(
            fields.Version,
            fields.Released,
            fields.DownloadUrl,
            fields.Sha256);

        if (!trusted.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new SignerException($"{label}: the signature does not verify with the app's trusted key.");
    }

    // Package inspection

    private static string InspectPackage(string packagePath, string manifestVersion, List<string> warnings)
    {
        var path = Path.GetFullPath(packagePath);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new SignerException($"Package not found: {path}");
        if (info.Length == 0 || info.Length > MaxPackageBytes)
            throw new SignerException("The package is empty or larger than the 512 MiB the app accepts.");

        string sha256;
        using (var stream = File.OpenRead(path))
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaxArchiveEntries)
            throw new SignerException($"The package must contain between 1 and {MaxArchiveEntries} entries.");

        long total = 0;
        var names = new List<string>(archive.Entries.Count);
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxEntryBytes)
                throw new SignerException($"'{entry.FullName}' is larger than the updater's per-file limit.");
            if (entry.Length > MaxUncompressedBytes - total)
                throw new SignerException("The package exceeds the updater's uncompressed size limit.");
            total += entry.Length;

            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith('/', StringComparison.Ordinal)
                || Regex.IsMatch(normalized, @"^[A-Za-z]:")
                || normalized.Split('/').Any(segment => segment is "." or ".."))
            {
                throw new SignerException($"The package contains an unsafe archive path: '{entry.FullName}'.");
            }

            var canonicalName = normalized.TrimEnd('/');
            if (canonicalName.Length == 0 || !uniqueNames.Add(canonicalName))
                throw new SignerException($"The package contains an empty or duplicate archive path: '{entry.FullName}'.");

            names.Add(canonicalName);
        }

        // The updater accepts PejPass.exe at the root, or all extracted entries inside one top-level folder.
        string exeName;
        var topLevel = names
            .Select(name => name.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Any(name => name.Equals("PejPass.exe", StringComparison.OrdinalIgnoreCase)))
        {
            exeName = names.First(name => name.Equals("PejPass.exe", StringComparison.OrdinalIgnoreCase));
        }
        else if (topLevel.Count == 1
                 && names.Any(name => name.Equals(topLevel[0] + "/PejPass.exe", StringComparison.OrdinalIgnoreCase)))
        {
            exeName = topLevel[0] + "/PejPass.exe";
        }
        else
        {
            throw new SignerException(
                "The updater would reject this package: PejPass.exe must be at the root of the zip, " +
                "or the zip must contain a single top-level folder that holds it.");
        }

        var exeEntry = archive.Entries.First(entry =>
            entry.FullName.Replace('\\', '/').TrimEnd('/').Equals(exeName, StringComparison.OrdinalIgnoreCase));

        var tempExe = Path.Combine(Path.GetTempPath(), $"PejPass-signer-{Guid.NewGuid():N}.exe");
        try
        {
            exeEntry.ExtractToFile(tempExe);
            var versionInfo = FileVersionInfo.GetVersionInfo(tempExe);
            var raw = versionInfo.ProductVersion ?? versionInfo.FileVersion;

            if (string.IsNullOrWhiteSpace(raw))
            {
                warnings.Add("Could not read the version from PejPass.exe, so it was not compared with the manifest.");
            }
            else
            {
                var exeVersion = Canonical(raw);
                var wanted = Canonical(manifestVersion);
                if (!Same(exeVersion, wanted))
                {
                    throw new SignerException(
                        $"The manifest says {manifestVersion}, but PejPass.exe inside the package is {raw}.\n" +
                        "The installed app would still see the update as newer and offer it again and again.");
                }
            }
        }
        finally
        {
            TryDeleteFile(tempExe);
        }

        return sha256;
    }

    // Key verification

    private static ECDsa LoadTrustedKey(string? overrideKey, out string source)
    {
        string? base64;
        if (overrideKey is not null)
        {
            base64 = overrideKey;
            source = "--public-key";
        }
        else
        {
            base64 = FindEmbeddedPublicKey(out var file);
            source = file ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new SignerException(
                "Could not find the app's embedded public key (TrustedPublicKeyBase64). " +
                "Run the tool from inside the repository, or pass --public-key <base64>.");
        }

        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);

            if (read != bytes.Length)
            {
                key.Dispose();
                throw new SignerException("The public key has trailing data.");
            }

            return key;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new SignerException("The public key is not a valid Base64 SubjectPublicKeyInfo.");
        }
    }

    private static string? FindEmbeddedPublicKey(out string? file)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(
                    dir.FullName, "src", "PejPass.Wpf", "Services", "UpdateSignatureService.cs");
                if (!File.Exists(candidate))
                    continue;

                var match = Regex.Match(
                    File.ReadAllText(candidate),
                    "TrustedPublicKeyBase64\\s*=\\s*\\\"([A-Za-z0-9+/=]+)\\\"");
                if (match.Success)
                {
                    file = Path.GetRelativePath(Directory.GetCurrentDirectory(), candidate);
                    return match.Groups[1].Value;
                }
            }
        }

        file = null;
        return null;
    }

    private static bool SameKey(ECDsa a, ECDsa b)
    {
        var pa = a.ExportParameters(false);
        var pb = b.ExportParameters(false);

        return pa.Curve.Oid?.Value == pb.Curve.Oid?.Value
            && pa.Q.X is { } ax && pb.Q.X is { } bx && ax.AsSpan().SequenceEqual(bx)
            && pa.Q.Y is { } ay && pb.Q.Y is { } by && ay.AsSpan().SequenceEqual(by);
    }

    private static string Fingerprint(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];

    // Helpers

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Normalize(string version)
    {
        var text = version.Trim();
        return text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;
    }

    /// <summary>1.1, 1.1.0 and 1.1.0.0 are the same version; a +commit suffix is ignored.</summary>
    private static Version Canonical(string raw)
    {
        var text = Normalize(raw);
        var plus = text.IndexOf('+');
        if (plus > 0)
            text = text[..plus];

        if (!Version.TryParse(text, out var parsed))
            throw new SignerException($"'{raw}' is not a valid version number.");

        return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
    }

    private static bool Same(Version a, Version b) =>
        Canonical(a.ToString()).Equals(Canonical(b.ToString()));

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
