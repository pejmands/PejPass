using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Services;

public sealed class ClipboardServiceTests
{
    [Fact(Timeout = 15000)]
    public async Task CopyWithTimeout_WhenClipboardStillContainsCopiedText_ClearsClipboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var clock = new ManualTimeProvider();
        var service = new ClipboardService(clipboard, dispatcher, clock);

        service.CopyWithTimeout("Secret123!", TimeSpan.FromMinutes(1));

        clock.Advance(TimeSpan.FromMinutes(1));
        await dispatcher.Invoked.Task.WaitAsync(cancellationToken);

        Assert.True(clipboard.ClearCalled);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void ClearIfOwned_WhenClipboardStillContainsCopiedText_ClearsClipboard()
    {
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var clock = new ManualTimeProvider();
        var service = new ClipboardService(clipboard, dispatcher, clock);

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
        var clock = new ManualTimeProvider();
        var service = new ClipboardService(clipboard, dispatcher, clock);

        service.CopyWithTimeout("Secret123!", TimeSpan.FromMinutes(1));
        clipboard.SetText("User copied text");

        service.ClearIfOwned();

        Assert.False(clipboard.ClearCalled);
        Assert.Equal("User copied text", clipboard.Text);
    }

    [Fact(Timeout = 15000)]
    public async Task CopyWithTimeout_WhenClipboardChangedByUser_DoesNotClearClipboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clipboard = new FakeClipboardProvider();
        var dispatcher = new FakeUiDispatcher();
        var clock = new ManualTimeProvider();
        var service = new ClipboardService(clipboard, dispatcher, clock);

        service.CopyWithTimeout("Secret123!", TimeSpan.FromMinutes(1));
        clipboard.SetText("User copied text");

        clock.Advance(TimeSpan.FromMinutes(1));
        await dispatcher.Invoked.Task.WaitAsync(cancellationToken);

        Assert.False(clipboard.ClearCalled);
        Assert.Equal("User copied text", clipboard.Text);
    }

    private sealed class FakeClipboardProvider : IClipboardProvider
    {
        public string? Text { get; private set; }

        public bool ClearCalled { get; private set; }

        public void SetText(string text) => Text = text;

        public bool ContainsText() => Text is not null;

        public string GetText() => Text ?? string.Empty;

        public void Clear()
        {
            ClearCalled = true;
            Text = null;
        }
    }

    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public TaskCompletionSource<bool> Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Invoke(Action action)
        {
            action();
            Invoked.TrySetResult(true);
        }
    }
}
