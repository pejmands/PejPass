using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

[TestClass(DisableParallelization = true)]
public sealed class FaviconServiceTests
{
    private static readonly FieldInfo DownloadQueueField =
        typeof(FaviconService).GetField("DownloadQueue", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo InFlightField =
        typeof(FaviconService).GetField("InFlight", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo MemoryField =
        typeof(FaviconService).GetField("Memory", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo FailedField =
        typeof(FaviconService).GetField("Failed", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo CacheDirField =
        typeof(FaviconService).GetField("CacheDir", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo PathCacheField =
        typeof(FaviconService).GetField("PathCache", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CachePathMethod =
        typeof(FaviconService).GetMethod("CachePath", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsFailedRecentlyMethod =
        typeof(FaviconService).GetMethod("IsFailedRecently", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MarkFailedMethod =
        typeof(FaviconService).GetMethod("MarkFailed", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo DownloadOneMethod =
        typeof(FaviconService).GetMethod("DownloadOneAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly byte[] TinyPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task DisabledOnlineFetching_DoesNotQueueNetworkDownload()
    {
        FaviconService.ConfigureOnlineFetching(false);
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
    public async Task DisabledOnlineFetching_StillLoadsExistingDiskCache()
    {
        const string host = "example.com";

        FaviconService.ConfigureOnlineFetching(false);
        ClearPendingDownloads();

        var cacheDir = (string)CacheDirField.GetValue(null)!;
        var pathCache = (ConcurrentDictionary<string, string>)PathCacheField.GetValue(null)!;
        var memory = (ConcurrentDictionary<string, System.Windows.Media.ImageSource>)MemoryField.GetValue(null)!;
        var cachePath = (string)CachePathMethod.Invoke(null, [host])!;

        Directory.CreateDirectory(cacheDir);
        await File.WriteAllBytesAsync(cachePath, TinyPng, TestContext.Current.CancellationToken);

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
        FaviconService.ConfigureOnlineFetching(false);
        ClearPendingDownloads();

        ImageSource? image = null;
        RunOnSta(() => image = FaviconService.GetImage("https://example.com", "Example"));

        Assert.IsType<RenderTargetBitmap>(image);
    }

    [Fact]
    public void RecentFailure_BlocksRetry()
    {
        const string host = "recent-failure.example";

        MarkFailedMethod.Invoke(null, [host]);

        try
        {
            var isFailed = (bool)IsFailedRecentlyMethod.Invoke(null, [host])!;
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
            var isFailed = (bool)IsFailedRecentlyMethod.Invoke(null, [host])!;
            Assert.False(isFailed);
            Assert.False(GetFailed().ContainsKey(host));
        }
        finally
        {
            GetFailed().TryRemove(host, out _);
        }
    }

    [Fact]
    public async Task SuccessfulDiskLoad_ClearsPreviousFailure()
    {
        const string host = "disk-success.example";

        FaviconService.ConfigureOnlineFetching(true);
        ClearPendingDownloads();

        var cacheDir = (string)CacheDirField.GetValue(null)!;
        var pathCache = (ConcurrentDictionary<string, string>)PathCacheField.GetValue(null)!;
        var memory = (ConcurrentDictionary<string, System.Windows.Media.ImageSource>)MemoryField.GetValue(null)!;
        var cachePath = (string)CachePathMethod.Invoke(null, [host])!;

        Directory.CreateDirectory(cacheDir);
        await File.WriteAllBytesAsync(cachePath, TinyPng, TestContext.Current.CancellationToken);
        MarkFailedMethod.Invoke(null, [host]);

        try
        {
            memory.TryRemove(host, out _);

            var task = (Task)DownloadOneMethod.Invoke(null, [host])!;
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
            FaviconService.ConfigureOnlineFetching(false);
        }
    }

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

    private static ConcurrentQueue<string> GetDownloadQueue() =>
        (ConcurrentQueue<string>)DownloadQueueField.GetValue(null)!;

    private static ConcurrentDictionary<string, byte> GetInFlight() =>
        (ConcurrentDictionary<string, byte>)InFlightField.GetValue(null)!;

    private static ConcurrentDictionary<string, DateTimeOffset> GetFailed() =>
        (ConcurrentDictionary<string, DateTimeOffset>)FailedField.GetValue(null)!;

    private static void ClearPendingDownloads()
    {
        var queue = GetDownloadQueue();
        while (queue.TryDequeue(out _))
        {
        }

        GetInFlight().Clear();
    }
}
