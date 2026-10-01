using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Media.Imaging;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

public sealed class FaviconServiceTests
{
    private static readonly FieldInfo DownloadQueueField =
        typeof(FaviconService).GetField("DownloadQueue", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo InFlightField =
        typeof(FaviconService).GetField("InFlight", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo MemoryField =
        typeof(FaviconService).GetField("Memory", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo CacheDirField =
        typeof(FaviconService).GetField("CacheDir", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo PathCacheField =
        typeof(FaviconService).GetField("PathCache", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CachePathMethod =
        typeof(FaviconService).GetMethod("CachePath", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly byte[] TinyPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task DisabledOnlineFetching_DoesNotQueueNetworkDownload()
    {
        FaviconService.ConfigureOnlineFetching(false);
        ClearPendingDownloads();

        var image = FaviconService.GetImage("https://example.com", "Example");

        Assert.IsType<RenderTargetBitmap>(image);
        Assert.Empty(GetDownloadQueue());
        Assert.Empty(GetInFlight());

        FaviconService.Prefetch(
        [
            ("https://example.com", "Example"),
            ("https://example.org", "Example Org")
        ]);

        await Task.Delay(150);

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
        await File.WriteAllBytesAsync(cachePath, TinyPng);

        try
        {
            memory.TryRemove(host, out _);

            FaviconService.Prefetch([( "https://example.com", "Example" )]);

            for (var i = 0; i < 20 && !memory.ContainsKey(host); i++)
                await Task.Delay(25);

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

        var image = FaviconService.GetImage("https://example.com", "Example");

        Assert.IsType<RenderTargetBitmap>(image);
    }

    private static ConcurrentQueue<string> GetDownloadQueue() =>
        (ConcurrentQueue<string>)DownloadQueueField.GetValue(null)!;

    private static ConcurrentDictionary<string, byte> GetInFlight() =>
        (ConcurrentDictionary<string, byte>)InFlightField.GetValue(null)!;

    private static void ClearPendingDownloads()
    {
        var queue = GetDownloadQueue();
        while (queue.TryDequeue(out _))
        {
        }

        GetInFlight().Clear();
    }
}
