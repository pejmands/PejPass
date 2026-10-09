using PejPass.Domain.Settings;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

public sealed class SessionPasswordCacheTests
{
    [Fact]
    public void CacheExpiresAtConfiguredTimeout()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cache = new SessionPasswordCache(settings, clock);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-test.pejpass");

        cache.Store(path, "test-master-password");
        Assert.True(cache.HasCacheFor(path));
        clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.True(cache.HasCacheFor(path));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cache.HasCacheFor(path));
    }

    [Fact]
    public void RestoringPasswordDoesNotExtendCacheLifetime()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cache = new SessionPasswordCache(settings, clock);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-no-refresh.pejpass");

        cache.Store(path, "test-master-password");
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(cache.TryRestore(path, out var restored));
        Assert.Equal("test-master-password", restored);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(cache.HasCacheFor(path));
    }

    [Fact]
    public void ThirdConsecutiveHelloAuthenticationFailureClearsCache()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 240 };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cache = new SessionPasswordCache(settings, clock);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-failures.pejpass");
        cache.Store(path, "test-master-password");

        Assert.False(cache.RecordHelloAuthenticationFailure());
        Assert.True(cache.HasCacheFor(path));
        Assert.False(cache.RecordHelloAuthenticationFailure());
        Assert.True(cache.HasCacheFor(path));
        Assert.True(cache.RecordHelloAuthenticationFailure());
        Assert.False(cache.HasCacheFor(path));
        Assert.Equal(3, cache.ConsecutiveHelloFailures);
    }

    [Fact]
    public void SuccessfulMasterPasswordStoreResetsHelloFailureCounter()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 240 };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cache = new SessionPasswordCache(settings, clock);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-reset.pejpass");
        cache.Store(path, "first-password");
        cache.RecordHelloAuthenticationFailure();
        cache.RecordHelloAuthenticationFailure();

        cache.Store(path, "second-password");

        Assert.Equal(0, cache.ConsecutiveHelloFailures);
        Assert.True(cache.TryRestore(path, out var restored));
        Assert.Equal("second-password", restored);
    }

    [Fact]
    public void ZeroTimeoutKeepsCacheUntilExplicitClear()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 0 };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cache = new SessionPasswordCache(settings, clock);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-until-exit.pejpass");

        cache.Store(path, "test-master-password");
        clock.Advance(TimeSpan.FromDays(30));
        Assert.True(cache.HasCacheFor(path));
        cache.Clear();
        Assert.False(cache.HasCacheFor(path));
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialTime;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
