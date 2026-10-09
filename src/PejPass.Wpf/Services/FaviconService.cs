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
    internal static readonly ConcurrentDictionary<string, ImageSource> Memory = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly ConcurrentQueue<KeyValuePair<string, ImageSource>> MemoryEvictionQueue = new();
    private static readonly Lock MemoryCacheLock = new();
    private static readonly ConcurrentDictionary<string, ImageSource> LetterCache = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly ConcurrentDictionary<string, DateTimeOffset> Failed = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly ConcurrentDictionary<string, string> PathCache = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly ConcurrentQueue<string> DownloadQueue = new();
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private static readonly SemaphoreSlim DownloadSlots = new(3, 3);
    private const int MaxFaviconResponseBytes = 1024 * 1024;
    private const int MaxMemoryCacheEntries = 512;
    private const int MaxDiskCacheFiles = 4096;
    private const long MaxDiskCacheBytes = 128L * 1024 * 1024;
    private const int MinDiskCacheAgeDays = 30;
    private const int MaxDiskCacheAgeDays = 40;
    private const int FailedLookupTtlHours = 24;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PejPass");
    internal static readonly string CacheDir = Path.Combine(AppDataDir, "favicons");
    private static readonly string CacheKeyPath = Path.Combine(AppDataDir, "favicon-cache-key.dpapi");
    private static readonly byte[] CacheKeyEntropy = Encoding.UTF8.GetBytes("PejPass.FaviconCache.Key.v1");
    internal static readonly Lazy<byte[]> CacheMasterKey = new(LoadOrCreateCacheMasterKey, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly byte[] CacheFileMagic = "PFC2"u8.ToArray();
    private const int CacheNonceSize = 12;
    private const int CacheTagSize = 16;
    private const int CacheMasterKeySize = 32;

    private static readonly Lock ExpirationCleanupLock = new();
    private static readonly Lock StartupCleanupLock = new();
    private static readonly Lock OnlineFetchingStateLock = new();
    private static long _cacheGeneration;
    internal static Task _startupCleanupTask = Task.CompletedTask;
    private static bool _startupCleanupStarted;

    private static DispatcherTimer? _batchTimer;
    private static int _batchPending;
    private static int _diskWarmRunning;
    private static volatile bool _onlineFetchingEnabled;

    public static event Action<bool>? OnlineFetchingChanged;

    public static void ConfigureOnlineFetching(bool enabled)
    {
        bool changed;

        lock (OnlineFetchingStateLock)
        {
            changed = _onlineFetchingEnabled != enabled;
            _onlineFetchingEnabled = enabled;
        }

        if (changed)
            OnlineFetchingChanged?.Invoke(enabled);
    }

    /// <summary>
    /// Clears all persisted and in-memory favicon cache state.
    /// A generation change prevents in-flight warm/download work from repopulating a cache that was just cleared.
    /// </summary>
    public static void ClearCache()
    {
        lock (ExpirationCleanupLock)
        {
            Interlocked.Increment(ref _cacheGeneration);

            try
            {
                if (Directory.Exists(CacheDir))
                {
                    foreach (var file in Directory.GetFiles(CacheDir, "*.bin"))
                    {
                        try { File.Delete(file); } catch { }
                    }

                    try
                    {
                        File.Delete(Path.Combine(CacheDir, ".last-expiration-cleanup-date"));
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            lock (MemoryCacheLock)
            {
                Memory.Clear();
                MemoryEvictionQueue.Clear();
            }

            Failed.Clear();
        }
    }

    /// <summary>
    /// Starts disk-cache cleanup in the background without delaying the login window.
    /// Cache reads and downloads wait for this task before accessing disk.
    /// </summary>
    public static void StartDiskCacheCleanup()
    {
        lock (StartupCleanupLock)
        {
            if (_startupCleanupStarted)
                return;

            _startupCleanupStarted = true;
            _startupCleanupTask = Task.Run(() =>
            {
                PruneLegacyDiskCache(CacheDir);
                PruneExpiredDiskCacheOncePerDay(
                    CacheDir,
                    DateTime.UtcNow,
                    DateOnly.FromDateTime(DateTime.Today));
                PruneDiskCache(CacheDir);
            });
        }
    }

    private static Task WaitForStartupCleanupAsync()
    {
        lock (StartupCleanupLock)
            return _startupCleanupTask;
    }

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
            if (Memory.ContainsKey(host) || IsFailedRecently(host)) continue;
            if (!seen.Add(host)) continue;
            hosts.Add(host);
        }

        _ = Task.Run(() => WarmDiskThenDownloadAsync(hosts));
    }

    internal static async Task WarmDiskThenDownloadAsync(List<string> hosts)
    {
        await WaitForStartupCleanupAsync().ConfigureAwait(false);

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
                        if (Memory.ContainsKey(host) || IsFailedRecently(host))
                            return ValueTask.CompletedTask;

                        var generation = Volatile.Read(ref _cacheGeneration);
                        var fromDisk = TryLoadFromDisk(host);
                        if (fromDisk is not null && generation == Volatile.Read(ref _cacheGeneration))
                        {
                            SetMemory(host, fromDisk);
                            ScheduleBatchNotify();
                        }

                        return ValueTask.CompletedTask;
                    }).ConfigureAwait(false);

                PruneDiskCache(CacheDir);
            }

            if (!_onlineFetchingEnabled)
                return;

            foreach (var host in hosts)
            {
                if (Memory.ContainsKey(host) || IsFailedRecently(host))
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
        if (Memory.ContainsKey(host) || IsFailedRecently(host))
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
                if (!_onlineFetchingEnabled || Memory.ContainsKey(host) || IsFailedRecently(host))
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

    internal static async Task DownloadOneAsync(string host)
    {
        try
        {
            if (!_onlineFetchingEnabled)
                return;

            await WaitForStartupCleanupAsync().ConfigureAwait(false);
            var generation = Volatile.Read(ref _cacheGeneration);

            var fromDisk = TryLoadFromDisk(host);
            if (fromDisk is not null)
            {
                if (generation != Volatile.Read(ref _cacheGeneration))
                    return;

                SetMemory(host, fromDisk);
                Failed.TryRemove(host, out _);
                ScheduleBatchNotify();
                return;
            }

            byte[]? bytes =
                await TryDownloadBytesAsync($"https://icons.duckduckgo.com/ip3/{host}.ico").ConfigureAwait(false)
                ?? await TryDownloadBytesAsync($"https://www.google.com/s2/favicons?domain={host}&sz=64").ConfigureAwait(false);

            if (bytes is null || bytes.Length < 16)
            {
                if (_onlineFetchingEnabled)
                    MarkFailed(host);
                return;
            }

            var image = CreateBitmap(bytes);
            if (image is null)
            {
                if (_onlineFetchingEnabled)
                    MarkFailed(host);
                return;
            }

            lock (OnlineFetchingStateLock)
            {
                if (!_onlineFetchingEnabled ||
                    generation != Volatile.Read(ref _cacheGeneration))
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(CacheDir);
                    var encryptedBytes = EncryptCacheBytes(host, bytes, CacheMasterKey.Value);
                    try
                    {
                        File.WriteAllBytes(CachePath(host), encryptedBytes);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(encryptedBytes);
                    }
                    PruneDiskCache(CacheDir);
                }
                catch
                {
                }

                SetMemory(host, image);
                Failed.TryRemove(host, out _);
            }

            ScheduleBatchNotify();
        }
        catch
        {
            MarkFailed(host);
        }
    }

    internal static void SetMemory(string host, ImageSource image)
    {
        lock (MemoryCacheLock)
        {
            if (Memory.ContainsKey(host))
            {
                Memory[host] = image;
                return;
            }

            Memory[host] = image;
            MemoryEvictionQueue.Enqueue(new KeyValuePair<string, ImageSource>(host, image));

            while (Memory.Count > MaxMemoryCacheEntries && MemoryEvictionQueue.TryDequeue(out var candidate))
                Memory.TryRemove(candidate);
        }
    }

    internal static bool IsFailedRecently(string host)
    {
        if (!Failed.TryGetValue(host, out var failedAt))
            return false;

        if (DateTimeOffset.UtcNow - failedAt < TimeSpan.FromHours(FailedLookupTtlHours))
            return true;

        Failed.TryRemove(new KeyValuePair<string, DateTimeOffset>(host, failedAt));
        return false;
    }

    internal static void MarkFailed(string host) =>
        Failed[host] = DateTimeOffset.UtcNow;

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
            using var response = await Http.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxFaviconResponseBytes)
                return null;

            return await ReadContentBytesAsync(response.Content).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<byte[]?> ReadContentBytesAsync(HttpContent content)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            var total = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                if (read == 0)
                    break;

                if (total > MaxFaviconResponseBytes - read)
                    return null;

                ms.Write(buffer, 0, read);
                total += read;
            }

            return total > 0 ? ms.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void PruneDiskCache(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            var files = new DirectoryInfo(directory).GetFiles("*.bin");
            var totalBytes = files.Sum(file => file.Length);
            if (files.Length <= MaxDiskCacheFiles && totalBytes <= MaxDiskCacheBytes)
                return;

            var fileCount = files.Length;

            foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
            {
                if (fileCount <= MaxDiskCacheFiles && totalBytes <= MaxDiskCacheBytes)
                    break;

                try
                {
                    var length = file.Length;
                    file.Delete();
                    fileCount--;
                    totalBytes -= length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    internal static void PruneExpiredDiskCacheOncePerDay(
        string directory,
        DateTime utcNow,
        DateOnly localDate)
    {
        lock (ExpirationCleanupLock)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                var markerPath = Path.Combine(directory, ".last-expiration-cleanup-date");
                if (File.Exists(markerPath) &&
                    DateOnly.TryParseExact(
                        File.ReadAllText(markerPath).Trim(),
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var lastCleanupDate) &&
                    lastCleanupDate == localDate)
                {
                    return;
                }

                foreach (var file in Directory.GetFiles(directory, "*.bin"))
                {
                    try
                    {
                        var age = utcNow - File.GetLastWriteTimeUtc(file);
                        var thresholdDays = Random.Shared.Next(
                            MinDiskCacheAgeDays,
                            MaxDiskCacheAgeDays + 1);

                        if (age.TotalDays >= thresholdDays)
                            File.Delete(file);
                    }
                    catch
                    {
                    }
                }

                File.WriteAllText(
                    markerPath,
                    localDate.ToString(
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
            }
        }
    }

    // Cache storage privacy is implemented by HMAC-derived names and authenticated encryption.
    internal static string CachePath(string host) =>
        PathCache.GetOrAdd(host, static h => CreateCachePath(h, CacheMasterKey.Value));

    internal static string CreateCachePath(string host, byte[] masterKey)
    {
        var key = DeriveCacheKey(masterKey, "filename");
        try
        {
            var hash = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(host))).ToLowerInvariant();
            return Path.Combine(CacheDir, "v2-" + hash + ".bin");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveCacheKey(byte[] masterKey, string purpose) =>
        HMACSHA256.HashData(masterKey, Encoding.UTF8.GetBytes("PejPass.FaviconCache." + purpose + ".v1"));

    private static byte[] LoadOrCreateCacheMasterKey()
    {
        Directory.CreateDirectory(AppDataDir);
        try
        {
            var key = ProtectedData.Unprotect(File.ReadAllBytes(CacheKeyPath), CacheKeyEntropy, DataProtectionScope.CurrentUser);
            if (key.Length == CacheMasterKeySize)
                return key;
            CryptographicOperations.ZeroMemory(key);
        }
        catch
        {
        }

        var newKey = RandomNumberGenerator.GetBytes(CacheMasterKeySize);
        var protectedKey = ProtectedData.Protect(newKey, CacheKeyEntropy, DataProtectionScope.CurrentUser);
        var tempPath = CacheKeyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, protectedKey);
            File.Move(tempPath, CacheKeyPath, overwrite: true);
            return newKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(newKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            try { File.Delete(tempPath); } catch { }
        }
    }

    internal static byte[] EncryptCacheBytes(string host, byte[] plaintext, byte[] masterKey)
    {
        var key = DeriveCacheKey(masterKey, "encryption");
        var nonce = RandomNumberGenerator.GetBytes(CacheNonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[CacheTagSize];
        try
        {
            using var aes = new AesGcm(key, CacheTagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(host));
            var result = new byte[CacheFileMagic.Length + CacheNonceSize + CacheTagSize + ciphertext.Length];
            CacheFileMagic.CopyTo(result, 0);
            nonce.CopyTo(result, CacheFileMagic.Length);
            tag.CopyTo(result, CacheFileMagic.Length + CacheNonceSize);
            ciphertext.CopyTo(result, CacheFileMagic.Length + CacheNonceSize + CacheTagSize);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static byte[]? TryDecryptCacheBytes(string host, byte[] storedBytes, byte[] masterKey)
    {
        var headerLength = CacheFileMagic.Length + CacheNonceSize + CacheTagSize;
        if (storedBytes.Length < headerLength || !storedBytes.AsSpan(0, CacheFileMagic.Length).SequenceEqual(CacheFileMagic))
            return null;

        var nonce = storedBytes.AsSpan(CacheFileMagic.Length, CacheNonceSize);
        var tag = storedBytes.AsSpan(CacheFileMagic.Length + CacheNonceSize, CacheTagSize);
        var ciphertext = storedBytes.AsSpan(headerLength);
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveCacheKey(masterKey, "encryption");
        try
        {
            using var aes = new AesGcm(key, CacheTagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(host));
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static void PruneLegacyDiskCache(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var file in Directory.GetFiles(directory, "*.bin"))
            {
                if (Path.GetFileName(file).StartsWith("v2-", StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
        }
    }

    private static BitmapImage? TryLoadFromDisk(string host)
    {
        try
        {
            var path = CachePath(host);
            if (!File.Exists(path))
                return null;

            var storedBytes = File.ReadAllBytes(path);
            var bytes = TryDecryptCacheBytes(host, storedBytes, CacheMasterKey.Value);
            if (bytes is null || bytes.Length < 16)
                return null;

            try
            {
                return CreateBitmap(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
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
    internal static BitmapImage? CreateBitmap(byte[] bytes)
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
