using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Security;

public sealed class SessionPasswordCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CacheExpiresAtConfiguredTimeout()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x11);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-test.pejpass");

        cache.Store(path, material);
        Assert.True(cache.HasCacheFor(path));
        clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.True(cache.HasCacheFor(path));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cache.HasCacheFor(path));
    }

    [Fact]
    public void RestoringKeyMaterialDoesNotExtendCacheLifetime()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x12);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-no-refresh.pejpass");

        cache.Store(path, material);
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(cache.TryRestore(path, out var restored));
        Assert.NotNull(restored);
        using (restored)
        {
            Assert.Equal(material.Key.ToArray(), restored.Key.ToArray());
            Assert.Equal(material.Salt.ToArray(), restored.Salt.ToArray());
            Assert.Equal(material.KdfParameters, restored.KdfParameters);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(cache.HasCacheFor(path));
    }

    [Fact]
    public void ThirdConsecutiveHelloAuthenticationFailureClearsCache()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 240 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x13);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-failures.pejpass");
        cache.Store(path, material);

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
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var firstMaterial = CreateMaterial(0x14);
        using var secondMaterial = CreateMaterial(0x15);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-reset.pejpass");
        cache.Store(path, firstMaterial);
        cache.RecordHelloAuthenticationFailure();
        cache.RecordHelloAuthenticationFailure();

        cache.Store(path, secondMaterial);

        Assert.Equal(0, cache.ConsecutiveHelloFailures);
        Assert.True(cache.TryRestore(path, out var restored));
        Assert.NotNull(restored);
        using (restored)
            Assert.Equal(secondMaterial.Key.ToArray(), restored.Key.ToArray());
    }

    [Fact]
    public void ZeroTimeoutKeepsCacheUntilExplicitClear()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 0 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x16);
        var path = Path.Combine(Path.GetTempPath(), "PejPass-cache-until-exit.pejpass");

        cache.Store(path, material);
        clock.Advance(TimeSpan.FromDays(30));
        Assert.True(cache.HasCacheFor(path));
        cache.Clear();
        Assert.False(cache.HasCacheFor(path));
    }

    [Fact]
    public void KeyMaterialPayloadRejectsInvalidData()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        Assert.Throws<InvalidDataException>(() => VaultKeyMaterial.FromByteArray(payload));
    }

    [Fact]
    public void Store_SchedulesOneExpiryTimerForTheConfiguredLifetime()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x21);

        cache.Store(VaultPath("timer-schedule"), material);

        var due = Assert.Single(clock.PendingDueIn());
        Assert.Equal(TimeSpan.FromMinutes(15), due);
    }

    [Fact]
    public void Store_WithZeroTimeout_SchedulesNoTimer()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 0 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x22);

        cache.Store(VaultPath("timer-none"), material);

        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void StoringAgain_ReplacesTheTimerInsteadOfAccumulatingThem()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var first = CreateMaterial(0x23);
        using var second = CreateMaterial(0x24);
        var path = VaultPath("timer-replace");

        cache.Store(path, first);
        clock.Advance(TimeSpan.FromMinutes(10));
        cache.Store(path, second);

        Assert.Equal(1, clock.ActiveTimerCount);
        Assert.Equal(TimeSpan.FromMinutes(15), Assert.Single(clock.PendingDueIn()));
    }

    [Fact]
    public void FiredTimer_ClearsTheCache_EvenWhenNobodyChecksExpiry()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x25);
        var path = VaultPath("timer-fires");

        cache.Store(path, material);

        // The clock reaches the lifetime: only the timer callback runs, nothing checks the cache.
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(0, clock.ActiveTimerCount);

        // Without the callback, moving time back would make expiry checks report the cache as valid.
        clock.SetUtcNow(Start);
        Assert.False(cache.HasCacheFor(path));
        Assert.False(cache.TryRestore(path, out _));
    }

    [Fact]
    public void RefreshExpiry_ReschedulesWithTheNewTimeoutAndTheRemainingLifetime()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x26);

        cache.Store(VaultPath("timer-refresh"), material);
        clock.Advance(TimeSpan.FromMinutes(5));

        settings.WindowsHelloTimeoutMinutes = 60;
        cache.RefreshExpiry();

        Assert.Equal(1, clock.ActiveTimerCount);
        Assert.Equal(TimeSpan.FromMinutes(55), Assert.Single(clock.PendingDueIn()));
    }

    [Fact]
    public void RefreshExpiry_WithShorterTimeoutThanElapsed_ClearsTheCacheImmediately()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 60 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x27);
        var path = VaultPath("timer-shorter");

        cache.Store(path, material);
        clock.Advance(TimeSpan.FromMinutes(20));

        settings.WindowsHelloTimeoutMinutes = 15;
        cache.RefreshExpiry();

        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.False(cache.HasCacheFor(path));
    }

    [Fact]
    public void Clear_DisposesTheExpiryTimer()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x28);

        cache.Store(VaultPath("timer-clear"), material);
        cache.Clear();

        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void ThirdHelloFailure_DisposesTheExpiryTimer()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 240 };
        var clock = new ManualTimeProvider(Start);
        using var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x29);

        cache.Store(VaultPath("timer-failures"), material);
        cache.RecordHelloAuthenticationFailure();
        cache.RecordHelloAuthenticationFailure();
        Assert.Equal(1, clock.ActiveTimerCount);

        Assert.True(cache.RecordHelloAuthenticationFailure());

        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void Dispose_DisposesTheExpiryTimer()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(Start);
        var cache = new SessionPasswordCache(settings, clock);
        using var material = CreateMaterial(0x2A);

        cache.Store(VaultPath("timer-dispose"), material);
        cache.Dispose();

        Assert.Equal(0, clock.ActiveTimerCount);
    }

    private static string VaultPath(string name) =>
        Path.Combine(Path.GetTempPath(), $"PejPass-cache-{name}.pejpass");

    private static VaultKeyMaterial CreateMaterial(byte marker)
    {
        var key = Enumerable.Repeat(marker, 32).ToArray();
        var salt = Enumerable.Repeat((byte)(marker + 1), 16).ToArray();

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
