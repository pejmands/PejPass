using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace PejPass.BrowserHost;

internal static class TrayHostDiagnostic
{
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NifGuid = 0x20;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PejPass", "tray-diag");

    public static void RunIfRequested()
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "tray-diag.on");
        if (!File.Exists(marker))
            return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory,
                $"host-tray-diag-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-pid{Environment.ProcessId}.log");
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            Log(writer, "BrowserHost tray diagnostic started.");

            using var process = Process.GetCurrentProcess();
            Log(writer, $"Process: {process.ProcessName}; PID={process.Id}; path={Environment.ProcessPath ?? "(unknown)"}");
            Log(writer, $"User: {SafeIdentityName()}; session={process.SessionId}; 64-bit process={Environment.Is64BitProcess}; interactive={Environment.UserInteractive}");
            LogIntegrityLevel(writer);

            var tray = FindWindow("Shell_TrayWnd", null);
            Log(writer, $"Shell_TrayWnd HWND=0x{tray.ToInt64():X}");
            if (tray != IntPtr.Zero)
            {
                GetWindowThreadProcessId(tray, out var ownerPid);
                Log(writer, $"Shell_TrayWnd owner PID={ownerPid}");
            }

            var inJobResult = IsProcessInJob(process.Handle, IntPtr.Zero, out var inJob);
            Log(writer, $"IsProcessInJob returned {inJobResult}; inJob={inJob}; error={(inJobResult ? 0 : Marshal.GetLastWin32Error())}");

            var hwnd = CreateWindowEx(0, "STATIC", "PejPass tray diagnostic", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                Log(writer, $"CreateWindowEx failed; error={Marshal.GetLastWin32Error()}");
                return;
            }

            try
            {
                Log(writer, $"Diagnostic HWND=0x{hwnd.ToInt64():X}; NOTIFYICONDATAW size={Marshal.SizeOf<NotifyIconData>()}");
                RunAddTest(writer, "A: NIM_ADD without NIF_GUID", hwnd, 1, null);
                RunAddTest(writer, "B: NIM_ADD with fresh GUID", hwnd, 2, Guid.NewGuid());
            }
            finally
            {
                DestroyWindow(hwnd);
            }

            Log(writer, "BrowserHost tray diagnostic completed.");
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(Path.Combine(LogDirectory, $"host-tray-diag-failure-pid{Environment.ProcessId}.log"),
                    $"{DateTime.UtcNow:O} Diagnostic failed: {ex.GetType().FullName}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch { }
        }
    }

    private static void RunAddTest(TextWriter writer, string name, IntPtr hwnd, uint id, Guid? guid)
    {
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = hwnd,
            Id = id,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = 0x8001,
            Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
            Tip = "PejPass host tray diagnostic",
            Info = string.Empty,
            InfoTitle = string.Empty
        };

        if (guid is { } value)
        {
            data.Flags |= NifGuid;
            data.GuidItem = value;
        }

        var added = Shell_NotifyIcon(NimAdd, ref data);
        var error = Marshal.GetLastWin32Error();
        Log(writer, $"{name}: Shell_NotifyIcon(NIM_ADD) returned {added}; GetLastError={error}; flags=0x{data.Flags:X}; guid={(guid?.ToString("D") ?? "(none)")}");
        if (!added)
            return;

        var deleteData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = hwnd,
            Id = id,
            Flags = guid.HasValue ? NifGuid : 0,
            GuidItem = guid.GetValueOrDefault()
        };
        var deleted = Shell_NotifyIcon(NimDelete, ref deleteData);
        Log(writer, $"{name}: cleanup NIM_DELETE returned {deleted}; GetLastError={Marshal.GetLastWin32Error()}");
    }

    private static void LogIntegrityLevel(TextWriter writer)
    {
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0008, out token))
            {
                Log(writer, $"Token integrity query failed: OpenProcessToken error={Marshal.GetLastWin32Error()}");
                return;
            }

            GetTokenInformation(token, 25, IntPtr.Zero, 0, out var required);
            if (required == 0)
            {
                Log(writer, $"Token integrity query failed: GetTokenInformation error={Marshal.GetLastWin32Error()}");
                return;
            }

            buffer = Marshal.AllocHGlobal((int)required);
            if (!GetTokenInformation(token, 25, buffer, required, out _))
            {
                Log(writer, $"Token integrity query failed: GetTokenInformation error={Marshal.GetLastWin32Error()}");
                return;
            }

            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(label.Label.Sid));
            if (count == 0)
            {
                Log(writer, "Token integrity level unavailable (SID has no subauthorities)");
                return;
            }

            var rid = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(label.Label.Sid, (uint)(count - 1))));
            var name = rid switch
            {
                < 0x1000 => "Untrusted",
                < 0x2000 => "Low",
                < 0x3000 => "Medium",
                < 0x4000 => "High",
                < 0x5000 => "System",
                _ => "Protected"
            };
            Log(writer, $"Token integrity level: {name}; RID=0x{rid:X}");
        }
        catch (Exception ex)
        {
            Log(writer, $"Token integrity query failed: {ex.GetType().Name}");
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    private static string SafeIdentityName()
    {
        try { return WindowsIdentity.GetCurrent().Name; }
        catch { return "(unavailable)"; }
    }

    private static void Log(TextWriter writer, string message) =>
        writer.WriteLine($"{DateTime.UtcNow:O} {message}");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr HWnd;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, [MarshalAs(UnmanagedType.Bool)] out bool result);
}
