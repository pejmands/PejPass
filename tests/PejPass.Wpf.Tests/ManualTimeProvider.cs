namespace PejPass.Wpf.Tests;

/// <summary>
/// Deterministic clock with real timer semantics: timers only fire when Advance
/// moves the clock past their due time. Shared by the tests that depend on time.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset? initialTime = null) : TimeProvider
{
    private readonly Lock _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = initialTime ?? DateTimeOffset.UnixEpoch;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _utcNow;
    }

    public override long GetTimestamp() => GetUtcNow().UtcDateTime.Ticks;

    /// <summary>Timers that were created and not disposed yet.</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_sync)
                return _timers.Count;
        }
    }

    /// <summary>For each scheduled timer, the time remaining until it is due.</summary>
    public IReadOnlyList<TimeSpan> PendingDueIn()
    {
        lock (_sync)
        {
            return [.. _timers
                .Where(timer => timer.DueAt.HasValue)
                .Select(timer => timer.DueAt!.Value - _utcNow)];
        }
    }

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

    /// <summary>Moves the clock forward and invokes each timer that became due.</summary>
    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        List<ManualTimer> dueTimers;

        lock (_sync)
        {
            _utcNow += elapsed;
            dueTimers = [.. _timers.Where(timer => timer.IsDue(_utcNow))];

            foreach (var timer in dueTimers)
                timer.MarkTriggered(_utcNow);
        }

        // Callbacks run outside the lock, as on a real thread pool.
        foreach (var timer in dueTimers)
            timer.Invoke();
    }

    /// <summary>
    /// Sets the clock without running timers. Moving time backwards is intentionally
    /// available only for tests that need to isolate a timer callback from expiry checks.
    /// </summary>
    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_sync)
            _utcNow = value;
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

        public DateTimeOffset? DueAt => _dueAt;

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
