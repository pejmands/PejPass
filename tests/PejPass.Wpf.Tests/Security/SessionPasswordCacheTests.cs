using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Security;

public sealed class SessionPasswordCacheTests
{
    [Fact]
    public void CacheExpiresAtConfiguredTimeout()
    {
        var settings = new AppSettings { WindowsHelloTimeoutMinutes = 15 };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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

    private sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialTime;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
