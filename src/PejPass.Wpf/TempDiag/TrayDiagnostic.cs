using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Interop;

namespace PejPass.Wpf.TempDiag;

/// <summary>
/// Temporary, opt-in diagnostics for investigating tray icon creation failures.
/// Remove this class and its startup call after the issue is understood.
/// </summary>
internal static class TrayDiagnostic
{
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifState = 0x00000008;
    private const uint NifGuid = 0x00000020;
    private const uint NifShowTip = 0x00000080;
    private const uint NisHidden = 0x00000001;
    private const int JobObjectBasicUiRestrictions = 4;
    private const int JobObjectExtendedLimitInformation = 9;

    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PejPass", "tray-diag");

    public static void RunIfRequested(string[] args)
    {
        try
        {
            var enabledByArgument = args.Any(arg =>
                string.Equals(arg.Trim(), "--tray-diag", StringComparison.OrdinalIgnoreCase));
            var enabledByMarker = File.Exists(Path.Combine(AppContext.BaseDirectory, "tray-diag.on"));

            if (!enabledByArgument && !enabledByMarker)
                return;

            Directory.CreateDirectory(LogDirectory);
            var logPath = Path.Combine(LogDirectory,
                $"tray-diag-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-pid{Environment.ProcessId}.log");

            using var writer = new StreamWriter(logPath, append: false, new UTF8Encoding(false));
            WriteLog(writer, "Diagnostic started.");
            WriteProcessContext(writer, args);
            WriteShellContext(writer);
            WriteJobContext(writer);

            var realGuid = GetLibraryGuid();
            var freshGuid = Guid.NewGuid();
            WriteLog(writer, $"Expected H.NotifyIcon GUID: {realGuid:D}");
            WriteLog(writer, $"Fresh test GUID: {freshGuid:D}");
            WriteLog(writer, $"NOTIFYICONDATAW size: {Marshal.SizeOf<NotifyIconData>()}; pointer size: {IntPtr.Size}");
            WriteLog(writer, $"NOTIFYICONDATAW offsets: hWnd={Marshal.OffsetOf<NotifyIconData>(nameof(NotifyIconData.HWnd))}, uFlags={Marshal.OffsetOf<NotifyIconData>(nameof(NotifyIconData.Flags))}, guidItem={Marshal.OffsetOf<NotifyIconData>(nameof(NotifyIconData.GuidItem))}");

            using var window = new HwndSource(new HwndSourceParameters("PejPass.TrayDiagnostic")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0
            });

            var hwnd = window.Handle;
            WriteLog(writer, $"Diagnostic HWND: 0x{hwnd.ToInt64():X}");

            RunAddTest(writer, "A: NIM_ADD without NIF_GUID", hwnd, 1, null);
            RunAddTest(writer, "B: NIM_ADD with H.NotifyIcon GUID", hwnd, 2, realGuid);
            RunAddTest(writer, "C: NIM_ADD with fresh GUID", hwnd, 3, freshGuid);

            WriteLog(writer, "Diagnostic completed. No NIM_MODIFY probes were performed.");
        }
        catch (Exception ex)
        {
            // Diagnostics must never prevent the application from continuing startup.
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    Path.Combine(LogDirectory, $"tray-diag-failure-pid{Environment.ProcessId}.log"),
                    $"{DateTime.UtcNow:O} Diagnostic failed: {ex.GetType().FullName}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch
            {
                // Ignore all diagnostic logging failures.
            }
        }
    }

    private static void RunAddTest(TextWriter writer, string name, IntPtr hwnd, uint id, Guid? guid)
    {
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = hwnd,
            Id = id,
            Flags = NifMessage | NifIcon | NifTip | NifState | NifShowTip,
            CallbackMessage = 0x8001,
            Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
            Tip = "PejPass tray diagnostic",
            Info = string.Empty,
            InfoTitle = string.Empty,
            State = NisHidden,
            StateMask = NisHidden
        };

        if (guid is { } value)
        {
            data.Flags |= NifGuid;
            data.GuidItem = value;
        }

        var ok = Shell_NotifyIcon(NimAdd, ref data);
        var error = Marshal.GetLastWin32Error();
        WriteLog(writer, $"{name}: Shell_NotifyIcon(NIM_ADD) returned {ok}; GetLastError={error}; flags=0x{data.Flags:X}; id={id}; guid={(guid?.ToString("D") ?? "(none)")}; iconHandle=0x{data.Icon.ToInt64():X}");

        if (!ok)
            return;

        // Remove only the icon this test successfully added.
        var deleteData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = hwnd,
            Id = id,
            Flags = guid.HasValue ? NifGuid : 0,
            GuidItem = guid.GetValueOrDefault()
        };

        var deleted = Shell_NotifyIcon(NimDelete, ref deleteData);
        var deleteError = Marshal.GetLastWin32Error();
        WriteLog(writer, $"{name}: cleanup NIM_DELETE returned {deleted}; GetLastError={deleteError}");
    }

    private static void WriteProcessContext(TextWriter writer, string[] args)
    {
        using var process = Process.GetCurrentProcess();
        WriteLog(writer, $"Process: {process.ProcessName}; PID={process.Id}; path={Environment.ProcessPath ?? "(unknown)"}");
        WriteLog(writer, $"User: {SafeIdentityName()}; session={process.SessionId}; 64-bit process={Environment.Is64BitProcess}; 64-bit OS={Environment.Is64BitOperatingSystem}; interactive={Environment.UserInteractive}");
        WriteLog(writer, $"Current directory: {Environment.CurrentDirectory}");
        WriteLog(writer, $"Command-line argument count={args.Length}; switches={string.Join(",", args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).Select(a => a.Split('=', 2)[0]))}");
        WriteLog(writer, $"Window station: {GetUserObjectName(GetProcessWindowStation())}; desktop: {GetUserObjectName(GetThreadDesktop(GetCurrentThreadId()))}");

        try
        {
            var parentPid = GetParentProcessId(process.Handle);
            if (parentPid > 0)
            {
                using var parent = Process.GetProcessById(parentPid);
                WriteLog(writer, $"Parent process: PID={parentPid}; name={parent.ProcessName}; path={SafeProcessPath(parent)}");
            }
            else
            {
                WriteLog(writer, $"Parent process PID: {parentPid}");
            }
        }
        catch (Exception ex)
        {
            WriteLog(writer, $"Parent process lookup failed: {ex.GetType().Name}");
        }
    }

    private static void WriteShellContext(TextWriter writer)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        var shell = GetShellWindow();
        WriteLog(writer, $"Shell_TrayWnd HWND=0x{tray.ToInt64():X}; GetShellWindow HWND=0x{shell.ToInt64():X}");

        if (tray != IntPtr.Zero)
        {
            GetWindowThreadProcessId(tray, out var ownerPid);
            try
            {
                using var owner = Process.GetProcessById((int)ownerPid);
                WriteLog(writer, $"Shell_TrayWnd owner: PID={ownerPid}; name={owner.ProcessName}; path={SafeProcessPath(owner)}");
            }
            catch (Exception ex)
            {
                WriteLog(writer, $"Shell_TrayWnd owner lookup failed: {ex.GetType().Name}; PID={ownerPid}");
            }
        }
    }

    private static void WriteJobContext(TextWriter writer)
    {
        var inJob = IsProcessInJob(Process.GetCurrentProcess().Handle, IntPtr.Zero, out var isInJob);
        WriteLog(writer, $"IsProcessInJob returned {inJob}; inJob={isInJob}; error={(inJob ? 0 : Marshal.GetLastWin32Error())}");

        var uiOk = QueryInformationJobObject(IntPtr.Zero, JobObjectBasicUiRestrictions,
            out var uiRestrictions, sizeof(uint), out var uiLength);
        WriteLog(writer, $"Job UI restrictions query returned {uiOk}; flags=0x{uiRestrictions:X8}; bytes={uiLength}; error={(uiOk ? 0 : Marshal.GetLastWin32Error())}");

        var limits = new JobObjectExtendedLimitInformation();
        var limitsSize = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var memory = Marshal.AllocHGlobal(limitsSize);
        try
        {
            var limitOk = QueryInformationJobObject(IntPtr.Zero, JobObjectExtendedLimitInformation,
                memory, (uint)limitsSize, out var returned);
            if (limitOk)
                limits = Marshal.PtrToStructure<JobObjectExtendedLimitInformation>(memory);

            WriteLog(writer, $"Job extended limits query returned {limitOk}; flags=0x{limits.BasicLimitInformation.LimitFlags:X8}; bytes={returned}; error={(limitOk ? 0 : Marshal.GetLastWin32Error())}");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static Guid GetLibraryGuid()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine executable path.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(processPath + "_"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string SafeIdentityName()
    {
        try { return WindowsIdentity.GetCurrent().Name; }
        catch { return "(unavailable)"; }
    }

    private static string SafeProcessPath(Process process)
    {
        try { return process.MainModule?.FileName ?? "(unavailable)"; }
        catch { return "(unavailable)"; }
    }

    private static string GetUserObjectName(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return "(null)";

        GetUserObjectInformation(handle, 2, IntPtr.Zero, 0, out var required);
        if (required == 0)
            return $"(unavailable; error={Marshal.GetLastWin32Error()})";

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (!GetUserObjectInformation(handle, 2, buffer, required, out _))
                return $"(unavailable; error={Marshal.GetLastWin32Error()})";

            return Marshal.PtrToStringUni(buffer) ?? "(empty)";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetParentProcessId(IntPtr processHandle)
    {
        var status = NtQueryInformationProcess(processHandle, 0, out var info,
            Marshal.SizeOf<ProcessBasicInformation>(), out _);
        return status == 0 ? unchecked((int)info.InheritedFromUniqueProcessId.ToInt64()) : -status;
    }

    private static void WriteLog(TextWriter writer, string message) =>
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
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        IntPtr handle, int index, IntPtr information, uint length, out uint needed);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr jobHandle, int informationClass, out uint information, uint informationLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr jobHandle, int informationClass, IntPtr information, uint informationLength, out uint returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass, out ProcessBasicInformation processInformation,
        int processInformationLength, out int returnLength);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);
}
