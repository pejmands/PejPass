using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class WindowChromeTests
{
    [Fact(Timeout = 15000)]
    public void PrepareCustomChrome_UsesNativeRoundedCornersWhenSupported()
    {
        WpfTestHost.Run(_ =>
        {
            var window = new Window
            {
                Width = 400,
                Height = 300,
                Content = new Grid()
            };

            App.PrepareCustomChrome(window);

            var root = Assert.IsType<Border>(window.Content);
            var handle = new WindowInteropHelper(window).EnsureHandle();

            Assert.NotEqual(IntPtr.Zero, handle);

            var expectedRadius = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                ? 0
                : 10;

            var chrome = WindowChrome.GetWindowChrome(window);

            Assert.NotNull(chrome);
            Assert.Equal(expectedRadius, chrome!.CornerRadius.TopLeft);
            Assert.Equal(expectedRadius, root.CornerRadius.TopLeft);

            var expectedGlassFrame = expectedRadius == 0
                ? new Thickness(0.00001)
                : new Thickness(0);
            Assert.Equal(expectedGlassFrame, chrome.GlassFrameThickness);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
