namespace PejPass.Wpf.Services;

internal enum UnlockGate
{
    Allowed,
    LockedOut,
    Busy
}

internal enum UnlockOutcome
{
    Success,
    Failure,
    Aborted
}

/// <summary>
/// In-memory rate limiting for master-password unlock attempts initiated by the browser.
/// </summary>
internal sealed class UnlockRateLimiter(TimeProvider timeProvider)
{
    internal const int FreeAttempts = 4;
    internal static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Lock _sync = new();
    private int _failures;
    private bool _inFlight;
    private DateTimeOffset _lockedUntil;
    private DateTimeOffset _lastFailure;

    /// <summary>
    /// If Allowed is returned, the caller must call Complete from a finally block.
    /// </summary>
    public UnlockGate TryBegin(out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();

            if (_failures > 0 && now - _lastFailure >= FailureMemory)
            {
                _failures = 0;
                _lockedUntil = default;
            }

            if (_inFlight)
            {
                retryAfter = TimeSpan.FromSeconds(1);
                return UnlockGate.Busy;
            }

            if (now < _lockedUntil)
            {
                retryAfter = _lockedUntil - now;
                return UnlockGate.LockedOut;
            }

            _inFlight = true;
            retryAfter = TimeSpan.Zero;
            return UnlockGate.Allowed;
        }
    }

    public void Complete(UnlockOutcome outcome)
    {
        lock (_sync)
        {
            _inFlight = false;

            switch (outcome)
            {
                case UnlockOutcome.Success:
                    _failures = 0;
                    _lockedUntil = default;
                    break;

                case UnlockOutcome.Failure:
                    var now = _time.GetUtcNow();
                    _failures++;
                    _lastFailure = now;
                    _lockedUntil = now + LockoutFor(_failures);
                    break;
            }
        }
    }

    private static TimeSpan LockoutFor(int failures)
    {
        if (failures <= FreeAttempts)
            return TimeSpan.Zero;

        var exponent = Math.Min(failures - FreeAttempts - 1, 10);
        var seconds = BaseLockout.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxLockout.TotalSeconds));
    }
}
