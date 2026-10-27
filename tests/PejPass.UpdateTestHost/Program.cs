using System.Diagnostics;
using System.Globalization;

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
