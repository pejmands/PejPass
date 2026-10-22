using PejPass.Wpf.Controls;

namespace PejPass.Wpf.Tests.Controls;

public sealed class HorizontalSelectionScrollBehaviorTests
{
    [Fact]
    public void AttachWithoutScrollViewer_RetriesOnlyUpToTheConfiguredLimit()
    {
        for (var attempt = 0; attempt < HorizontalSelectionScrollBehavior.MaxAttachAttempts - 1; attempt++)
            Assert.True(HorizontalSelectionScrollBehavior.ShouldRetryAttach(attempt));

        Assert.False(HorizontalSelectionScrollBehavior.ShouldRetryAttach(
            HorizontalSelectionScrollBehavior.MaxAttachAttempts - 1));
    }
}
