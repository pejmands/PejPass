using Microsoft.Extensions.DependencyInjection;
using PejPass.Application.Services;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PejPass.Wpf.Services;

/// <summary>
/// Displays the vault lock state using the native Windows taskbar overlay icon.
/// </summary>
internal static partial class TaskbarOverlayService
{
    private static ITaskbarList3? _taskbarList;
    private static Icon? _lockedOverlayIcon;

    internal static void TrackWindow(Window window)
    {
        window.SourceInitialized -= OnWindowSourceInitialized;
        window.SourceInitialized += OnWindowSourceInitialized;
        window.Closed -= OnWindowClosed;
        window.Closed += OnWindowClosed;

        UpdateWindowOverlay(window, IsVaultUnlocked());
    }

    internal static void UpdateWindowOverlay(Window window, bool isUnlocked)
    {
        if (SystemTrayService.IsHostWindow(window) || !window.ShowInTaskbar)
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        try
        {
            var taskbar = GetTaskbarList();
            if (taskbar is null)
                return;

            var iconHandle = isUnlocked ? IntPtr.Zero : GetLockedOverlayIcon().Handle;
            taskbar.SetOverlayIcon(
                handle,
                iconHandle,
                isUnlocked ? null : "Vault locked");
        }
        catch
        {
            // Taskbar decoration is best-effort and must never prevent vault use.
        }
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
            UpdateWindowOverlay(window, IsVaultUnlocked());
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        window.SourceInitialized -= OnWindowSourceInitialized;
        window.Closed -= OnWindowClosed;
    }

    private static bool IsVaultUnlocked() =>
        App.Services?.GetService<VaultSession>()?.IsActive == true;

    private static ITaskbarList3? GetTaskbarList()
    {
        if (_taskbarList is not null)
            return _taskbarList;

        try
        {
            var taskbar = (ITaskbarList3)new TaskbarListComObject();
            taskbar.HrInit();
            _taskbarList = taskbar;
            return taskbar;
        }
        catch
        {
            return null;
        }
    }

    private static Icon GetLockedOverlayIcon() =>
        _lockedOverlayIcon ??= CreateLockedOverlayIcon();

    private static Icon CreateLockedOverlayIcon()
    {
        const int size = 32;

        using var colorBitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(colorBitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var badgeBrush = new SolidBrush(Color.FromArgb(35, 39, 47));
            using var badgePen = new Pen(Color.White, 1.5f);
            graphics.FillEllipse(badgeBrush, 1, 1, 30, 30);
            graphics.DrawEllipse(badgePen, 1, 1, 30, 30);

            using var lockPen = new Pen(Color.White, 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawArc(lockPen, 10, 5, 12, 14, 180, -180);
            graphics.DrawLine(lockPen, 10, 12, 10, 17);
            graphics.DrawLine(lockPen, 22, 12, 22, 17);

            using var lockBrush = new SolidBrush(Color.White);
            graphics.FillRoundedRectangle(lockBrush, 7, 15, 18, 12, 3);

            using var keyholeBrush = new SolidBrush(Color.FromArgb(35, 39, 47));
            graphics.FillEllipse(keyholeBrush, 14, 17, 4, 4);
            graphics.FillRectangle(keyholeBrush, 15, 20, 2, 4);
        }

        using var maskBitmap = new Bitmap(size, size, PixelFormat.Format1bppIndexed);
        var maskRectangle = new Rectangle(0, 0, size, size);
        var maskData = maskBitmap.LockBits(
            maskRectangle,
            ImageLockMode.WriteOnly,
            PixelFormat.Format1bppIndexed);

        try
        {
            var stride = Math.Abs(maskData.Stride);
            var bytes = Enumerable.Repeat((byte)0xFF, stride * size).ToArray();

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (colorBitmap.GetPixel(x, y).A >= 128)
                        bytes[y * stride + x / 8] &= (byte)~(0x80 >> (x % 8));
                }
            }

            Marshal.Copy(bytes, 0, maskData.Scan0, bytes.Length);
        }
        finally
        {
            maskBitmap.UnlockBits(maskData);
        }

        var colorHandle = colorBitmap.GetHbitmap();
        var maskHandle = maskBitmap.GetHbitmap();
        try
        {
            var iconInfo = new IconInfo
            {
                IsIcon = 1,
                MaskBitmap = maskHandle,
                ColorBitmap = colorHandle
            };

            var handle = CreateIconIndirect(ref iconInfo);
            if (handle == IntPtr.Zero)
                throw new ExternalException("Could not create the taskbar lock overlay icon.");

            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        finally
        {
            DeleteObject(colorHandle);
            DeleteObject(maskHandle);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreateIconIndirect(ref IconInfo iconInfo);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int DestroyIcon(IntPtr iconHandle);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int DeleteObject(IntPtr objectHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private class TaskbarListComObject
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(
            IntPtr hwnd,
            IntPtr iconHandle,
            [MarshalAs(UnmanagedType.LPWStr)] string? description);
        void SetThumbnailTooltip(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPWStr)] string? tooltip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr rectangle);
    }
}
