using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace PejPass.Wpf.Services;

internal static class WindowsClipboardWriter
{
    private const uint ClipboardUnicodeText = 13;
    private const uint GlobalMemoryMoveable = 0x0002;
    private const int ClipboardOpenAttempts = 10;
    private const int ClipboardRetryDelayMilliseconds = 20;

    private const string ExcludeClipboardContentFromMonitorProcessing =
        "ExcludeClipboardContentFromMonitorProcessing";

    private const string CanIncludeInClipboardHistory =
        "CanIncludeInClipboardHistory";

    private const string CanUploadToCloudClipboard =
        "CanUploadToCloudClipboard";

    internal readonly record struct ClipboardPayload(string FormatName, byte[] Bytes);

    internal static IReadOnlyList<ClipboardPayload> CreatePayloads(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var zeroDword = new byte[sizeof(uint)];

        return
        [
            new(DataFormats.UnicodeText, Encoding.Unicode.GetBytes(text + '\0')),
            new(ExcludeClipboardContentFromMonitorProcessing, (byte[])zeroDword.Clone()),
            new(CanIncludeInClipboardHistory, (byte[])zeroDword.Clone()),
            new(CanUploadToCloudClipboard, (byte[])zeroDword.Clone())
        ];
    }

    internal static void SetText(string text)
    {
        var payloads = CreatePayloads(text);
        var nativeItems = new List<NativeClipboardItem>(payloads.Count);

        try
        {
            foreach (var payload in payloads)
            {
                var format = ResolveFormat(payload.FormatName);
                var memory = AllocateGlobalMemory(payload.Bytes);
                nativeItems.Add(new NativeClipboardItem(format, memory));
            }

            var ownerWindow = GetOwnerWindow();
            OpenClipboardWithRetry(ownerWindow);

            try
            {
                if (!EmptyClipboard())
                    throw CreateClipboardException("Could not clear the clipboard.");

                for (var index = 0; index < nativeItems.Count; index++)
                {
                    var item = nativeItems[index];
                    if (SetClipboardData(item.Format, item.Memory) == IntPtr.Zero)
                        throw CreateClipboardException($"Could not set clipboard format {item.Format}.");

                    // Windows owns the global memory after SetClipboardData succeeds.
                    nativeItems[index] = item with { Memory = IntPtr.Zero };
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            foreach (var item in nativeItems)
            {
                if (item.Memory != IntPtr.Zero)
                    GlobalFree(item.Memory);
            }
        }
    }

    private static uint ResolveFormat(string formatName)
    {
        if (formatName == DataFormats.UnicodeText)
            return ClipboardUnicodeText;

        var format = RegisterClipboardFormat(formatName);
        if (format == 0)
            throw CreateClipboardException($"Could not register clipboard format {formatName}.");

        return format;
    }

    private static IntPtr AllocateGlobalMemory(byte[] bytes)
    {
        var memory = GlobalAlloc(GlobalMemoryMoveable, new UIntPtr((uint)bytes.Length));
        if (memory == IntPtr.Zero)
            throw CreateClipboardException("Could not allocate clipboard memory.");

        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero)
                throw CreateClipboardException("Could not lock clipboard memory.");

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            return memory;
        }
        catch
        {
            GlobalFree(memory);
            throw;
        }
    }

    private static IntPtr GetOwnerWindow()
    {
        var application = Application.Current;
        if (application is not null)
        {
            var windows = application.Windows
                .OfType<Window>()
                .OrderByDescending(window => window.IsActive)
                .ThenByDescending(window => window.IsVisible);

            foreach (var window in windows)
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                    return handle;
            }
        }

        var activeWindow = GetActiveWindow();
        if (activeWindow != IntPtr.Zero)
            return activeWindow;

        throw new InvalidOperationException(
            "Copying to the clipboard requires an existing PejPass window.");
    }

    private static void OpenClipboardWithRetry(IntPtr ownerWindow)
    {
        for (var attempt = 0; attempt < ClipboardOpenAttempts; attempt++)
        {
            if (OpenClipboard(ownerWindow))
                return;

            if (attempt < ClipboardOpenAttempts - 1)
                Thread.Sleep(ClipboardRetryDelayMilliseconds);
        }

        throw CreateClipboardException("The clipboard is busy or unavailable.");
    }

    private static Win32Exception CreateClipboardException(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    private sealed record NativeClipboardItem(uint Format, IntPtr Memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
