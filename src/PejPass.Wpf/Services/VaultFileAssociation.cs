using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace PejPass.Wpf.Services;

/// <summary>
/// Registers .pejpass → PejPass for the current user (HKCU) so double-click opens the vault.
/// </summary>
public static partial class VaultFileAssociation
{
    private const string ProgId = "PejPass.Vault";
    private const string Extension = ".pejpass";

    public static void EnsureRegistered()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            var command = $"\"{exe}\" \"%1\"";
            var icon = $"\"{exe}\",0";
            var classesPath = @"Software\Classes";

            using var classes = Registry.CurrentUser.OpenSubKey(classesPath);
            var needsRegistration = false;

            if (classes is null)
            {
                needsRegistration = true;
            }
            else
            {
                using var ext = classes.OpenSubKey(Extension);
                var registeredProgId = ext?.GetValue(null) as string;

                using var prog = classes.OpenSubKey(ProgId);
                var registeredIcon = prog?.OpenSubKey("DefaultIcon")?.GetValue(null) as string;
                var registeredCommand = prog?.OpenSubKey(@"shell\open\command")?.GetValue(null) as string;

                needsRegistration =
                    !string.Equals(registeredProgId, ProgId, StringComparison.Ordinal) ||
                    !string.Equals(registeredIcon, icon, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(registeredCommand, command, StringComparison.OrdinalIgnoreCase);
            }

            if (!needsRegistration)
                return;

            using var writableClasses = Registry.CurrentUser.CreateSubKey(classesPath);
            if (writableClasses is null)
                return;

            using (var ext = writableClasses.CreateSubKey(Extension))
            {
                ext?.SetValue(null, ProgId);
            }

            using (var prog = writableClasses.CreateSubKey(ProgId))
            {
                if (prog is null)
                    return;

                prog.SetValue(null, "PejPass Vault");

                using (var defaultIcon = prog.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue(null, icon);

                using var commandKey = prog.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue(null, command);
            }

            // Notify the shell only when the association was changed.
            try
            {
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // ignore
            }
        }
        catch
        {
            // File association is best-effort; app must still run without it
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
