using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PejPass.Wpf.Services;

/// <summary>
/// Gives the login window its own Windows taskbar identity so a pinned
/// application shortcut cannot override its monochrome taskbar-group icon.
/// </summary>
internal static partial class LoginTaskbarIdentityService
{
    private const string LoginAppUserModelId = "PejMands.PejPass.Login";
    private const ushort VariantTypeEmpty = 0;
    private const ushort VariantTypeLPWStr = 31;

    private static readonly Guid PropertyStoreIid =
        new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    private static readonly PropertyKey RelaunchIconKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        3);

    private static readonly PropertyKey AppUserModelIdKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    internal static void Configure(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = IntPtr.Zero;
        var identityApplied = false;

        void ApplyIdentity(object? sender, EventArgs args)
        {
            window.SourceInitialized -= ApplyIdentity;

            hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "PejPassLocked.ico");

            identityApplied = TryApply(hwnd, iconPath);
        }

        window.SourceInitialized += ApplyIdentity;

        // Closing can be canceled when the app is configured to hide to the tray.
        // This handler is registered after LoginWindow's own Closing handler.
        window.Closing += (_, e) =>
        {
            if (e.Cancel || !identityApplied || hwnd == IntPtr.Zero)
                return;

            TryClear(hwnd);
            identityApplied = false;
        };

        // Handle an already-created HWND as well (for reuse scenarios).
        var existingHandle = new WindowInteropHelper(window).Handle;
        if (existingHandle != IntPtr.Zero)
            ApplyIdentity(null, EventArgs.Empty);
    }

    private static bool TryApply(IntPtr hwnd, string iconPath)
    {
        if (!File.Exists(iconPath))
        {
            Trace.WriteLine($"Login taskbar icon was not found: {iconPath}");
            return false;
        }

        IPropertyStore? store = null;
        IntPtr storePointer = IntPtr.Zero;
        var iconWasSet = false;
        var appIdWasSet = false;

        try
        {
            var hr = SHGetPropertyStoreForWindow(hwnd, in PropertyStoreIid, out storePointer);
            Marshal.ThrowExceptionForHR(hr);

            if (storePointer == IntPtr.Zero)
                return false;

            store = (IPropertyStore)Marshal.GetObjectForIUnknown(storePointer);
            Marshal.Release(storePointer);
            storePointer = IntPtr.Zero;

            // Set the icon before the AppUserModelID. Setting the ID notifies
            // the taskbar to refresh the window's grouping and icon metadata.
            SetString(store, RelaunchIconKey, $"{iconPath},0");
            iconWasSet = true;

            SetString(store, AppUserModelIdKey, LoginAppUserModelId);
            appIdWasSet = true;

            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not set the login taskbar identity: {ex}");
            if (store is not null)
            {
                if (appIdWasSet)
                    TrySetEmpty(store, AppUserModelIdKey);
                if (iconWasSet)
                    TrySetEmpty(store, RelaunchIconKey);
            }

            return false;
        }
        finally
        {
            if (storePointer != IntPtr.Zero)
                Marshal.Release(storePointer);

            if (store is not null)
                Marshal.ReleaseComObject(store);
        }
    }

    private static void TryClear(IntPtr hwnd)
    {
        IPropertyStore? store = null;
        IntPtr storePointer = IntPtr.Zero;

        try
        {
            var hr = SHGetPropertyStoreForWindow(hwnd, in PropertyStoreIid, out storePointer);
            Marshal.ThrowExceptionForHR(hr);

            if (storePointer == IntPtr.Zero)
                return;

            store = (IPropertyStore)Marshal.GetObjectForIUnknown(storePointer);
            Marshal.Release(storePointer);
            storePointer = IntPtr.Zero;

            TrySetEmpty(store, AppUserModelIdKey);
            TrySetEmpty(store, RelaunchIconKey);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not clear the login taskbar identity: {ex}");
        }
        finally
        {
            if (storePointer != IntPtr.Zero)
                Marshal.Release(storePointer);

            if (store is not null)
                Marshal.ReleaseComObject(store);
        }
    }

    private static void SetString(IPropertyStore store, PropertyKey key, string value)
    {
        var variant = new PropVariant
        {
            VariantType = VariantTypeLPWStr,
            PointerValue = Marshal.StringToCoTaskMemUni(value)
        };

        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref variant));
        }
        finally
        {
            _ = PropVariantClear(ref variant);
        }
    }

    private static void TrySetEmpty(IPropertyStore store, PropertyKey key)
    {
        var variant = new PropVariant { VariantType = VariantTypeEmpty };

        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref variant));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not clear a taskbar window property: {ex}");
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPropertyStoreForWindow")]
    private static partial int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        in Guid riid,
        out IntPtr propertyStore);

    [LibraryImport("ole32.dll", EntryPoint = "PropVariantClear")]
    private static partial int PropVariantClear(ref PropVariant variant);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public IntPtr PointerValue;
    }
}
