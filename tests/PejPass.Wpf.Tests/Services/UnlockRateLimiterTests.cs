using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Services;

public sealed class UnlockRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstFourFailures_DoNotLockOut()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
    }

    [Fact]
    public void FifthFailure_LocksOutForBaseDuration_ThenReleases()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts + 1; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.LockedOut, limiter.TryBegin(out var wait));
        Assert.Equal(UnlockRateLimiter.BaseLockout, wait);

        clock.Advance(UnlockRateLimiter.BaseLockout - TimeSpan.FromSeconds(1));
        Assert.Equal(UnlockGate.LockedOut, limiter.TryBegin(out _));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
    }

    [Fact]
    public void Lockout_DoublesWithEachFurtherFailure()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts + 2; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.LockedOut, limiter.TryBegin(out var wait));
        Assert.Equal(UnlockRateLimiter.BaseLockout * 2, wait);
    }

    [Fact]
    public void Lockout_IsCappedAtMaximum()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < 20; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.LockedOut, limiter.TryBegin(out var wait));
        Assert.Equal(UnlockRateLimiter.MaxLockout, wait);
    }

    [Fact]
    public void Success_ResetsFailureCount()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
        limiter.Complete(UnlockOutcome.Success);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
    }

    [Fact]
    public void AbortedAttempt_IsNotCountedAsFailure()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
            limiter.Complete(UnlockOutcome.Aborted);
        }

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
    }

    [Fact]
    public void SecondAttemptWhileOneIsInFlight_IsBusy_UntilCompleted()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
        Assert.Equal(UnlockGate.Busy, limiter.TryBegin(out _));

        limiter.Complete(UnlockOutcome.Aborted);
        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
    }

    [Fact]
    public void FailuresAreForgotten_AfterTheMemoryWindow()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts + 1; i++)
            Fail(limiter, clock);

        Assert.Equal(UnlockGate.LockedOut, limiter.TryBegin(out _));

        clock.Advance(UnlockRateLimiter.FailureMemory);

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
        limiter.Complete(UnlockOutcome.Failure);

        Assert.Equal(UnlockGate.Allowed, limiter.TryBegin(out _));
    }

    [Fact]
    public async Task Endpoint_WhenLockedOut_ReturnsRateLimited_WithoutTouchingUi()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UnlockRateLimiter(clock);

        for (var i = 0; i < UnlockRateLimiter.FreeAttempts + 1; i++)
            Fail(limiter, clock);

        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session) { UnlockLimiter = limiter };

        var response = await service.CreateUnlockVaultResponseAsync(
            """{"type":"unlockVault","protocolVersion":1,"masterPassword":"anything"}""");

        Assert.Equal("error", response.Type);
        Assert.Equal("unlock_rate_limited", response.Code);
        Assert.Equal((int)UnlockRateLimiter.BaseLockout.TotalSeconds, response.RetryAfterSeconds);
        Assert.Null(response.Password);
    }

    private static void Fail(UnlockRateLimiter limiter, ManualTimeProvider clock)
    {
        var gate = limiter.TryBegin(out var wait);

        if (gate == UnlockGate.LockedOut)
        {
            clock.Advance(wait);
            gate = limiter.TryBegin(out _);
        }

        Assert.Equal(UnlockGate.Allowed, gate);
        limiter.Complete(UnlockOutcome.Failure);
    }
}
