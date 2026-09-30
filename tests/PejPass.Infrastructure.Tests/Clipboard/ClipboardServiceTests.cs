using PejPass.Wpf.Services;

namespace PejPass.Infrastructure.Tests.Clipboard;

public sealed class ClipboardServiceTests
{
    [Fact]
    public async Task CopyWithTimeout_WhenClipboardStillContainsCopiedText_ClearsClipboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var service = new ClipboardService(clipboard, dispatcher);

        service.CopyWithTimeout(
            "Secret123!",
            TimeSpan.FromMilliseconds(50));

        await Task.Delay(150, cancellationToken);

        Assert.True(clipboard.ClearCalled);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void ClearIfOwned_WhenClipboardStillContainsCopiedText_ClearsClipboard()
    {
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var service = new ClipboardService(clipboard, dispatcher);

        service.CopyWithTimeout("Secret123!", TimeSpan.FromMinutes(1));

        service.ClearIfOwned();

        Assert.True(clipboard.ClearCalled);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void ClearIfOwned_WhenClipboardChangedByUser_DoesNotClearClipboard()
    {
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var service = new ClipboardService(clipboard, dispatcher);

        service.CopyWithTimeout("Secret123!", TimeSpan.FromMinutes(1));
        clipboard.SetText("User copied text");

        service.ClearIfOwned();

        Assert.False(clipboard.ClearCalled);
        Assert.Equal("User copied text", clipboard.Text);
    }

    [Fact]
    public async Task CopyWithTimeout_WhenClipboardChangedByUser_DoesNotClearClipboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var service = new ClipboardService(clipboard, dispatcher);

        service.CopyWithTimeout(
            "Secret123!",
            TimeSpan.FromMilliseconds(50));

        clipboard.SetText("User copied text");

        await Task.Delay(150, cancellationToken);

        Assert.False(clipboard.ClearCalled);
        Assert.Equal("User copied text", clipboard.Text);
    }

    private sealed class FakeClipboardProvider : IClipboardProvider
    {
        public string? Text { get; private set; }

        public bool ClearCalled { get; private set; }

        public void SetText(string text)
        {
            Text = text;
        }

        public bool ContainsText()
        {
            return Text is not null;
        }

        public string GetText()
        {
            return Text ?? string.Empty;
        }

        public void Clear()
        {
            ClearCalled = true;
            Text = null;
        }
    }

    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action)
        {
            action();
        }
    }
}
