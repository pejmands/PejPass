using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PejPass.Wpf.Services;

/// <summary>
/// Fetches and caches site favicons for the entry list.
/// Offline-friendly: disk cache under LocalAppData; letter avatar when unknown.
/// </summary>
public static class FaviconService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, ImageSource> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Failed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PejPass", "favicons");

    /// <summary>Raised on UI thread when a favicon becomes available (host key).</summary>
    public static event Action<string>? FaviconReady;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
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
    /// Returns a cached favicon or a letter avatar. Schedules download when needed.
    /// </summary>
    public static ImageSource GetImage(string? url, string? title = null)
    {
        var host = TryGetHost(url);
        if (host is not null)
        {
            if (Memory.TryGetValue(host, out var mem))
                return mem;

            var fromDisk = TryLoadFromDisk(host);
            if (fromDisk is not null)
            {
                Memory[host] = fromDisk;
                return fromDisk;
            }

            if (!Failed.ContainsKey(host))
                _ = DownloadAsync(host);
        }

        var letterSource = title;
        if (string.IsNullOrWhiteSpace(letterSource))
            letterSource = host ?? "?";

        return CreateLetterAvatar(letterSource);
    }

    public static void Prefetch(IEnumerable<(string? Url, string? Title)> items)
    {
        foreach (var (url, _) in items)
        {
            var host = TryGetHost(url);
            if (host is null) continue;
            if (Memory.ContainsKey(host) || Failed.ContainsKey(host)) continue;
            if (File.Exists(CachePath(host)))
            {
                var img = TryLoadFromDisk(host);
                if (img is not null)
                    Memory[host] = img;
                continue;
            }

            _ = DownloadAsync(host);
        }
    }

    private static async Task DownloadAsync(string host)
    {
        if (!InFlight.TryAdd(host, 0))
            return;

        try
        {
            // DuckDuckGo icon service (PNG). Fallback: Google s2 favicons.
            byte[]? bytes =
                await TryDownloadBytesAsync($"https://icons.duckduckgo.com/ip3/{host}.ico")
                ?? await TryDownloadBytesAsync($"https://www.google.com/s2/favicons?domain={host}&sz=64");

            if (bytes is null || bytes.Length < 16)
            {
                Failed[host] = 0;
                return;
            }

            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(CachePath(host), bytes);

            var image = CreateBitmap(bytes);
            if (image is null)
            {
                Failed[host] = 0;
                return;
            }

            Memory[host] = image;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            if (dispatcher.CheckAccess())
                FaviconReady?.Invoke(host);
            else
                _ = dispatcher.BeginInvoke(() => FaviconReady?.Invoke(host));
        }
        catch
        {
            Failed[host] = 0;
        }
        finally
        {
            InFlight.TryRemove(host, out _);
        }
    }

    private static async Task<byte[]?> TryDownloadBytesAsync(string requestUrl)
    {
        try
        {
            using var response = await Http.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
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

    private static ImageSource? TryLoadFromDisk(string host)
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

    private static ImageSource? CreateBitmap(byte[] bytes)
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

    /// <summary>Simple colored circle with first letter (no network).</summary>
    public static ImageSource CreateLetterAvatar(string seed)
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

        // Stable pastel-ish color from hash
        var hash = seed.GetHashCode();
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
