using PejPass.Wpf.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

    private static readonly FieldInfo MemoryEvictionQueueField =
        typeof(FaviconService).GetField("MemoryEvictionQueue", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SetMemoryMethod =
        typeof(FaviconService).GetMethod("SetMemory", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateBitmapMethod =
        typeof(FaviconService).GetMethod("CreateBitmap", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo CacheDirField =
        typeof(FaviconService).GetField("CacheDir", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo PathCacheField =
        typeof(FaviconService).GetField("PathCache", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CachePathMethod =
        typeof(FaviconService).GetMethod("CachePath", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ReadContentBytesMethod =
        typeof(FaviconService).GetMethod("ReadContentBytesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly byte[] TinyPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

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
            for (var i = 0; i < 256; i++)
                SetMemory($"host-{i}.example", image);

            SetMemory("host-256.example", image);

            Assert.Equal(256, memory.Count);
            Assert.False(memory.ContainsKey("host-0.example"));
            Assert.True(memory.ContainsKey("host-1.example"));
            Assert.True(memory.ContainsKey("host-256.example"));
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
            for (var i = 0; i < 256; i++)
                SetMemory($"host-{i}.example", firstImage);

            SetMemory("host-0.example", updatedImage);
            SetMemory("host-256.example", firstImage);

            Assert.Equal(256, memory.Count);
            Assert.Same(updatedImage, memory["host-0.example"]);
            Assert.False(memory.ContainsKey("host-1.example"));
            Assert.True(memory.ContainsKey("host-256.example"));
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
        using var content = new ByteArrayContent(new byte[(256 * 1024) + 1]);

        var bytes = await ReadContentBytesAsync(content);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task FaviconResponse_UnknownLengthOversizedContent_IsRejected()
    {
        using var content = new ChunkedTestContent((256 * 1024) + 1);

        var bytes = await ReadContentBytesAsync(content);

        Assert.Null(bytes);
    }

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

    private static async Task<byte[]?> ReadContentBytesAsync(HttpContent content)
    {
        var task = (Task<byte[]?>)ReadContentBytesMethod.Invoke(null, [content])!;
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

    private static ConcurrentDictionary<string, ImageSource> GetMemory() =>
        (ConcurrentDictionary<string, ImageSource>)MemoryField.GetValue(null)!;

    private static ConcurrentQueue<KeyValuePair<string, ImageSource>> GetMemoryEvictionQueue() =>
        (ConcurrentQueue<KeyValuePair<string, ImageSource>>)MemoryEvictionQueueField.GetValue(null)!;

    private static void SetMemory(string host, ImageSource image) =>
        SetMemoryMethod.Invoke(null, [host, image]);

    private static ImageSource CreateTestImage() =>
        (ImageSource)CreateBitmapMethod.Invoke(null, [TinyPng])!;

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
