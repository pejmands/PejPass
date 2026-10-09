using PejPass.Domain.Settings;
using PejPass.Wpf.Records;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PejPass.Wpf.Services;

/// <summary>
/// Manual update discovery, package download, and portable self-apply.
/// Never runs automatically. Never replaces the running process in-place.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const long MaxUpdatePackageBytes = 512L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    public UpdateManifest? LastManifest { get; private set; }

    public UpdateService()
        : this(AppInfoService.UpdateManifestUrl)
    {
    }

    public UpdateService(string manifestUrl)
    {
        _manifestUrl = manifestUrl;

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{AppInfoService.Name}/{AppInfoService.Version}");
    }

    public static string CurrentVersion => AppInfoService.Version;

    public static string DefaultStatusMessage =>
        $"You are running v{CurrentVersion}.";

    public static string ManifestCachePath =>
        Path.Combine(
            Path.GetDirectoryName(AppSettings.SettingsFilePath)
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PejPass"),
            "update-manifest.json");

    public static string PendingWhatsNewPath =>
        Path.Combine(
            Path.GetDirectoryName(AppSettings.SettingsFilePath)
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PejPass"),
            "pending-whats-new.txt");

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await FetchManifestAsync(cancellationToken).ConfigureAwait(false);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return UpdateCheckResult.InvalidManifest(
                    "Missing or empty version field.");
            }

            if (!IsTrustedManifest(manifest))
                return UpdateCheckResult.InvalidManifest("Update signature verification failed.");

            LastManifest = manifest;
            TrySaveManifestCache(manifest);

            if (!IsNewerVersion(manifest.Version, CurrentVersion))
                return UpdateCheckResult.UpToDate(CurrentVersion);

            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                return UpdateCheckResult.InvalidManifest("Missing download URL.");

            if (!TryValidateHttpsUrl(manifest.DownloadUrl))
                return UpdateCheckResult.InvalidManifest("Download URL must use HTTPS.");

            if (!IsValidSha256(manifest.Sha256))
                return UpdateCheckResult.InvalidManifest("Missing or invalid SHA-256 package hash.");

            return UpdateCheckResult.Available(CurrentVersion, manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Cancelled,
                CurrentVersion = CurrentVersion,
                Message = "Update check cancelled."
            };
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.NetworkError("Request timed out.");
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResult.NetworkError("Could not connect to the update server.");
        }
        catch (JsonException)
        {
            return UpdateCheckResult.InvalidManifest("The update manifest is invalid.");
        }
        catch (Exception)
        {
            return UpdateCheckResult.NetworkError("Could not check for updates.");
        }
    }

    public IReadOnlyList<ReleaseNote> GetCachedReleaseNotes()
    {
        var manifest = LastManifest ?? TryLoadManifestCache();
        if (manifest is null || !IsTrustedManifest(manifest))
            return [];

        LastManifest = manifest;
        return ToReleaseNotes(manifest, CurrentVersion);
    }

    public async Task<IReadOnlyList<ReleaseNote>> LoadReleaseNotesAsync(
        CancellationToken cancellationToken = default)
    {
        var cachedNotes = GetCachedReleaseNotes();

        try
        {
            var remote = await FetchManifestAsync(cancellationToken).ConfigureAwait(false);
            if (remote is not null
                && !string.IsNullOrWhiteSpace(remote.Version)
                && IsTrustedManifest(remote))
            {
                LastManifest = remote;
                TrySaveManifestCache(remote);

                var notes = ToReleaseNotes(remote, CurrentVersion);
                if (notes.Count > 0)
                    return notes;
            }
        }
        catch
        {
        }

        return cachedNotes;
    }

    public static bool HasPendingWhatsNew()
    {
        try
        {
            if (!File.Exists(PendingWhatsNewPath))
                return false;

            var expected = File.ReadAllText(PendingWhatsNewPath).Trim();
            if (string.IsNullOrEmpty(expected))
                return false;

            return string.Equals(
                Normalize(expected),
                Normalize(CurrentVersion),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void ClearPendingWhatsNew()
    {
        try
        {
            if (File.Exists(PendingWhatsNewPath))
                File.Delete(PendingWhatsNewPath);
        }
        catch
        {
        }
    }

    private static void WritePendingWhatsNew(string version)
    {
        try
        {
            var path = PendingWhatsNewPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, Normalize(version));
        }
        catch
        {
        }
    }

    public static void ApplyPortableUpdateAndRestart(string zipPath, string targetVersion)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            throw new FileNotFoundException("Update package not found.", zipPath);

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve PejPass.exe path.");

        var appDir = Path.GetDirectoryName(processPath)
            ?? throw new InvalidOperationException("Cannot resolve application directory.");

        var stageRoot = Path.Combine(
            Path.GetTempPath(),
            "PejPass-update-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(stageRoot);
        ZipFile.ExtractToDirectory(zipPath, stageRoot);

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
        catch
        {
        }

        var sourceDir = ResolveExtractedPayloadRoot(stageRoot);
        WritePendingWhatsNew(targetVersion);

        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            "PejPass-apply-update-" + Guid.NewGuid().ToString("N") + ".cmd");

        var appDirEsc = EscapeCmd(appDir);
        var sourceEsc = EscapeCmd(sourceDir);
        var exeEsc = EscapeCmd(processPath);
        var stageEsc = EscapeCmd(stageRoot);

        var script =
            "@echo off" + Environment.NewLine +
            "setlocal EnableExtensions" + Environment.NewLine +
            "set \"APPDIR=" + appDirEsc + "\"" + Environment.NewLine +
            "set \"SOURCE=" + sourceEsc + "\"" + Environment.NewLine +
            "set \"EXE=" + exeEsc + "\"" + Environment.NewLine +
            "set \"STAGE=" + stageEsc + "\"" + Environment.NewLine +
            ":wait" + Environment.NewLine +
            "timeout /t 1 /nobreak >nul" + Environment.NewLine +
            "tasklist /FI \"IMAGENAME eq PejPass.exe\" 2>nul | find /I \"PejPass.exe\" >nul" + Environment.NewLine +
            "if not errorlevel 1 goto wait" + Environment.NewLine +
            "robocopy \"%SOURCE%\" \"%APPDIR%\" /E /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1" + Environment.NewLine +
            "if errorlevel 8 exit /b 1" + Environment.NewLine +
            "start \"\" \"%EXE%\"" + Environment.NewLine +
            "rmdir /S /Q \"%STAGE%\" 2>nul" + Environment.NewLine +
            "del \"%~f0\" 2>nul" + Environment.NewLine;

        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }

    private static string ResolveExtractedPayloadRoot(string stageRoot)
    {
        var entries = Directory.GetFileSystemEntries(stageRoot);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
            return entries[0];

        return stageRoot;
    }

    private static string EscapeCmd(string path) =>
        path.Replace("\"", string.Empty);

    private static UpdateManifest? TryLoadManifestCache()
    {
        try
        {
            var path = ManifestCachePath;
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveManifestCache(UpdateManifest manifest)
    {
        try
        {
            var path = ManifestCachePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
        }
    }

    private async Task<UpdateManifest?> FetchManifestAsync(CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(_manifestUrl, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        return await JsonSerializer
            .DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static IReadOnlyList<ReleaseNote> ToReleaseNotes(
        UpdateManifest manifest,
        string installedVersion)
    {
        var entries = new List<(string Version, string? Released, UpdateNotes? Notes)>();

        if (manifest.Releases is { Count: > 0 })
        {
            foreach (var r in manifest.Releases)
            {
                if (string.IsNullOrWhiteSpace(r.Version))
                    continue;

                entries.Add((r.Version.Trim(), r.Released, r.Notes));
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Version))
        {
            var rootVer = manifest.Version.Trim();
            var already = entries.Any(e =>
                string.Equals(
                    Normalize(e.Version),
                    Normalize(rootVer),
                    StringComparison.OrdinalIgnoreCase));

            if (!already)
                entries.Insert(0, (rootVer, manifest.Released, manifest.Notes));
            else if (manifest.Notes is not null)
            {
                var idx = entries.FindIndex(e =>
                    string.Equals(
                        Normalize(e.Version),
                        Normalize(rootVer),
                        StringComparison.OrdinalIgnoreCase));

                if (idx >= 0 && IsEmptyNotes(entries[idx].Notes))
                {
                    entries[idx] = (entries[idx].Version, entries[idx].Released ?? manifest.Released, manifest.Notes);
                }
            }
        }

        if (entries.Count == 0)
            return [];

        entries.Sort((a, b) => CompareVersionsDesc(a.Version, b.Version));

        var latestNorm = Normalize(entries[0].Version);
        var installedNorm = Normalize(installedVersion);

        var list = new List<ReleaseNote>(entries.Count);

        foreach (var (version, released, notes) in entries)
        {
            var note = MapNotes(version, released, notes, latestNorm, installedNorm);
            if (note is not null)
                list.Add(note);
        }

        return list;
    }

    private static ReleaseNote? MapNotes(
        string version,
        string? released,
        UpdateNotes? notes,
        string latestNorm,
        string installedNorm)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var added = notes?.Added ?? [];
        var improved = notes?.Improved ?? [];
        var fixedItems = notes?.Fixed ?? [];

        if (added.Count == 0 && improved.Count == 0 && fixedItems.Count == 0
            && !string.IsNullOrWhiteSpace(notes?.PlainText))
        {
            improved = [notes!.PlainText!];
        }

        var norm = Normalize(version);
        var display = version.StartsWith('v') || version.StartsWith('V')
            ? version
            : $"v{version}";

        var isLatest = string.Equals(norm, latestNorm, StringComparison.OrdinalIgnoreCase);
        var isInstalled = string.Equals(norm, installedNorm, StringComparison.OrdinalIgnoreCase);
        var isAvailableUpdate = IsNewerVersion(version, installedNorm);

        return new ReleaseNote
        {
            Version = display,
            Date = string.IsNullOrWhiteSpace(released) ? string.Empty : released,
            Added = added,
            Improved = improved,
            Fixed = fixedItems,
            IsLatest = isLatest,
            IsInstalled = isInstalled,
            IsAvailableUpdate = isAvailableUpdate
        };
    }

    private static bool IsEmptyNotes(UpdateNotes? notes)
    {
        if (notes is null)
            return true;

        return notes.Added.Count == 0
               && notes.Improved.Count == 0
               && notes.Fixed.Count == 0
               && string.IsNullOrWhiteSpace(notes.PlainText);
    }

    private static int CompareVersionsDesc(string a, string b)
    {
        if (Version.TryParse(Normalize(a), out var va)
            && Version.TryParse(Normalize(b), out var vb))
        {
            return vb.CompareTo(va);
        }

        return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string expectedSha256,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateHttpsUrl(downloadUrl))
            throw new ArgumentException("Download URL must use HTTPS.", nameof(downloadUrl));

        if (!IsValidSha256(expectedSha256))
            throw new ArgumentException("Expected SHA-256 hash is missing or invalid.", nameof(expectedSha256));

        using var response = await _http
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        if (total > MaxUpdatePackageBytes)
            throw new InvalidDataException("Update package exceeds the maximum supported size.");

        // Use a generated filename; never trust a server-provided filename for a filesystem path.
        var destPath = Path.Combine(
            Path.GetTempPath(),
            $"PejPass-dl-{Guid.NewGuid():N}.zip");
        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var output = new FileStream(
            destPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                if (readTotal > MaxUpdatePackageBytes - read)
                    throw new InvalidDataException("Update package exceeds the maximum supported size.");

                hasher.AppendData(buffer, 0, read);

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                readTotal += read;

                if (total > 0 && progress is not null)
                    progress.Report(readTotal / (double)total);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            var actualSha256 = Convert.ToHexString(hasher.GetHashAndReset());

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualSha256),
                    Convert.FromHexString(expectedSha256)))
            {
                TryDeleteFile(destPath);
                throw new InvalidDataException("Update package SHA-256 hash does not match the manifest.");
            }

            progress?.Report(1.0);
            return destPath;
        }
        catch
        {
            TryDeleteFile(destPath);
            throw;
        }
    }

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

    private static bool IsTrustedManifest(UpdateManifest manifest) =>
        UpdateSignatureService.VerifyManifestSignature(manifest);

    private static bool TryValidateHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsValidSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            return false;

        return value.All(Uri.IsHexDigit);
    }

    private static string ResolveFileName(string url, HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentDisposition?.FileName is { Length: > 0 } cd)
            return cd.Trim('"', '\'');

        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        return $"{AppInfoService.Name}-update.zip";
    }

    public static bool IsNewerVersion(string candidate, string current)
    {
        if (!Version.TryParse(Normalize(candidate), out var next))
            return false;

        if (!Version.TryParse(Normalize(current), out var cur))
            return true;

        return next > cur;
    }

    private static string Normalize(string version)
    {
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
            v = v[1..];
        return v;
    }



    public static bool OpenUrl(string? url)
    {
        if (!WebUrlValidator.TryNormalize(url, out var normalizedUrl))
            return false;

        Process.Start(new ProcessStartInfo
        {
            FileName = normalizedUrl,
            UseShellExecute = true
        });

        return true;
    }

    public static void OpenRepositoryPage() =>
       OpenUrl(AppInfoService.RepositoryUrl);

    public static void OpenReleasesPage() =>
        OpenUrl($"{AppInfoService.RepositoryUrl.TrimEnd('/')}/releases");

    public void Dispose() => _http.Dispose();
}
