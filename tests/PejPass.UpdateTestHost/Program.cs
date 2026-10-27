using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var pidLogPath = Environment.GetEnvironmentVariable("PEJPASS_UPDATE_TEST_PID_LOG");
if (!string.IsNullOrWhiteSpace(pidLogPath))
{
    var directory = Path.GetDirectoryName(pidLogPath);
    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    await File.AppendAllTextAsync(
        pidLogPath,
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
}

var stageZipPath = Environment.GetEnvironmentVariable("PEJPASS_UPDATE_TEST_STAGE_ZIP");
if (!string.IsNullOrWhiteSpace(stageZipPath))
{
    try
    {
        var targetVersion = GetRequiredEnvironmentVariable("PEJPASS_UPDATE_TEST_TARGET_VERSION");
        var installExePath = GetRequiredEnvironmentVariable("PEJPASS_UPDATE_TEST_INSTALL_EXE_PATH");
        var pendingPath = GetRequiredEnvironmentVariable("PEJPASS_UPDATE_TEST_PENDING_PATH");
        var stageResultPath = GetRequiredEnvironmentVariable("PEJPASS_UPDATE_TEST_STAGE_RESULT_PATH");

        using var packageStream = File.OpenRead(stageZipPath);
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageStream));

        AssemblyLoadContext.Default.Resolving += static (context, assemblyName) =>
        {
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
                return null;

            var candidatePath = Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll");
            return File.Exists(candidatePath)
                ? context.LoadFromAssemblyPath(candidatePath)
                : null;
        };

        var updateAssemblyPath = Path.Combine(AppContext.BaseDirectory, "PejPass.dll");
        if (!File.Exists(updateAssemblyPath))
            throw new FileNotFoundException("PejPass.dll was not found beside the updater test host.", updateAssemblyPath);

        var updateAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(updateAssemblyPath);
        var updateServiceType = updateAssembly.GetType("PejPass.Wpf.Services.UpdateService", throwOnError: true)!;
        var stageMethod = updateServiceType.GetMethod(
            "StageUpdate",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("UpdateService.StageUpdate was not found.");

        var coordinationSuffix = "e2e_" + Guid.NewGuid().ToString("N");
        var staged = stageMethod.Invoke(
            null,
            [stageZipPath, targetVersion, expectedSha256, installExePath, coordinationSuffix])
            ?? throw new InvalidOperationException("UpdateService.StageUpdate returned no staged update.");

        var stagedType = staged.GetType();
        var scriptPath = GetStagedPath(stagedType, staged, "ScriptPath");
        var stageRoot = GetStagedPath(stagedType, staged, "StageRoot");
        var scriptText = File.ReadAllText(scriptPath);

        var pendingMatch = Regex.Match(scriptText, @"(?m)^\$PendingPath = .*$");
        if (!pendingMatch.Success)
            throw new InvalidDataException("The generated update script did not contain its pending What's New path.");

        scriptText = scriptText
            .Remove(pendingMatch.Index, pendingMatch.Length)
            .Insert(pendingMatch.Index, "$PendingPath = " + EscapePowerShellLiteral(pendingPath));

        var backupMatch = Regex.Match(scriptText, @"(?m)^\$BackupDir = '((?:''|[^'])*)'\s*$");
        if (!backupMatch.Success)
            throw new InvalidDataException("The generated update script did not contain its backup path.");

        var backupDirectory = backupMatch.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var stageResultDirectory = Path.GetDirectoryName(stageResultPath);
        if (!string.IsNullOrWhiteSpace(stageResultDirectory))
            Directory.CreateDirectory(stageResultDirectory);

        await File.WriteAllLinesAsync(stageResultPath, [scriptPath, stageRoot, backupDirectory]);

        foreach (var name in new[]
                 {
                     "PEJPASS_UPDATE_TEST_STAGE_ZIP",
                     "PEJPASS_UPDATE_TEST_TARGET_VERSION",
                     "PEJPASS_UPDATE_TEST_INSTALL_EXE_PATH",
                     "PEJPASS_UPDATE_TEST_PENDING_PATH",
                     "PEJPASS_UPDATE_TEST_STAGE_RESULT_PATH"
                 })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

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
        startInfo.ArgumentList.Add(scriptPath);

        using var updater = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated updater script could not be started.");

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 50;
    }
}

if (Environment.GetEnvironmentVariable("PEJPASS_UPDATE_TEST_HOLD_HOST_LOCK") == "1")
{
    var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    if (string.IsNullOrWhiteSpace(localAppData))
        return 20;

    var lockDirectory = Path.Combine(localAppData, "PejPass");
    Directory.CreateDirectory(lockDirectory);

    var lockPath = Path.Combine(lockDirectory, "BrowserHost.update.lock");
    using var updateLock = new FileStream(
        lockPath,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read);

    var readyPath = Environment.GetEnvironmentVariable("PEJPASS_UPDATE_TEST_HOST_LOCK_READY");
    if (!string.IsNullOrWhiteSpace(readyPath))
        await File.WriteAllTextAsync(readyPath, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

var exitAfterText = Environment.GetEnvironmentVariable("PEJPASS_UPDATE_TEST_EXIT_AFTER_MS");
if (int.TryParse(exitAfterText, NumberStyles.None, CultureInfo.InvariantCulture, out var exitAfterMilliseconds))
{
    await Task.Delay(exitAfterMilliseconds);
    return 0;
}

await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;

static string GetRequiredEnvironmentVariable(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required test environment variable '{name}' is missing.");

static string GetStagedPath(Type stagedType, object staged, string propertyName) =>
    stagedType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(staged) as string
    ?? throw new InvalidOperationException($"The staged update did not expose '{propertyName}'.");

static string EscapePowerShellLiteral(string value) =>
    "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
