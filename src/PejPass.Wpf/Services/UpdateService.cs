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
    private const int MaxUpdateManifestBytes = 4 * 1024 * 1024;
    private const int MaxManifestReleases = 1_000;
    private const int MaxReleaseNoteItemsPerCategory = 100;
    private const int MaxReleaseNoteItemCharacters = 4_096;
    private const int MaxManifestTextCharacters = 2_000_000;
    private const int MaxUpdateArchiveEntries = 4096;
    private const long MaxUpdateUncompressedBytes = 1024L * 1024 * 1024;
    private const long MaxUpdateEntryBytes = 256L * 1024 * 1024;

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
                return UpdateCheckResult.InvalidManifest("Missing or empty version field.");

            if (!IsTrustedManifest(manifest))
                return UpdateCheckResult.InvalidManifest("Update signature verification failed.");

            if (!Version.TryParse(Normalize(manifest.Version), out _))
                return UpdateCheckResult.InvalidManifest("The version field is not a valid version number.");

            if (!TryValidateHttpsUrl(manifest.DownloadUrl))
                return UpdateCheckResult.InvalidManifest("The download URL is missing or is not a valid HTTPS URL.");

            if (!IsValidSha256(manifest.Sha256))
                return UpdateCheckResult.InvalidManifest("The package SHA-256 hash is missing or invalid.");

            LastManifest = manifest;
            TrySaveManifestCache(manifest);

            if (!IsNewerVersion(manifest.Version, CurrentVersion))
                return UpdateCheckResult.UpToDate(CurrentVersion);

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
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.NetworkError($"The update server request failed: {ex.Message}");
        }
        catch (JsonException)
        {
            return UpdateCheckResult.InvalidManifest("The update manifest is not valid JSON.");
        }
        catch (InvalidDataException ex)
        {
            return UpdateCheckResult.InvalidManifest(ex.Message);
        }
        catch (Exception)
        {
            return UpdateCheckResult.NetworkError("Could not check for updates. Please try again later.");
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

    public static void ApplyPortableUpdateAndRestart(
        string zipPath,
        string targetVersion,
        string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("Update package path is required.", nameof(zipPath));

        if (!File.Exists(zipPath))
            throw new FileNotFoundException("The downloaded update package could not be found.", zipPath);

        if (!Version.TryParse(Normalize(targetVersion ?? string.Empty), out _))
            throw new ArgumentException("The target update version is invalid.", nameof(targetVersion));

        if (!IsValidSha256(expectedSha256))
            throw new ArgumentException("The expected update package SHA-256 hash is invalid.", nameof(expectedSha256));

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running PejPass executable path.");

        if (!Path.GetFileName(processPath).Equals("PejPass.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The running process is not PejPass.exe. Automatic installation was stopped.");

        var appDir = Path.GetDirectoryName(processPath)
            ?? throw new InvalidOperationException("Could not determine the PejPass installation directory.");

        if (!Directory.Exists(appDir))
            throw new DirectoryNotFoundException("The PejPass installation directory no longer exists.");

        EnsureDirectoryWritable(appDir);

        var stageRoot = Path.Combine(
            Path.GetTempPath(),
            "PejPass-update-" + Guid.NewGuid().ToString("N"));

        using (var packageStream = File.OpenRead(zipPath))
        using (var hasher = SHA256.Create())
        {
            var actualHash = Convert.ToHexString(hasher.ComputeHash(packageStream));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(expectedSha256)))
            {
                throw new InvalidDataException("The update package changed after download or its SHA-256 hash does not match.");
            }
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            ValidateUpdateArchive(archive);
            if (!archive.Entries.Any(entry =>
                    string.Equals(
                        entry.FullName.Replace('\\', '/').TrimStart('/'),
                        "PejPass.exe",
                        StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Replace('\\', '/').TrimStart('/').EndsWith(
                        "/PejPass.exe",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("The update package does not contain PejPass.exe.");
            }
        }

        Directory.CreateDirectory(stageRoot);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stageRoot);
        }
        catch
        {
            try
            {
                Directory.Delete(stageRoot, recursive: true);
            }
            catch
            {
            }

            throw;
        }

        var sourceDir = ResolveExtractedPayloadRoot(stageRoot);
        if (!File.Exists(Path.Combine(sourceDir, "PejPass.exe")))
        {
            try
            {
                Directory.Delete(stageRoot, recursive: true);
            }
            catch
            {
            }

            throw new InvalidDataException(
                "The update package does not place PejPass.exe at the root of its application payload.");
        }

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
        catch
        {
        }

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
            "set \"BACKUP=" + EscapeCmd(Path.Combine(Path.GetTempPath(), "PejPass-backup-" + Guid.NewGuid().ToString("N"))) + "\"" + Environment.NewLine +
            "set \"APP_PID=" + Environment.ProcessId + "\"" + Environment.NewLine +
            "set /a WAITCOUNT=0" + Environment.NewLine +
            ":wait" + Environment.NewLine +
            "tasklist /FI \"PID eq %APP_PID%\" 2>nul | find \"%APP_PID%\" >nul" + Environment.NewLine +
            "if errorlevel 1 goto apply_update" + Environment.NewLine +
            "set /a WAITCOUNT+=1" + Environment.NewLine +
            "if %WAITCOUNT% GEQ 120 goto install_failed" + Environment.NewLine +
            "timeout /t 1 /nobreak >nul" + Environment.NewLine +
            "goto wait" + Environment.NewLine +
            ":apply_update" + Environment.NewLine +
            "mkdir \"%BACKUP%\" 2>nul" + Environment.NewLine +
            "robocopy \"%APPDIR%\" \"%BACKUP%\" /E /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1" + Environment.NewLine +
            "if errorlevel 8 goto install_failed" + Environment.NewLine +
            "robocopy \"%SOURCE%\" \"%APPDIR%\" /E /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1" + Environment.NewLine +
            "if errorlevel 8 goto install_failed" + Environment.NewLine +
            "start \"\" \"%EXE%\"" + Environment.NewLine +
            "rmdir /S /Q \"%STAGE%\" 2>nul" + Environment.NewLine +
            "rmdir /S /Q \"%BACKUP%\" 2>nul" + Environment.NewLine +
            "del \"%~f0\" 2>nul" + Environment.NewLine +
            "exit /b 0" + Environment.NewLine +
            ":install_failed" + Environment.NewLine +
            "if exist \"%BACKUP%\" robocopy \"%BACKUP%\" \"%APPDIR%\" /E /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1 >nul" + Environment.NewLine +
            "start \"\" \"%EXE%\" 2>nul" + Environment.NewLine +
            "echo PejPass update installation failed. Previous application files were restored when possible. > \"%TEMP%\\PejPass-update-error.txt\"" + Environment.NewLine +
            "rmdir /S /Q \"%STAGE%\" 2>nul" + Environment.NewLine +
            "rmdir /S /Q \"%BACKUP%\" 2>nul" + Environment.NewLine +
            "del \"%~f0\" 2>nul" + Environment.NewLine +
            "exit /b 1" + Environment.NewLine;

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

    internal static void ValidateUpdateArchive(
        ZipArchive archive,
        int maxEntries = MaxUpdateArchiveEntries,
        long maxTotalUncompressedBytes = MaxUpdateUncompressedBytes,
        long maxEntryUncompressedBytes = MaxUpdateEntryBytes)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (maxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxTotalUncompressedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTotalUncompressedBytes));
        if (maxEntryUncompressedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEntryUncompressedBytes));

        if (archive.Entries.Count == 0)
            throw new InvalidDataException("Update archive is empty.");

        if (archive.Entries.Count > maxEntries)
            throw new InvalidDataException("Update archive contains too many entries.");

        long totalUncompressedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Length > maxEntryUncompressedBytes)
                throw new InvalidDataException("Update archive contains an oversized entry.");

            if (entry.Length > maxTotalUncompressedBytes - totalUncompressedBytes)
                throw new InvalidDataException("Update archive exceeds the maximum uncompressed size.");

            totalUncompressedBytes += entry.Length;
        }
    }

    private static void EnsureDirectoryWritable(string directory)
    {
        var probePath = Path.Combine(
            directory,
            ".pejpass-write-check-" + Guid.NewGuid().ToString("N"));

        try
        {
            using (new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.DeleteOnClose))
            {
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "PejPass cannot write to its installation folder. Move the portable app to a folder you can modify, then try again.",
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
            catch
            {
            }
        }
    }

    private static string ResolveExtractedPayloadRoot(string stageRoot)
    {
        var entries = Directory.GetFileSystemEntries(stageRoot);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
            return entries[0];

        return stageRoot;
    }

    internal static string EscapeCmd(string path) =>
        path.Replace("\"", string.Empty).Replace("%", "%%");

    private static UpdateManifest? TryLoadManifestCache()
    {
        try
        {
            var path = ManifestCachePath;
            if (!File.Exists(path))
                return null;

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaxUpdateManifestBytes)
                return null;

            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            if (manifest is null)
                return null;

            ValidateUpdateManifest(manifest);
            return manifest;
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

            ValidateUpdateManifest(manifest);
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxUpdateManifestBytes)
                return;

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
            .GetAsync(_manifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"The update server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is <= 0 or > MaxUpdateManifestBytes)
            throw new InvalidDataException("Update manifest exceeds the maximum supported size.");

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            if (buffer.Length > MaxUpdateManifestBytes - read)
                throw new InvalidDataException("Update manifest exceeds the maximum supported size.");

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(
            buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)),
            JsonOptions);

        if (manifest is not null)
            ValidateUpdateManifest(manifest);

        return manifest;
    }

    internal static void ValidateUpdateManifest(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("Update manifest is missing its version.");

        if (!Version.TryParse(Normalize(manifest.Version), out _))
            throw new InvalidDataException("Update manifest contains an invalid version number.");

        if (manifest.Releases is null || manifest.Releases.Count > MaxManifestReleases)
            throw new InvalidDataException("Update manifest contains too many releases.");

        var totalTextCharacters = 0;

        void ValidateText(string? value, string fieldName, int maxCharacters)
        {
            if (value is null)
                return;

            if (value.Length > maxCharacters)
                throw new InvalidDataException($"Update manifest {fieldName} exceeds its maximum length.");

            totalTextCharacters = checked(totalTextCharacters + value.Length);
            if (totalTextCharacters > MaxManifestTextCharacters)
                throw new InvalidDataException("Update manifest contains too much text.");
        }

        void ValidateNotes(UpdateNotes? notes, string fieldName)
        {
            if (notes is null)
                return;

            ValidateText(notes.PlainText, $"{fieldName} plain text", MaxReleaseNoteItemCharacters);

            ValidateList(notes.Added, $"{fieldName} added notes");
            ValidateList(notes.Improved, $"{fieldName} improved notes");
            ValidateList(notes.Fixed, $"{fieldName} fixed notes");
        }

        void ValidateList(List<string>? values, string fieldName)
        {
            if (values is null || values.Count > MaxReleaseNoteItemsPerCategory)
                throw new InvalidDataException($"Update manifest {fieldName} contains too many items.");

            foreach (var value in values)
            {
                if (value is null)
                    throw new InvalidDataException($"Update manifest {fieldName} contains a null item.");

                ValidateText(value, fieldName, MaxReleaseNoteItemCharacters);
            }
        }

        ValidateText(manifest.Version, "version", 128);
        ValidateText(manifest.Released, "release date", 128);
        ValidateText(manifest.DownloadUrl, "download URL", 2_048);
        ValidateText(manifest.Sha256, "SHA-256 hash", 64);
        ValidateText(manifest.Signature, "signature", 256);
        ValidateNotes(manifest.Notes, "root notes");

        foreach (var release in manifest.Releases)
        {
            if (release is null)
                throw new InvalidDataException("Update manifest contains an invalid release.");

            ValidateText(release.Version, "release version", 128);
            if (!string.IsNullOrWhiteSpace(release.Version)
                && !Version.TryParse(Normalize(release.Version), out _))
            {
                throw new InvalidDataException("Update manifest contains an invalid release version number.");
            }

            ValidateText(release.Released, "release date", 128);
            ValidateNotes(release.Notes, $"release {release.Version} notes");
        }
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
        if (total == 0)
            throw new InvalidDataException("The update package is empty.");
        if (total > MaxUpdatePackageBytes)
            throw new InvalidDataException("Update package exceeds the maximum supported size.");

        // Use a generated filename; never trust a server-provided filename for a filesystem path.
        var destPath = Path.Combine(
            Path.GetTempPath(),
            $"PejPass-dl-{Guid.NewGuid():N}.zip");
        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            // Dispose the file before cleanup so Windows can delete partial downloads reliably.
            await using (var output = new FileStream(
                destPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
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
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (readTotal == 0)
                throw new InvalidDataException("The update package is empty.");

            var actualSha256 = Convert.ToHexString(hasher.GetHashAndReset());

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualSha256),
                    Convert.FromHexString(expectedSha256)))
            {
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

    internal static bool TryValidateHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }

    internal static bool IsValidSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            return false;

        return value.All(Uri.IsHexDigit);
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