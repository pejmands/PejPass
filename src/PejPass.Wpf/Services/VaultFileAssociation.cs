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

            // While debugging under `dotnet run`, ProcessPath can be the apphost — still fine.
            var command = $"\"{exe}\" \"%1\"";
            var icon = $"\"{exe}\",0";

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            if (classes is null) return;

            using (var ext = classes.CreateSubKey(Extension))
            {
                ext?.SetValue(null, ProgId);
            }

            using (var prog = classes.CreateSubKey(ProgId))
            {
                if (prog is null) return;
                prog.SetValue(null, "PejPass Vault");

                using (var defaultIcon = prog.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue(null, icon);

                using var commandKey = prog.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue(null, command);
            }

            // Notify the shell that associations changed (best-effort)
            try
            {
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
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
