using PejPass.Domain.Settings;

namespace PejPass.Domain.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void OnlineFaviconFetching_IsDisabledByDefault()
    {
        Assert.False(new AppSettings().OnlineFaviconFetchingEnabled);
    }

    [Fact]
    public void FastDragScrollTip_IsUnseenByDefault()
    {
        Assert.False(new AppSettings().HasSeenFastDragScrollTip);
    }
}
