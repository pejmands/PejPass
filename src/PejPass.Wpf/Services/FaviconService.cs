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
/// Favicon cache for the entry list. UI path is memory-only (no disk/network on UI thread).
/// Background queue loads disk + downloads with limited concurrency; UI refresh is debounced.
/// </summary>
public static class FaviconService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, ImageSource> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> LetterCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Failed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> DownloadQueue = new();
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private static readonly SemaphoreSlim WorkerSlots = new(3, 3);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PejPass", "favicons");

    private static DispatcherTimer? _batchTimer;
    private static int _batchPending;

    /// <summary>Raised on UI thread after one or more favicons finished (debounced ~350ms).</summary>
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
            EnqueueHost(host);

        var letterSource = !string.IsNullOrWhiteSpace(title) ? title : host ?? "?";
        return GetLetterAvatar(letterSource);
    }

    /// <summary>Background: load disk cache + download missing icons (throttled).</summary>
    public static void Prefetch(IEnumerable<(string Url, string Title)> items)
    {
        foreach (var (url, _) in items)
        {
            var host = TryGetHost(url);
            if (host is null) continue;
            if (Memory.ContainsKey(host) || Failed.ContainsKey(host)) continue;
            EnqueueHost(host);
        }

        _ = ProcessQueueAsync();
    }

    private static void EnqueueHost(string host)
    {
        if (!InFlight.TryAdd(host, 0))
            return;

        DownloadQueue.Enqueue(host);
        _ = ProcessQueueAsync();
    }

    private static async Task ProcessQueueAsync()
    {
        if (!await DownloadGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            while (DownloadQueue.TryDequeue(out var host))
            {
                await WorkerSlots.WaitAsync().ConfigureAwait(false);
                var captured = host;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LoadOneAsync(captured).ConfigureAwait(false);
                    }
                    finally
                    {
                        WorkerSlots.Release();
                        InFlight.TryRemove(captured, out _);
                        if (!DownloadQueue.IsEmpty)
                            _ = ProcessQueueAsync();
                    }
                });
            }
        }
        finally
        {
            DownloadGate.Release();
            if (!DownloadQueue.IsEmpty)
                _ = ProcessQueueAsync();
        }
    }

    private static async Task LoadOneAsync(string host)
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
                _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
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

    private static string CachePath(string host)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host))).ToLowerInvariant();
        return Path.Combine(CacheDir, hash + ".bin");
    }

    private static BitmapImage? TryLoadFromDisk(string host)
    {
        try
        {
            var path = CachePath(host);
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            return CreateBitmap(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CreateBitmap(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
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
