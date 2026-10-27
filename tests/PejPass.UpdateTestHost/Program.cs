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

var exitAfterText = Environment.GetEnvironmentVariable("PEJPASS_UPDATE_TEST_EXIT_AFTER_MS");
if (int.TryParse(exitAfterText, NumberStyles.None, CultureInfo.InvariantCulture, out var exitAfterMilliseconds))
{
    await Task.Delay(exitAfterMilliseconds);
    return 0;
}

await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;
