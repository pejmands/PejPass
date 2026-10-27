using Microsoft.Extensions.DependencyInjection;
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
    private const int MaxAdditionalSignatures = 4;
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
    private readonly bool _ownsHttpClient;
    private readonly string _manifestUrl;

    public UpdateManifest? LastManifest { get; private set; }

    public UpdateService()
        : this(AppInfoService.UpdateManifestUrl)
    {
    }

    public UpdateService(string manifestUrl)
    {
        _manifestUrl = manifestUrl ?? throw new ArgumentNullException(nameof(manifestUrl));
        _http = CreateDefaultHttpClient();
        _ownsHttpClient = true;
    }

    internal UpdateService(string manifestUrl, HttpClient httpClient)
    {
        _manifestUrl = manifestUrl ?? throw new ArgumentNullException(nameof(manifestUrl));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{AppInfoService.Name}/{AppInfoService.Version}");

        return http;
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

    public async Task<ReleaseNotesLoadResult> LoadReleaseNotesAsync(
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
                    return new ReleaseNotesLoadResult(notes, IsFromNetwork: true);
            }
        }
        catch
        {
        }

        return new ReleaseNotesLoadResult(cachedNotes, IsFromNetwork: false);
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

            return SameVersion(expected, CurrentVersion);
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

    /// <summary>A fully extracted update plus the script that applies it. Not yet launched.</summary>
    internal sealed record StagedUpdate(string ScriptPath, string StageRoot, string TargetVersion);

    /// <summary>
    /// Verifies, extracts and stages the package on a worker thread, then launches the apply
    /// script and shuts the application down on the calling (UI) thread.
    /// </summary>
    public static async Task ApplyPortableUpdateAndRestartAsync(
        string zipPath,
        string targetVersion,
        string expectedSha256)
    {
        // Hashing and extraction are heavy; keep them off the UI thread.
        var staged = await Task
            .Run(() => StageUpdate(zipPath, targetVersion, expectedSha256))
            .ConfigureAwait(true);

        LaunchStagedUpdate(staged);
    }

    internal static StagedUpdate StageUpdate(
        string zipPath,
        string targetVersion,
        string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("Update package path is required.", nameof(zipPath));

        if (!File.Exists(zipPath))
            throw new FileNotFoundException("The downloaded update package could not be found.", zipPath);

        var normalizedTargetVersion = Normalize(targetVersion ?? string.Empty);
        if (!Version.TryParse(normalizedTargetVersion, out _))
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

        // Re-verify the file that is about to be extracted, not just the one that was downloaded.
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

        var stageRoot = Path.Combine(
            Path.GetTempPath(),
            "PejPass-update-" + Guid.NewGuid().ToString("N"));

        string? scriptPath = null;

        try
        {
            Directory.CreateDirectory(stageRoot);
            ZipFile.ExtractToDirectory(zipPath, stageRoot);

            var sourceDir = ResolveExtractedPayloadRoot(stageRoot);
            if (!File.Exists(Path.Combine(sourceDir, "PejPass.exe")))
            {
                throw new InvalidDataException(
                    "The update package does not place PejPass.exe at the root of its application payload.");
            }

            TryDeleteFile(zipPath);

            scriptPath = Path.Combine(
                Path.GetTempPath(),
                "PejPass-apply-update-" + Guid.NewGuid().ToString("N") + ".ps1");

            File.WriteAllText(
                scriptPath,
                BuildApplyScript(
                    appDir,
                    sourceDir,
                    processPath,
                    stageRoot,
                    PendingWhatsNewPath,
                    Path.Combine(Path.GetTempPath(), "PejPass-backup-" + Guid.NewGuid().ToString("N")),
                    Environment.ProcessId),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            return new StagedUpdate(scriptPath, stageRoot, normalizedTargetVersion);
        }
        catch
        {
            // Never leave a half-extracted payload or a script behind.
            TryDeleteDirectory(stageRoot);
            if (scriptPath is not null)
                TryDeleteFile(scriptPath);

            throw;
        }
    }

    /// <summary>Must run on the UI thread: it shuts the application down.</summary>
    internal static void LaunchStagedUpdate(StagedUpdate staged)
    {
        try
        {
            var powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(staged.ScriptPath);

            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("The update process could not be started.");
        }
        catch
        {
            TryDeleteDirectory(staged.StageRoot);
            TryDeleteFile(staged.ScriptPath);
            throw;
        }

        WritePendingWhatsNew(staged.TargetVersion);

        var application = System.Windows.Application.Current;
        application?.Dispatcher.Invoke(() =>
            {
                App.Services?.GetService<SystemTrayService>()?.PrepareForExit();
                application.Shutdown();
            });
    }

    internal static string BuildApplyScript(
        string appDir,
        string sourceDir,
        string exePath,
        string stageRoot,
        string pendingPath,
        string backupDir,
        int processId)
    {
        var nl = Environment.NewLine;

        return string.Join(nl, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "$AppDir = " + EscapePowerShellLiteral(appDir),
            "$SourceDir = " + EscapePowerShellLiteral(sourceDir),
            "$ExePath = " + EscapePowerShellLiteral(exePath),
            "$StageRoot = " + EscapePowerShellLiteral(stageRoot),
            "$PendingPath = " + EscapePowerShellLiteral(pendingPath),
            "$BackupDir = " + EscapePowerShellLiteral(backupDir),
            "$AppProcessId = " + processId,
            "$LockPath = Join-Path $env:LOCALAPPDATA 'PejPass\\BrowserHost.update.lock'",
            "$UpdateMutexName = 'Local\\PejPass.UpdateLock.v1'",
            "$UpdateEventName = 'Local\\PejPass.UpdateRequested.v1'",
            "$ErrorPath = Join-Path $env:TEMP 'PejPass-update-error.txt'",
            "$ScriptPath = $MyInvocation.MyCommand.Path",
            "$UpdateLock = $null",
            "$UpdateEvent = $null",
            "$SourceHostRoot = Join-Path $SourceDir 'BrowserHost'",
            "$HostRoot = [System.IO.Path]::GetFullPath((Join-Path $AppDir 'BrowserHost'))",
            "$HostRootPrefix = $HostRoot + [System.IO.Path]::DirectorySeparatorChar",
            "$PausedHostFiles = [System.Collections.Generic.List[string]]::new()",
            "$UpdateMutex = [System.Threading.Mutex]::new($false, $UpdateMutexName)",
            "$UpdateMutexAcquired = $false",
            "try { $UpdateMutexAcquired = $UpdateMutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $UpdateMutexAcquired = $true }",
            "if (-not $UpdateMutexAcquired) {",
            "    Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue",
            "    Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue",
            "    $UpdateMutex.Dispose()",
            "    exit 2",
            "}",
            "$UpdateEvent = [System.Threading.EventWaitHandle]::new($true, [System.Threading.EventResetMode]::ManualReset, $UpdateEventName)",
            "$BackupComplete = $false",
            "$InstallStarted = $false",
            "$RollbackSucceeded = $false",
            "$FailureMessage = $null",
            "try {",
            "    $AppDeadline = [DateTime]::UtcNow.AddSeconds(120)",
            "    $AppProcess = Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue",
            "    while ($null -ne $AppProcess) {",
            "        if ([DateTime]::UtcNow -ge $AppDeadline) { throw [System.TimeoutException]::new('Timed out waiting for PejPass to exit.') }",
            "        Start-Sleep -Seconds 1",
            "        $AppProcess = Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue",
            "    }",
            "",
            "    $LockDirectory = Split-Path -Parent $LockPath",
            "    [System.IO.Directory]::CreateDirectory($LockDirectory) | Out-Null",
            "    $LockDeadline = [DateTime]::UtcNow.AddSeconds(120)",
            "    while ($null -eq $UpdateLock) {",
            "        try {",
            "            $UpdateLock = [System.IO.File]::Open($LockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)",
            "        } catch [System.IO.IOException] {",
            "            if ([DateTime]::UtcNow -ge $LockDeadline) { throw [System.TimeoutException]::new('Timed out waiting for BrowserHost processes to exit.') }",
            "            Start-Sleep -Seconds 1",
            "        }",
            "    }",
            "",
            "    if (-not (Test-Path -LiteralPath $AppDir -PathType Container)) { throw [System.IO.DirectoryNotFoundException]::new('The PejPass installation directory was not found.') }",
            "    if (-not (Test-Path -LiteralPath $SourceHostRoot -PathType Container)) { throw [System.IO.DirectoryNotFoundException]::new('The update package is missing its BrowserHost directory.') }",
            "    $SourceHostExecutables = @(Get-ChildItem -LiteralPath $SourceHostRoot -Filter 'PejPass.BrowserHost.exe' -File -Recurse -ErrorAction SilentlyContinue)",
            "    if ($SourceHostExecutables.Count -eq 0) { throw [System.IO.FileNotFoundException]::new('The update package does not contain PejPass.BrowserHost.exe.') }",
            "    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null",
            "    & robocopy $AppDir $BackupDir /E /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null",
            "    $BackupExitCode = $LASTEXITCODE",
            "    if ($BackupExitCode -ge 8) { throw [System.IO.IOException]::new('Could not back up the existing PejPass installation. Robocopy exit code: ' + $BackupExitCode) }",
            "    if (-not (Test-Path -LiteralPath (Join-Path $BackupDir 'PejPass.exe') -PathType Leaf)) { throw [System.IO.IOException]::new('The PejPass backup is incomplete because PejPass.exe is missing.') }",
            "    $BackupComplete = $true",
            "",
            "    $InstallStarted = $true",
            "    $HostShutdownDeadline = [DateTime]::UtcNow.AddSeconds(30)",
            "    while ($true) {",
            "        foreach ($HostProcess in @(Get-Process -Name 'PejPass.BrowserHost' -ErrorAction SilentlyContinue)) {",
            "            try {",
            "                $HostProcessPath = $HostProcess.Path",
            "                if ($HostProcessPath -and $HostProcessPath.StartsWith($HostRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {",
            "                    Stop-Process -Id $HostProcess.Id -Force -ErrorAction SilentlyContinue",
            "                }",
            "            } catch { }",
            "        }",
            "        $PauseFailed = $false",
            "        foreach ($DestinationHost in @(Get-ChildItem -LiteralPath $HostRoot -Filter 'PejPass.BrowserHost.exe' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)) {",
            "            $PausePath = $DestinationHost + '.update-paused.' + $AppProcessId + '.' + [Guid]::NewGuid().ToString('N')",
            "            try {",
            "                Move-Item -LiteralPath $DestinationHost -Destination $PausePath -ErrorAction Stop",
            "                $PausedHostFiles.Add($PausePath)",
            "            } catch {",
            "                $PauseFailed = $true",
            "            }",
            "        }",
            "        $ActiveHosts = [System.Collections.Generic.List[int]]::new()",
            "        foreach ($HostProcess in @(Get-Process -Name 'PejPass.BrowserHost' -ErrorAction SilentlyContinue)) {",
            "            try {",
            "                $HostProcessPath = $HostProcess.Path",
            "                if ($HostProcessPath -and $HostProcessPath.StartsWith($HostRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {",
            "                    $ActiveHosts.Add($HostProcess.Id)",
            "                }",
            "            } catch { }",
            "        }",
            "        $RemainingHostExecutables = @(Get-ChildItem -LiteralPath $HostRoot -Filter 'PejPass.BrowserHost.exe' -File -Recurse -ErrorAction SilentlyContinue)",
            "        if (-not $PauseFailed -and $ActiveHosts.Count -eq 0 -and $RemainingHostExecutables.Count -eq 0) { break }",
            "        if ([DateTime]::UtcNow -ge $HostShutdownDeadline) { throw [System.TimeoutException]::new('Timed out stopping and pausing existing BrowserHost processes.') }",
            "        Start-Sleep -Seconds 1",
            "    }",
            "    & robocopy $SourceDir $AppDir /E /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null",
            "    $InstallExitCode = $LASTEXITCODE",
            "    if ($InstallExitCode -ge 8) { throw [System.IO.IOException]::new('Could not copy the update files. Robocopy exit code: ' + $InstallExitCode) }",
            "    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw [System.IO.IOException]::new('The updated PejPass.exe is missing after installation.') }",
            "",
            "    $StartedProcess = Start-Process -FilePath $ExePath -PassThru -ErrorAction Stop",
            "    Start-Sleep -Seconds 3",
            "    $StartedProcess.Refresh()",
            "    if ($StartedProcess.HasExited) { throw [System.InvalidOperationException]::new('The updated PejPass process exited during startup.') }",
            "    foreach ($PausedHostFile in $PausedHostFiles) { Remove-Item -LiteralPath $PausedHostFile -Force -ErrorAction SilentlyContinue }",
            "} catch {",
            "    $FailureMessage = $_.Exception.Message",
            "    if ($InstallStarted -and $BackupComplete) {",
            "        $RollbackExitCode = 16",
            "        try {",
            "            & robocopy $BackupDir $AppDir /MIR /IS /IT /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null",
            "            $RollbackExitCode = $LASTEXITCODE",
            "            $RollbackSucceeded = $RollbackExitCode -lt 8 -and (Test-Path -LiteralPath $ExePath -PathType Leaf)",
            "        } catch {",
            "            $RollbackSucceeded = $false",
            "        }",
            "        if (-not $RollbackSucceeded) {",
            "            $FailureMessage += ' Rollback failed (robocopy exit code ' + $RollbackExitCode + ').'",
            "        }",
            "    }",
            "} finally {",
            "    if ($null -ne $UpdateLock) { $UpdateLock.Dispose() }",
            "    if ($null -ne $UpdateEvent) { $UpdateEvent.Dispose() }",
            "    if ($UpdateMutexAcquired) { $UpdateMutex.ReleaseMutex() }",
            "    $UpdateMutex.Dispose()",
            "}",
            "",
            "if ($null -eq $FailureMessage) {",
            "    Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue",
            "    Remove-Item -LiteralPath $BackupDir -Recurse -Force -ErrorAction SilentlyContinue",
            "    Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue",
            "    exit 0",
            "}",
            "",
            "if ($RollbackSucceeded) {",
            "    Remove-Item -LiteralPath $BackupDir -Recurse -Force -ErrorAction SilentlyContinue",
            "} elseif ($BackupComplete -and $InstallStarted) {",
            "    $FailureMessage += ' Backup preserved at: ' + $BackupDir",
            "} else {",
            "    Remove-Item -LiteralPath $BackupDir -Recurse -Force -ErrorAction SilentlyContinue",
            "}",
            "",
            "if ((-not $InstallStarted -or $RollbackSucceeded) -and (Test-Path -LiteralPath $ExePath -PathType Leaf)) {",
            "    try { Start-Process -FilePath $ExePath -ErrorAction Stop | Out-Null }",
            "    catch { $FailureMessage += ' The previous PejPass process could not be started: ' + $_.Exception.Message }",
            "}",
            "Remove-Item -LiteralPath $PendingPath -Force -ErrorAction SilentlyContinue",
            "Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue",
            "try { Set-Content -LiteralPath $ErrorPath -Value $FailureMessage -Encoding UTF8 -Force } catch { }",
            "Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue",
            "exit 1"
        }) + nl;
    }

    private static string EscapePowerShellLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    internal static void ValidateUpdateArchive(
        ZipArchive archive,
        int maxEntries = MaxUpdateArchiveEntries,
        long maxTotalUncompressedBytes = MaxUpdateUncompressedBytes,
        long maxEntryUncompressedBytes = MaxUpdateEntryBytes)
    {
        ArgumentNullException.ThrowIfNull(archive);

        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalUncompressedBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntryUncompressedBytes, 1);

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

        if (contentLength == 0)
            throw new InvalidDataException("Update manifest is empty.");

        if (contentLength is > MaxUpdateManifestBytes)
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

        if (manifest.AdditionalSignatures is null ||
            manifest.AdditionalSignatures.Count > MaxAdditionalSignatures)
        {
            throw new InvalidDataException("Update manifest contains too many signatures.");
        }

        foreach (var signature in manifest.AdditionalSignatures)
        {
            if (signature is null)
                throw new InvalidDataException("Update manifest contains a null additional signature.");

            ValidateText(signature, "additional signature", 256);
        }

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

        var isLatest = SameVersion(norm, latestNorm);
        var isInstalled = SameVersion(norm, installedNorm);
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
        if (TryParseCanonicalVersion(a, out var va)
            && TryParseCanonicalVersion(b, out var vb))
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

    internal static bool SameVersion(string first, string second)
    {
        return TryParseCanonicalVersion(first, out var firstVersion) &&
               TryParseCanonicalVersion(second, out var secondVersion) &&
               firstVersion.CompareTo(secondVersion) == 0;
    }

    public static bool IsNewerVersion(string candidate, string current)
    {
        if (!TryParseCanonicalVersion(candidate, out var next))
            return false;

        if (!TryParseCanonicalVersion(current, out var cur))
            return true;

        return next.CompareTo(cur) > 0;
    }

    private static bool TryParseCanonicalVersion(string? value, out Version version)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Version.TryParse(Normalize(value), out var parsed))
        {
            version = null!;
            return false;
        }

        // Treat omitted trailing components as zero (e.g. 1.2 == 1.2.0).
        version = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
        return true;
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

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
