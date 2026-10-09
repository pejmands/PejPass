using System.Security.Cryptography;
using System.Text;
using PejPass.Wpf.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PejPass.Wpf.Tests;

[CollectionDefinition("FaviconService", DisableParallelization = true)]
public sealed class FaviconServiceCollection
{
    public const string Name = "FaviconService";
}

[Collection(FaviconServiceCollection.Name)]
public sealed class FaviconServiceTests
{
    private static readonly byte[] TinyPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void DiskCache_PrunesOldestFilesWhenFileCountExceedsLimit()
    {
        var directory = CreateTempDirectory();

        try
        {
            for (var i = 0; i < 4098; i++)
            {
                var path = Path.Combine(directory, $"{i:D3}.bin");
                File.WriteAllBytes(path, [1]);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
            }

            PruneDiskCache(directory);

            var files = Directory.GetFiles(directory, "*.bin");
            Assert.Equal(4096, files.Length);
            Assert.False(File.Exists(Path.Combine(directory, "000.bin")));
            Assert.False(File.Exists(Path.Combine(directory, "001.bin")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiskCache_PrunesOldestFilesWhenSizeExceedsLimit()
    {
        var directory = CreateTempDirectory();

        try
        {
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(directory, $"{i:D3}.bin");
                File.WriteAllBytes(path, new byte[32 * 1024 * 1024]);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
            }

            PruneDiskCache(directory);

            var files = Directory.GetFiles(directory, "*.bin");
            var totalBytes = files.Sum(path => new FileInfo(path).Length);

            Assert.Equal(4, files.Length);
            Assert.True(totalBytes <= 128L * 1024 * 1024);
            Assert.False(File.Exists(Path.Combine(directory, "000.bin")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiskCacheExpiration_PreservesFilesYoungerThanThirtyDays()
    {
        var directory = CreateTempDirectory();
        var now = DateTime.UtcNow;
        var recentPath = Path.Combine(directory, "recent.bin");
        var twentyNineDaysPath = Path.Combine(directory, "twenty-nine-days.bin");

        try
        {
            File.WriteAllBytes(recentPath, [1]);
            File.WriteAllBytes(twentyNineDaysPath, [1]);
            File.SetLastWriteTimeUtc(recentPath, now.AddDays(-2));
            File.SetLastWriteTimeUtc(twentyNineDaysPath, now.AddDays(-29));

            PruneExpiredDiskCacheOncePerDay(directory, now, DateOnly.FromDateTime(now));

            Assert.True(File.Exists(recentPath));
            Assert.True(File.Exists(twentyNineDaysPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiskCacheExpiration_DeletesFilesOlderThanFortyDays()
    {
        var directory = CreateTempDirectory();
        var now = DateTime.UtcNow;
        var expiredPath = Path.Combine(directory, "expired.bin");

        try
        {
            File.WriteAllBytes(expiredPath, [1]);
            File.SetLastWriteTimeUtc(expiredPath, now.AddDays(-41));

            PruneExpiredDiskCacheOncePerDay(directory, now, DateOnly.FromDateTime(now));

            Assert.False(File.Exists(expiredPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiskCacheExpiration_RunsOnlyOncePerDate()
    {
        var directory = CreateTempDirectory();
        var now = DateTime.UtcNow;
        var localDate = DateOnly.FromDateTime(now);
        var firstPath = Path.Combine(directory, "first.bin");
        var secondPath = Path.Combine(directory, "second.bin");

        try
        {
            File.WriteAllBytes(firstPath, [1]);
            File.SetLastWriteTimeUtc(firstPath, now.AddDays(-41));

            PruneExpiredDiskCacheOncePerDay(directory, now, localDate);

            Assert.False(File.Exists(firstPath));

            File.WriteAllBytes(secondPath, [1]);
            File.SetLastWriteTimeUtc(secondPath, now.AddDays(-41));

            PruneExpiredDiskCacheOncePerDay(directory, now, localDate);

            Assert.True(File.Exists(secondPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MemoryCache_EvictsOldestEntryAtCapacity()
    {
        var memory = GetMemory();
        var evictionQueue = GetMemoryEvictionQueue();
        memory.Clear();
        while (evictionQueue.TryDequeue(out _))
        {
        }

        var image = CreateTestImage();

        try
        {
            for (var i = 0; i < 512; i++)
                SetMemory($"host-{i}.example", image);

            SetMemory("host-512.example", image);

            Assert.Equal(512, memory.Count);
            Assert.False(memory.ContainsKey("host-0.example"));
            Assert.True(memory.ContainsKey("host-1.example"));
            Assert.True(memory.ContainsKey("host-512.example"));
        }
        finally
        {
            memory.Clear();
            while (evictionQueue.TryDequeue(out _))
            {
            }
        }
    }

    [Fact]
    public void MemoryCache_UpdatedEntryIsNotRemovedByStaleEvictionRecord()
    {
        var memory = GetMemory();
        var evictionQueue = GetMemoryEvictionQueue();
        memory.Clear();
        while (evictionQueue.TryDequeue(out _))
        {
        }

        var firstImage = CreateTestImage();
        var updatedImage = CreateTestImage();

        try
        {
            for (var i = 0; i < 512; i++)
                SetMemory($"host-{i}.example", firstImage);

            SetMemory("host-0.example", updatedImage);
            SetMemory("host-512.example", firstImage);

            Assert.Equal(512, memory.Count);
            Assert.Same(updatedImage, memory["host-0.example"]);
            Assert.False(memory.ContainsKey("host-1.example"));
            Assert.True(memory.ContainsKey("host-512.example"));
        }
        finally
        {
            memory.Clear();
            while (evictionQueue.TryDequeue(out _))
            {
            }
        }
    }

    [Fact]
    public async Task FaviconResponse_SmallContent_IsAccepted()
    {
        using var content = new ByteArrayContent(new byte[32]);

        var bytes = await ReadContentBytesAsync(content);

        Assert.NotNull(bytes);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public async Task FaviconResponse_KnownOversizedContent_IsRejected()
    {
        using var content = new ByteArrayContent(new byte[(1024 * 1024) + 1]);

        var bytes = await ReadContentBytesAsync(content);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task FaviconResponse_UnknownLengthOversizedContent_IsRejected()
    {
        using var content = new ChunkedTestContent((1024 * 1024) + 1);

        var bytes = await ReadContentBytesAsync(content);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task DisabledOnlineFetching_DoesNotQueueNetworkDownload()
    {
        ConfigureOnlineFetching(false);
        ClearPendingDownloads();

        ImageSource? image = null;
        RunOnSta(() => image = FaviconService.GetImage("https://example.com", "Example"));

        Assert.IsType<RenderTargetBitmap>(image);
        Assert.Empty(GetDownloadQueue());
        Assert.Empty(GetInFlight());

        FaviconService.Prefetch(
        [
            ("https://example.com", "Example"),
            ("https://example.org", "Example Org")
        ]);

        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.Empty(GetDownloadQueue());
        Assert.Empty(GetInFlight());
    }

    [Fact]
    public void EnabledOnlineFetching_GetImageDoesNotQueueNetworkDownload()
    {
        ConfigureOnlineFetching(true);
        ClearPendingDownloads();

        try
        {
            ImageSource? image = null;
            var url = $"https://{Guid.NewGuid():N}.example";
            RunOnSta(() => image = FaviconService.GetImage(url, "Example"));

            Assert.IsType<RenderTargetBitmap>(image);
            Assert.Empty(GetDownloadQueue());
            Assert.Empty(GetInFlight());
        }
        finally
        {
            ConfigureOnlineFetching(false);
            ClearPendingDownloads();
        }
    }

    [Fact]
    public async Task DisabledOnlineFetching_StillLoadsExistingDiskCache()
    {
        const string host = "example.com";

        ConfigureOnlineFetching(false);
        ClearPendingDownloads();

        var cacheDir = FaviconService.CacheDir;
        var pathCache = FaviconService.PathCache;
        var memory = FaviconService.Memory;
        var cachePath = FaviconService.CachePath(host);

        Directory.CreateDirectory(cacheDir);
        await WriteEncryptedCacheFileAsync(host, cachePath);

        try
        {
            memory.TryRemove(host, out _);

            FaviconService.Prefetch([("https://example.com", "Example")]);

            for (var i = 0; i < 20 && !memory.ContainsKey(host); i++)
                await Task.Delay(25, TestContext.Current.CancellationToken);

            Assert.True(memory.ContainsKey(host));
            Assert.IsType<BitmapImage>(FaviconService.GetImage("https://example.com", "Example"));
            Assert.Empty(GetDownloadQueue());
            Assert.Empty(GetInFlight());
        }
        finally
        {
            memory.TryRemove(host, out _);
            pathCache.TryRemove(host, out _);
            File.Delete(cachePath);
        }
    }

    [Fact]
    public void DisabledOnlineFetching_PreservesLetterAvatarFallback()
    {
        ConfigureOnlineFetching(false);
        ClearPendingDownloads();

        ImageSource? image = null;
        RunOnSta(() => image = FaviconService.GetImage("https://example.com", "Example"));

        Assert.IsType<RenderTargetBitmap>(image);
    }

    private static async Task<byte[]?> ReadContentBytesAsync(HttpContent content)
    {
        var task = FaviconService.ReadContentBytesAsync(content);
        return await task;
    }

    private sealed class ChunkedTestContent(int length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private async Task SerializeAsync(Stream stream)
        {
            var buffer = new byte[8192];
            var remaining = length;

            while (remaining > 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, count));
                remaining -= count;
            }
        }
    }

    [Fact]
    public void CachePath_UsesHmacAndIsStable()
    {
        const string host = "example.com";
        var masterKey = GetCacheMasterKey();
        var first = FaviconService.CreateCachePath(host, masterKey);
        var second = FaviconService.CreateCachePath(host, masterKey);
        var plainHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host))).ToLowerInvariant();

        Assert.Equal(first, second);
        Assert.StartsWith("v2-", Path.GetFileName(first));
        Assert.DoesNotContain(plainHash, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiskCacheMigration_DeletesLegacyNamesAndKeepsVersionedFiles()
    {
        var directory = CreateTempDirectory();
        var legacyPath = Path.Combine(directory, new string('a', 64) + ".bin");
        var currentPath = Path.Combine(directory, "v2-" + new string('b', 64) + ".bin");

        try
        {
            File.WriteAllBytes(legacyPath, [1, 2, 3]);
            File.WriteAllBytes(currentPath, [4, 5, 6]);

            FaviconService.PruneLegacyDiskCache(directory);

            Assert.False(File.Exists(legacyPath));
            Assert.True(File.Exists(currentPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiskCacheEncryption_RoundTripsAndUsesFreshNonce()
    {
        const string host = "example.com";
        var masterKey = GetCacheMasterKey();
        var first = FaviconService.EncryptCacheBytes(host, TinyPng, masterKey);
        var second = FaviconService.EncryptCacheBytes(host, TinyPng, masterKey);
        var decrypted = FaviconService.TryDecryptCacheBytes(host, first, masterKey);

        Assert.False(TinyPng.SequenceEqual(first));
        Assert.False(first.SequenceEqual(second));
        Assert.Equal(TinyPng, decrypted);
    }

    [Fact]
    public void DiskCacheEncryption_RejectsTamperedDataAndWrongHost()
    {
        const string host = "example.com";
        var masterKey = GetCacheMasterKey();
        var encrypted = FaviconService.EncryptCacheBytes(host, TinyPng, masterKey);
        encrypted[^1] ^= 0x40;

        Assert.Null(FaviconService.TryDecryptCacheBytes(host, encrypted, masterKey));
        var valid = FaviconService.EncryptCacheBytes(host, TinyPng, masterKey);
        Assert.Null(FaviconService.TryDecryptCacheBytes("other.example", valid, masterKey));
    }

    private static byte[] GetCacheMasterKey() => FaviconService.CacheMasterKey.Value;

    private static async Task WriteEncryptedCacheFileAsync(string host, string path)
    {
        var encrypted = FaviconService.EncryptCacheBytes(host, TinyPng, GetCacheMasterKey());
        try
        {
            await File.WriteAllBytesAsync(path, encrypted, TestContext.Current.CancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    [Fact]
    public void RecentFailure_BlocksRetry()
    {
        const string host = "recent-failure.example";

        FaviconService.MarkFailed(host);

        try
        {
            var isFailed = (bool)FaviconService.IsFailedRecently(host);
            Assert.True(isFailed);
        }
        finally
        {
            GetFailed().TryRemove(host, out _);
        }
    }

    [Fact]
    public void ExpiredFailure_AllowsRetry()
    {
        const string host = "expired-failure.example";
        GetFailed()[host] = DateTimeOffset.UtcNow.AddHours(-25);

        try
        {
            var isFailed = (bool)FaviconService.IsFailedRecently(host);
            Assert.False(isFailed);
            Assert.False(GetFailed().ContainsKey(host));
        }
        finally
        {
            GetFailed().TryRemove(host, out _);
        }
    }

    [Fact]
    public async Task DiskWarm_WaitsForStartupCleanupBeforeReadingDisk()
    {
        const string host = "startup-cleanup-warm.example";
        var cleanupReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var memory = FaviconService.Memory;
        var pathCache = FaviconService.PathCache;
        var cacheDir = FaviconService.CacheDir;
        var cachePath = FaviconService.CachePath(host);
        var originalCleanupTask = FaviconService._startupCleanupTask;

        Directory.CreateDirectory(cacheDir);
        await WriteEncryptedCacheFileAsync(host, cachePath);
        memory.TryRemove(host, out _);
        FaviconService._startupCleanupTask = cleanupReleased.Task;

        try
        {
            var task = FaviconService.WarmDiskThenDownloadAsync(new List<string> { host });

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(memory.ContainsKey(host));
            Assert.False(task.IsCompleted);

            cleanupReleased.SetResult();
            await task;

            Assert.True(memory.ContainsKey(host));
        }
        finally
        {
            FaviconService._startupCleanupTask = originalCleanupTask;
            memory.TryRemove(host, out _);
            pathCache.TryRemove(host, out _);
            File.Delete(cachePath);
        }
    }

    [Fact]
    public async Task Download_WaitsForStartupCleanupBeforeReadingDisk()
    {
        const string host = "startup-cleanup-download.example";
        var cleanupReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var memory = FaviconService.Memory;
        var pathCache = FaviconService.PathCache;
        var cacheDir = FaviconService.CacheDir;
        var cachePath = FaviconService.CachePath(host);
        var originalCleanupTask = FaviconService._startupCleanupTask;

        Directory.CreateDirectory(cacheDir);
        await WriteEncryptedCacheFileAsync(host, cachePath);
        memory.TryRemove(host, out _);
        ConfigureOnlineFetching(true);
        FaviconService._startupCleanupTask = cleanupReleased.Task;

        try
        {
            var task = FaviconService.DownloadOneAsync(host);

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(memory.ContainsKey(host));
            Assert.False(task.IsCompleted);

            cleanupReleased.SetResult();
            await task;

            Assert.True(memory.ContainsKey(host));
        }
        finally
        {
            FaviconService._startupCleanupTask = originalCleanupTask;
            memory.TryRemove(host, out _);
            pathCache.TryRemove(host, out _);
            File.Delete(cachePath);
            ConfigureOnlineFetching(false);
        }
    }

    [Fact]
    public async Task SuccessfulDiskLoad_ClearsPreviousFailure()
    {
        const string host = "disk-success.example";

        ConfigureOnlineFetching(true);
        ClearPendingDownloads();

        var cacheDir = FaviconService.CacheDir;
        var pathCache = FaviconService.PathCache;
        var memory = FaviconService.Memory;
        var cachePath = FaviconService.CachePath(host);

        Directory.CreateDirectory(cacheDir);
        await WriteEncryptedCacheFileAsync(host, cachePath);
        FaviconService.MarkFailed(host);

        try
        {
            memory.TryRemove(host, out _);

            var task = FaviconService.DownloadOneAsync(host);
            await task;

            Assert.False(GetFailed().ContainsKey(host));
            Assert.IsType<BitmapImage>(FaviconService.GetImage("https://disk-success.example", "Example"));
        }
        finally
        {
            memory.TryRemove(host, out _);
            GetFailed().TryRemove(host, out _);
            pathCache.TryRemove(host, out _);
            File.Delete(cachePath);
            ConfigureOnlineFetching(false);
        }
    }

    private static void ConfigureOnlineFetching(bool enabled) =>
        WpfTestHost.Run(
            _ => FaviconService.ConfigureOnlineFetching(enabled),
            TestContext.Current.CancellationToken);

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw new InvalidOperationException("STA test failed.", exception);
    }

    private static void PruneDiskCache(string directory) =>
        FaviconService.PruneDiskCache(directory);

    private static void PruneExpiredDiskCacheOncePerDay(
        string directory,
        DateTime utcNow,
        DateOnly localDate) =>
        FaviconService.PruneExpiredDiskCacheOncePerDay(directory, utcNow, localDate);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PejPass-FaviconTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ConcurrentDictionary<string, ImageSource> GetMemory() =>
        FaviconService.Memory;

    private static ConcurrentQueue<KeyValuePair<string, ImageSource>> GetMemoryEvictionQueue() =>
        FaviconService.MemoryEvictionQueue;

    private static void SetMemory(string host, ImageSource image) =>
        FaviconService.SetMemory(host, image);

    private static ImageSource CreateTestImage() =>
        FaviconService.CreateBitmap(TinyPng)!;

    private static ConcurrentQueue<string> GetDownloadQueue() =>
        FaviconService.DownloadQueue;

    private static ConcurrentDictionary<string, byte> GetInFlight() =>
        FaviconService.InFlight;

    private static ConcurrentDictionary<string, DateTimeOffset> GetFailed() =>
        FaviconService.Failed;

    private static void ClearPendingDownloads()
    {
        var queue = GetDownloadQueue();
        while (queue.TryDequeue(out _))
        {
        }

        GetInFlight().Clear();
    }
}
