using PejPass.Wpf.Data;
using PejPass.Wpf.Records;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace PejPass.Wpf.Services;

/// <summary>
/// Manual update discovery and package download.
/// Never runs automatically. Never replaces the running process.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    /// <summary>
    /// Last successfully fetched manifest (used by What's New).
    /// </summary>
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

    public string CurrentVersion => AppInfoService.Version;

    public string DefaultStatusMessage =>
        $"You are running v{CurrentVersion}.";

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

            LastManifest = manifest;

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
            return UpdateCheckResult.NetworkError(ex.Message);
        }
        catch (JsonException ex)
        {
            return UpdateCheckResult.InvalidManifest(ex.Message);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.NetworkError(ex.Message);
        }
    }

    /// <summary>
    /// Loads the changelog for What's New.
    /// Prefers remote update.json (releases[] or root notes);
    /// falls back to embedded offline notes.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseNote>> LoadReleaseNotesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = LastManifest
                ?? await FetchManifestAsync(cancellationToken).ConfigureAwait(false);

            if (manifest is not null)
            {
                LastManifest = manifest;
                var notes = ToReleaseNotes(manifest, CurrentVersion);
                if (notes.Count > 0)
                    return notes;
            }
        }
        catch
        {
            // Offline / error → embedded notes.
        }

        return ReleaseNotes.All;
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

    /// <summary>
    /// Builds ordered changelog cards from the manifest.
    /// Newest version first.
    /// </summary>
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

        // Always ensure the root version is present (PejTools single-entry manifests).
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
                // Prefer richer root notes if the matching release had empty notes.
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

        // Sort newest first by semantic version when possible.
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
        if (System.Version.TryParse(Normalize(a), out var va)
            && System.Version.TryParse(Normalize(b), out var vb))
        {
            return vb.CompareTo(va);
        }

        return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Downloads the update package to the user's Downloads folder.
    /// Does not install or replace the running app.
    /// </summary>
    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Download URL is empty.", nameof(downloadUrl));

        using var response = await _http
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var fileName = ResolveFileName(downloadUrl, response);
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(downloads);

        var destPath = Path.Combine(downloads, fileName);
        destPath = MakeUniquePath(destPath);

        var total = response.Content.Headers.ContentLength ?? -1L;
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

        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);

            readTotal += read;

            if (total > 0 && progress is not null)
                progress.Report(readTotal / (double)total);
        }

        progress?.Report(1.0);
        return destPath;
    }

    private static string ResolveFileName(string url, HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentDisposition?.FileName is { Length: > 0 } cd)
        {
            return cd.Trim('"', '\'');
        }

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

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    public static bool IsNewerVersion(string candidate, string current)
    {
        if (!System.Version.TryParse(Normalize(candidate), out var next))
            return false;

        if (!System.Version.TryParse(Normalize(current), out var cur))
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

    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return;
        }

        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
    }

    public void OpenRepositoryPage() =>
        OpenUrl(AppInfoService.RepositoryUrl);

    public void OpenReleasesPage() =>
        OpenUrl($"{AppInfoService.RepositoryUrl.TrimEnd('/')}/releases");

    public void Dispose() => _http.Dispose();
}
