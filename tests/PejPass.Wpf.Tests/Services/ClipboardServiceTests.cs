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
        public TaskCompletionSource Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Invoke(Action action)
        {
            action();
            Invoked.TrySetResult();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
                return _utcNow;
        }

        public override long GetTimestamp() => GetUtcNow().UtcDateTime.Ticks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(this, callback, state);

            lock (_sync)
                _timers.Add(timer);

            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));

            List<ManualTimer> dueTimers;

            lock (_sync)
            {
                _utcNow += elapsed;
                dueTimers = _timers.Where(timer => timer.IsDue(_utcNow)).ToList();

                foreach (var timer in dueTimers)
                    timer.MarkTriggered(_utcNow);
            }

            foreach (var timer in dueTimers)
                timer.Invoke();
        }

        private bool ChangeTimer(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(period));

            lock (_sync)
            {
                if (timer.IsDisposed)
                    return false;

                timer.SetSchedule(
                    dueTime == Timeout.InfiniteTimeSpan ? null : _utcNow + dueTime,
                    period);
                return true;
            }
        }

        private void DisposeTimer(ManualTimer timer)
        {
            lock (_sync)
            {
                timer.MarkDisposed();
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset? _dueAt;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;

            public bool IsDisposed { get; private set; }

            public bool IsDue(DateTimeOffset now) =>
                !IsDisposed && _dueAt is { } dueAt && dueAt <= now;

            public void MarkTriggered(DateTimeOffset now)
            {
                if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
                {
                    _dueAt = null;
                    return;
                }

                do
                {
                    _dueAt = _dueAt!.Value + _period;
                }
                while (_dueAt <= now);
            }

            public void SetSchedule(DateTimeOffset? dueAt, TimeSpan period)
            {
                _dueAt = dueAt;
                _period = period;
            }

            public void MarkDisposed()
            {
                IsDisposed = true;
                _dueAt = null;
            }

            public void Invoke() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                owner.ChangeTimer(this, dueTime, period);

            public void Dispose() => owner.DisposeTimer(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
