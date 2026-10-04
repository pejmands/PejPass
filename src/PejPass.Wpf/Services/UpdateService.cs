using PejPass.Wpf.Records;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace PejPass.Wpf.Services;

/// <summary>
/// Manual update discovery. Never runs automatically.
/// Fetches a small JSON manifest only when the user asks.
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

    public UpdateService()
        : this(AppInfoService.UpdateManifestUrl)
    {
    }

    public UpdateService(string manifestUrl)
    {
        _manifestUrl = manifestUrl;

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        // Identify ourselves politely; no telemetry.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{AppInfoService.Name}/{AppInfoService.Version}");
    }

    public string CurrentVersion => AppInfoService.Version;

    public string DefaultStatusMessage =>
        $"You are running v{CurrentVersion}.";

    /// <summary>
    /// Performs a single, user-initiated update check.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(_manifestUrl, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.NetworkError(
                    $"HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var manifest = await JsonSerializer
                .DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return UpdateCheckResult.InvalidManifest(
                    "Missing or empty version field.");
            }

            if (!IsNewerVersion(manifest.Version, CurrentVersion))
                return UpdateCheckResult.UpToDate(CurrentVersion);

            return UpdateCheckResult.Available(CurrentVersion, manifest);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Cancelled,
                CurrentVersion = CurrentVersion,
                Message = "Update check cancelled."
            };
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.NetworkError(ex.Message);
        }
        catch (TaskCanceledException)
        {
            return UpdateCheckResult.NetworkError("Request timed out.");
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
    /// Returns true when <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.
    /// Accepts simple dotted versions (e.g. 0.1.0, 1.2.3).
    /// </summary>
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

    public void OpenRepositoryPage() =>
        OpenUrl(AppInfoService.RepositoryUrl);

    public void OpenReleasesPage() =>
        OpenUrl($"{AppInfoService.RepositoryUrl.TrimEnd('/')}/releases");

    public void Dispose() => _http.Dispose();
}
