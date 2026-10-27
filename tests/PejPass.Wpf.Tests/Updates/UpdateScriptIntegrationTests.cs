using PejPass.Wpf.Services;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateScriptIntegrationTests
{
    private static readonly TimeSpan UpdaterTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task GeneratedUpdater_InstallsPayloadAndRemovesBackupAfterSuccessfulStartup()
    {
        using var layout = UpdateTestLayout.Create();

        using var updater = StartUpdater(layout, exitAfterMilliseconds: null);
        var result = await updater.WaitForExitAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("new-version", File.ReadAllText(layout.VersionPath));
        Assert.Equal("new-host", File.ReadAllText(layout.InstalledHostPath));
        Assert.False(Directory.Exists(layout.BackupDirectory));
        Assert.False(Directory.Exists(layout.StageRoot));
        Assert.False(File.Exists(layout.ScriptPath));
        await WaitForProcessCountAsync(layout.ProcessIdLogPath, 1, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GeneratedUpdater_RestoresBackupWhenUpdatedProcessExitsDuringStartup()
    {
        using var layout = UpdateTestLayout.Create();

        using var updater = StartUpdater(layout, exitAfterMilliseconds: 400);
        var result = await updater.WaitForExitAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("old-version", File.ReadAllText(layout.VersionPath));
        Assert.Equal("old-host", File.ReadAllText(layout.InstalledHostPath));
        Assert.False(Directory.Exists(layout.BackupDirectory));
        Assert.False(Directory.Exists(layout.StageRoot));
        Assert.True(File.Exists(layout.ErrorPath));
        Assert.Contains("exited during startup", File.ReadAllText(layout.ErrorPath), StringComparison.OrdinalIgnoreCase);
        await WaitForProcessCountAsync(layout.ProcessIdLogPath, 2, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GeneratedUpdater_PreservesBackupWhenRollbackCannotReplaceLockedFile()
    {
        using var layout = UpdateTestLayout.Create();

        using var updater = StartUpdater(layout, exitAfterMilliseconds: 1_000);
        await WaitForProcessCountAsync(layout.ProcessIdLogPath, 1, TimeSpan.FromSeconds(10));

        ScriptResult result;
        Assert.Equal("new-version", File.ReadAllText(layout.VersionPath));
        using (var lockedVersionFile = new FileStream(
            layout.VersionPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
        {
            result = await updater.WaitForExitAsync();
        }

        Assert.Equal(1, result.ExitCode);
        Assert.True(Directory.Exists(layout.BackupDirectory));
        Assert.Equal("old-version", File.ReadAllText(Path.Combine(layout.BackupDirectory, "version.txt")));
        Assert.True(File.Exists(layout.ErrorPath));

        var error = File.ReadAllText(layout.ErrorPath);
        Assert.Contains("Rollback failed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(layout.BackupDirectory, error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(layout.StageRoot));
    }

    private static RunningUpdater StartUpdater(UpdateTestLayout layout, int? exitAfterMilliseconds)
    {
        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        Assert.True(File.Exists(powerShellPath), $"Windows PowerShell was not found at '{powerShellPath}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(layout.ScriptPath);

        startInfo.Environment["LOCALAPPDATA"] = layout.LocalAppDataDirectory;
        startInfo.Environment["TEMP"] = layout.TempDirectory;
        startInfo.Environment["TMP"] = layout.TempDirectory;
        startInfo.Environment["PEJPASS_UPDATE_TEST_PID_LOG"] = layout.ProcessIdLogPath;

        if (exitAfterMilliseconds is null)
            startInfo.Environment.Remove("PEJPASS_UPDATE_TEST_EXIT_AFTER_MS");
        else
            startInfo.Environment["PEJPASS_UPDATE_TEST_EXIT_AFTER_MS"] =
                exitAfterMilliseconds.Value.ToString(CultureInfo.InvariantCulture);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not launch the updater integration test process.");

        return new RunningUpdater(process);
    }

    private static async Task WaitForProcessCountAsync(string path, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    if (File.ReadAllLines(path).Length >= expectedCount)
                        return;
                }
                catch (IOException)
                {
                }
            }

            await Task.Delay(50);
        }

        var actual = File.Exists(path) ? File.ReadAllLines(path).Length : 0;
        throw new TimeoutException($"Expected at least {expectedCount} test app process ID(s), but found {actual}.");
    }

    private sealed class RunningUpdater(Process process) : IDisposable
    {
        private readonly Task<string> _standardOutput = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _standardError = process.StandardError.ReadToEndAsync();

        public async Task<ScriptResult> WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(UpdaterTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                await process.WaitForExitAsync();
                throw new TimeoutException(
                    "The updater integration script did not finish in time.\n" +
                    "STDOUT:\n" + await _standardOutput + "\nSTDERR:\n" + await _standardError);
            }

            return new ScriptResult(
                process.ExitCode,
                await _standardOutput,
                await _standardError);
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class UpdateTestLayout : IDisposable
    {
        private UpdateTestLayout(string rootDirectory)
        {
            RootDirectory = rootDirectory;
            InstallDirectory = Path.Combine(rootDirectory, "Install");
            StageRoot = Path.Combine(rootDirectory, "Stage");
            SourceDirectory = Path.Combine(StageRoot, "Payload");
            BackupDirectory = Path.Combine(rootDirectory, "Backup");
            LocalAppDataDirectory = Path.Combine(rootDirectory, "LocalAppData");
            TempDirectory = Path.Combine(rootDirectory, "Temp");
            PendingPath = Path.Combine(rootDirectory, "State", "pending-whats-new.txt");
            ScriptPath = Path.Combine(rootDirectory, "apply-update.ps1");
            ProcessIdLogPath = Path.Combine(rootDirectory, "State", "started-processes.txt");
            ErrorPath = Path.Combine(TempDirectory, "PejPass-update-error.txt");
            CoordinationSuffix = Guid.NewGuid().ToString("N");
        }

        public string RootDirectory { get; }
        public string InstallDirectory { get; }
        public string StageRoot { get; }
        public string SourceDirectory { get; }
        public string BackupDirectory { get; }
        public string LocalAppDataDirectory { get; }
        public string TempDirectory { get; }
        public string PendingPath { get; }
        public string ScriptPath { get; }
        public string ProcessIdLogPath { get; }
        public string ErrorPath { get; }
        public string CoordinationSuffix { get; }
        public string VersionPath => Path.Combine(InstallDirectory, "version.txt");
        public string InstalledHostPath => Path.Combine(InstallDirectory, "BrowserHost", "PejPass.BrowserHost.exe");

        public static UpdateTestLayout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PejPass-update-integration-" + Guid.NewGuid().ToString("N"));
            var layout = new UpdateTestLayout(root);

            Directory.CreateDirectory(layout.InstallDirectory);
            Directory.CreateDirectory(Path.Combine(layout.InstallDirectory, "BrowserHost"));
            Directory.CreateDirectory(layout.SourceDirectory);
            Directory.CreateDirectory(Path.Combine(layout.SourceDirectory, "BrowserHost"));
            Directory.CreateDirectory(layout.LocalAppDataDirectory);
            Directory.CreateDirectory(layout.TempDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(layout.PendingPath)!);

            CopyTestHostRuntime(layout.InstallDirectory);
            CopyTestHostRuntime(layout.SourceDirectory);

            File.WriteAllText(layout.VersionPath, "old-version");
            File.WriteAllText(Path.Combine(layout.SourceDirectory, "version.txt"), "new-version");
            File.WriteAllText(layout.InstalledHostPath, "old-host");
            File.WriteAllText(
                Path.Combine(layout.SourceDirectory, "BrowserHost", "PejPass.BrowserHost.exe"),
                "new-host");
            File.WriteAllText(layout.PendingPath, "2.0.1");

            var script = UpdateService.BuildApplyScript(
                layout.InstallDirectory,
                layout.SourceDirectory,
                Path.Combine(layout.InstallDirectory, "PejPass.exe"),
                layout.StageRoot,
                layout.PendingPath,
                layout.BackupDirectory,
                processId: int.MaxValue,
                coordinationSuffix: layout.CoordinationSuffix);

            File.WriteAllText(
                layout.ScriptPath,
                script,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            return layout;
        }

        private static void CopyTestHostRuntime(string destinationDirectory)
        {
            var helperExe = Path.Combine(AppContext.BaseDirectory, "PejPass.UpdateTestHost.exe");
            if (!File.Exists(helperExe))
                throw new FileNotFoundException("Updater test host executable was not copied to the test output.", helperExe);

            foreach (var file in Directory.EnumerateFiles(
                         AppContext.BaseDirectory,
                         "PejPass.UpdateTestHost.*",
                         SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
            }

            var appExecutable = Path.Combine(destinationDirectory, "PejPass.exe");
            File.Copy(helperExe, appExecutable, overwrite: true);

            CopyIfExists(
                Path.Combine(destinationDirectory, "PejPass.UpdateTestHost.dll"),
                Path.Combine(destinationDirectory, "PejPass.dll"));
            CopyIfExists(
                Path.Combine(destinationDirectory, "PejPass.UpdateTestHost.runtimeconfig.json"),
                Path.Combine(destinationDirectory, "PejPass.runtimeconfig.json"));
            CopyIfExists(
                Path.Combine(destinationDirectory, "PejPass.UpdateTestHost.deps.json"),
                Path.Combine(destinationDirectory, "PejPass.deps.json"));
        }

        private static void CopyIfExists(string source, string destination)
        {
            if (File.Exists(source))
                File.Copy(source, destination, overwrite: true);
        }

        public void Dispose()
        {
            StopLoggedProcesses(ProcessIdLogPath);

            try
            {
                if (Directory.Exists(RootDirectory))
                    Directory.Delete(RootDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void StopLoggedProcesses(string path)
        {
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadAllLines(path))
            {
                if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
                    continue;

                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5_000);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
        }
    }
}
