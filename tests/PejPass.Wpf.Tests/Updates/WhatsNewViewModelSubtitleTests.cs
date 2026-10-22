using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.Updates;

public sealed class WhatsNewViewModelSubtitleTests
{
    [Theory]
    [InlineData(1, true, "1 release")]
    [InlineData(3, true, "3 releases")]
    [InlineData(1, false, "1 release (cached)")]
    [InlineData(3, false, "3 releases (cached)")]
    public void FormatSubtitle_UsesExplicitNetworkSource(
        int count,
        bool isFromNetwork,
        string expected)
    {
        Assert.Equal(
            expected,
            WhatsNewViewModel.FormatSubtitle(count, isFromNetwork));
    }
}
