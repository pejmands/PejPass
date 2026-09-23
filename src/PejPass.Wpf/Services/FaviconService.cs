using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PejPass.Wpf.Services;

/// <summary>
/// Favicon cache for the entry list.
/// UI path is memory-only. Disk is warmed in parallel (no network queue).
/// Network downloads are throttled separately. Bitmaps are decoded+frozen off the UI thread.
/// </summary>
public static class FaviconService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, ImageSource> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> LetterCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Failed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> PathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> DownloadQueue = new();
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private static readonly SemaphoreSlim DownloadSlots = new(3, 3);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PejPass", "favicons");

    private static DispatcherTimer? _batchTimer;
    private static int _batchPending;
    private static int _diskWarmRunning;

    /// <summary>Raised on UI thread after one or more favicons finished (debounced).</summary>
    public static event Action? FaviconsBatchReady;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PejPass/1.0 (+local password manager)");
        return c;
    }

    public static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var raw = url.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
            raw = "https://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        var host = uri.Host.Trim().TrimEnd('.');
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        return string.IsNullOrEmpty(host) ? null : host.ToLowerInvariant();
    }

    /// <summary>
    /// UI-safe: only touches memory + letter avatars. Never blocks on disk or network.
    /// </summary>
    public static ImageSource GetImage(string? url, string? title = null)
    {
        var host = TryGetHost(url);
        if (host is not null && Memory.TryGetValue(host, out var mem))
            return mem;

        if (host is not null && !Failed.ContainsKey(host) && !Memory.ContainsKey(host))
            EnqueueDownload(host);

        var letterSource = !string.IsNullOrWhiteSpace(title) ? title : host ?? "?";
        return GetLetterAvatar(letterSource);
    }

    /// <summary>
    /// Background: warm disk cache in parallel, then download only missing hosts (throttled).
    /// </summary>
    public static void Prefetch(IEnumerable<(string Url, string Title)> items)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (url, _) in items)
        {
            var host = TryGetHost(url);
            if (host is null) continue;
            if (Memory.ContainsKey(host) || Failed.ContainsKey(host)) continue;
            if (!seen.Add(host)) continue;
            hosts.Add(host);
        }

        if (hosts.Count == 0)
            return;

        _ = Task.Run(() => WarmDiskThenDownloadAsync(hosts));
    }

    private static async Task WarmDiskThenDownloadAsync(List<string> hosts)
    {
        var runDisk = Interlocked.CompareExchange(ref _diskWarmRunning, 1, 0) == 0;

        try
        {
            if (runDisk)
            {
                await Parallel.ForEachAsync(
                    hosts,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 16) },
                    (host, _) =>
                    {
                        if (Memory.ContainsKey(host) || Failed.ContainsKey(host))
                            return ValueTask.CompletedTask;

                        var fromDisk = TryLoadFromDisk(host);
                        if (fromDisk is not null)
                        {
                            Memory[host] = fromDisk;
                            ScheduleBatchNotify();
                        }

                        return ValueTask.CompletedTask;
                    }).ConfigureAwait(false);
            }

            foreach (var host in hosts)
            {
                if (Memory.ContainsKey(host) || Failed.ContainsKey(host))
                    continue;
                EnqueueDownload(host);
            }
        }
        finally
        {
            if (runDisk)
                Interlocked.Exchange(ref _diskWarmRunning, 0);
        }
    }

    private static void EnqueueDownload(string host)
    {
        if (Memory.ContainsKey(host) || Failed.ContainsKey(host))
            return;

        if (!InFlight.TryAdd(host, 0))
            return;

        DownloadQueue.Enqueue(host);
        _ = ProcessDownloadQueueAsync();
    }

    private static async Task ProcessDownloadQueueAsync()
    {
        if (!await DownloadGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            while (DownloadQueue.TryDequeue(out var host))
            {
                if (Memory.ContainsKey(host) || Failed.ContainsKey(host))
                {
                    InFlight.TryRemove(host, out _);
                    continue;
                }

                await DownloadSlots.WaitAsync().ConfigureAwait(false);
                var captured = host;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await DownloadOneAsync(captured).ConfigureAwait(false);
                    }
                    finally
                    {
                        DownloadSlots.Release();
                        InFlight.TryRemove(captured, out _);
                        if (!DownloadQueue.IsEmpty)
                            _ = ProcessDownloadQueueAsync();
                    }
                });
            }
        }
        finally
        {
            DownloadGate.Release();
            if (!DownloadQueue.IsEmpty)
                _ = ProcessDownloadQueueAsync();
        }
    }

    private static async Task DownloadOneAsync(string host)
    {
        try
        {
            var fromDisk = TryLoadFromDisk(host);
            if (fromDisk is not null)
            {
                Memory[host] = fromDisk;
                ScheduleBatchNotify();
                return;
            }

            byte[]? bytes =
                await TryDownloadBytesAsync($"https://icons.duckduckgo.com/ip3/{host}.ico").ConfigureAwait(false)
                ?? await TryDownloadBytesAsync($"https://www.google.com/s2/favicons?domain={host}&sz=64").ConfigureAwait(false);

            if (bytes is null || bytes.Length < 16)
            {
                Failed[host] = 0;
                return;
            }

            try
            {
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllBytesAsync(CachePath(host), bytes).ConfigureAwait(false);
            }
            catch
            {
            }

            var image = CreateBitmap(bytes);
            if (image is null)
            {
                Failed[host] = 0;
                return;
            }

            Memory[host] = image;
            ScheduleBatchNotify();
        }
        catch
        {
            Failed[host] = 0;
        }
    }

    private static void ScheduleBatchNotify()
    {
        Interlocked.Exchange(ref _batchPending, 1);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        dispatcher.BeginInvoke(() =>
        {
            if (_batchTimer is null)
            {
                _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _batchTimer.Tick += (_, _) =>
                {
                    _batchTimer.Stop();
                    if (Interlocked.Exchange(ref _batchPending, 0) == 0)
                        return;
                    FaviconsBatchReady?.Invoke();
                };
            }

            if (!_batchTimer.IsEnabled)
                _batchTimer.Start();
        }, DispatcherPriority.Background);
    }

    private static async Task<byte[]?> TryDownloadBytesAsync(string requestUrl)
    {
        try
        {
            using var response = await Http.GetAsync(requestUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static string CachePath(string host) =>
        PathCache.GetOrAdd(host, static h =>
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(h))).ToLowerInvariant();
            return Path.Combine(CacheDir, hash + ".bin");
        });

    private static BitmapImage? TryLoadFromDisk(string host)
    {
        try
        {
            var path = CachePath(host);
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 16)
                return null;

            return CreateBitmap(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decode + Freeze on the calling thread (background is fine).
    /// No Dispatcher.Invoke — avoids serializing hundreds of icons on the UI thread.
    /// </summary>
    private static BitmapImage? CreateBitmap(byte[] bytes)
    {
        try
        {
            return CreateBitmapCore(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CreateBitmapCore(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = ms;
            bmp.DecodePixelWidth = 64;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource GetLetterAvatar(string seed)
    {
        var letter = "?";
        foreach (var ch in seed.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                letter = char.ToUpperInvariant(ch).ToString();
                break;
            }
        }

        return LetterCache.GetOrAdd(letter, static l => CreateLetterAvatarCore(l));
    }

    private static RenderTargetBitmap CreateLetterAvatarCore(string letter)
    {
        var hash = letter.GetHashCode();
        var r = (byte)(80 + (hash & 0x7F));
        var g = (byte)(80 + ((hash >> 8) & 0x7F));
        var b = (byte)(80 + ((hash >> 16) & 0x7F));

        const int size = 64;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(r, g, b)),
                null,
                new Point(size / 2.0, size / 2.0),
                size / 2.0 - 1,
                size / 2.0 - 1);

            var ft = new FormattedText(
                letter,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                32,
                Brushes.White,
                1.25);

            dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
