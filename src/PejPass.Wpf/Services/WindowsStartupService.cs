using Microsoft.Win32;

namespace PejPass.Wpf.Services;

public static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PejPass";

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: true);

            if (key is null)
            {
                error = "The Windows startup registry key is unavailable.";
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                error = null;
                return true;
            }

            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                error = "The PejPass executable path could not be determined.";
                return false;
            }

            key.SetValue(
                ValueName,
                $"\"{executablePath}\" --startup",
                RegistryValueKind.String);

            error = null;
            return true;
        }
        catch (Exception)
        {
            error = "Could not update the Windows startup setting.";
            return false;
        }
    }
}
