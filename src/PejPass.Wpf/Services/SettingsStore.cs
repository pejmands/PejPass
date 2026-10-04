using PejPass.Domain.Settings;
using PejPass.Wpf.Controls;
using System.IO;
using System.Text.Json;

namespace PejPass.Wpf.Services;

public static class SettingsStore
{
    private const int MaxAutoLockMinutes = 1440;
    private const int MaxCorruptSettingsFiles = 5;
    private static readonly TimeSpan CorruptSettingsRetention = TimeSpan.FromDays(90);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string SettingsPath { get; set; }
    = AppSettings.SettingsFilePath;

    public static AppSettings Load()
    {
        var path = SettingsPath;

        CleanupTempFile(path);
        CleanupCorruptedSettings(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);

            var settings = new AppSettings();
            if (Normalize(document.RootElement, settings))
                TrySave(settings);

            return settings;
        }
        catch
        {
            BackupCorruptedSettings(path);

            var settings = new AppSettings();
            TrySave(settings);

            return settings;
        }
    }

    private static void CleanupTempFile(string path)
    {
        try
        {
            var tempPath = path + ".tmp";

            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void BackupCorruptedSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var directory = Path.GetDirectoryName(path)!;
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            var backupPath = Path.Combine(
                directory,
                $"{fileName}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");

            File.Move(path, backupPath);
        }
        catch
        {
            // Best effort only.
            // If backup fails, recovery should still continue.
        }
    }

    private static bool Normalize(JsonElement root, AppSettings settings)
    {
        var defaults = new AppSettings();
        var needsSave = false;

        if (root.TryGetProperty("recentVaultPaths", out var recentVaultPaths) &&
            recentVaultPaths.ValueKind == JsonValueKind.Array)
        {
            if (recentVaultPaths.GetArrayLength() > 5)
                needsSave = true;

            settings.RecentVaultPaths = [.. recentVaultPaths
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Take(5)];
        }
        else
        {
            settings.RecentVaultPaths = [];
            needsSave = true;
        }

        if (!TryGetInt32(root, "autoLockMinutes", out var autoLockMinutes) ||
            autoLockMinutes < 0 ||
            autoLockMinutes > MaxAutoLockMinutes)
        {
            autoLockMinutes = defaults.AutoLockMinutes;
            needsSave = true;
        }

        settings.AutoLockMinutes = autoLockMinutes;

        if (!TryGetInt32(root, "clipboardClearSeconds", out var clipboardClearSeconds) ||
            clipboardClearSeconds < 5 ||
            clipboardClearSeconds > 300)
        {
            clipboardClearSeconds = defaults.ClipboardClearSeconds;
            needsSave = true;
        }

        settings.ClipboardClearSeconds = clipboardClearSeconds;

        if (!TryGetInt32(root, "revealSecretSeconds", out var revealSecretSeconds) ||
            revealSecretSeconds is not 0 and not 5 and not 10 and not 30 and not 60)
        {
            revealSecretSeconds = defaults.RevealSecretSeconds;
            needsSave = true;
        }

        settings.RevealSecretSeconds = revealSecretSeconds;

        if (!TryGetEnumValue<ThemeMode>(root, "theme", out var theme))
        {
            theme = defaults.Theme;
            needsSave = true;
        }

        settings.Theme = theme;

        if (!TryGetEnumValue<FontSizeMode>(root, "fontSize", out var fontSize))
        {
            fontSize = defaults.FontSize;
            needsSave = true;
        }

        settings.FontSize = fontSize;

        if (!TryGetDouble(root, "zoom", out var zoom) ||
            !ZoomBehavior.ZoomLevels.Any(level => Math.Abs(level - zoom) < 0.001))
        {
            zoom = defaults.Zoom;
            needsSave = true;
        }

        settings.Zoom = zoom;

        if (!TryGetEnumValue<EntrySortMode>(root, "sortMode", out var sortMode))
        {
            sortMode = defaults.SortMode;
            needsSave = true;
        }

        settings.SortMode = sortMode;

        if (!TryGetEnumValue<TagSortMode>(root, "tagSortMode", out var tagSortMode))
        {
            tagSortMode = defaults.TagSortMode;
            needsSave = true;
        }

        settings.TagSortMode = tagSortMode;

        if (!TryGetBool(root, "windowsHelloEnabled", out var windowsHelloEnabled))
        {
            windowsHelloEnabled = defaults.WindowsHelloEnabled;
            needsSave = true;
        }

        settings.WindowsHelloEnabled = windowsHelloEnabled;

        if (!TryGetBool(root, "onlineFaviconFetchingEnabled", out var onlineFaviconFetchingEnabled))
        {
            onlineFaviconFetchingEnabled = false;
            needsSave = true;
        }

        settings.OnlineFaviconFetchingEnabled = onlineFaviconFetchingEnabled;

        if (!TryGetBool(root, "hasSeenFastDragScrollTip", out var hasSeenFastDragScrollTip))
        {
            hasSeenFastDragScrollTip = defaults.HasSeenFastDragScrollTip;
            needsSave = true;
        }

        settings.HasSeenFastDragScrollTip = hasSeenFastDragScrollTip;

        return needsSave;
    }

    private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
    {
        if (root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.Number &&
            property.TryGetInt32(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetDouble(JsonElement root, string propertyName, out double value)
    {
        if (root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.Number &&
            property.TryGetDouble(out value) &&
            double.IsFinite(value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetBool(JsonElement root, string propertyName, out bool value)
    {
        if (root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetEnumValue<TEnum>(JsonElement root, string propertyName, out TEnum value)
        where TEnum : struct, Enum
    {
        if (TryGetInt32(root, propertyName, out var numericValue) &&
            Enum.IsDefined(typeof(TEnum), numericValue))
        {
            value = (TEnum)(object)numericValue;
            return true;
        }

        value = default;
        return false;
    }

    private static void CleanupCorruptedSettings(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            var files = Directory.GetFiles(
                    directory,
                    "settings.corrupt-*.json")
                .Select(path => new FileInfo(path))
                .ToList();

            var now = DateTime.UtcNow;

            foreach (var file in files)
            {
                if (!TryGetCorruptFileDate(file.Name, out var createdAt))
                    continue;

                if (now - createdAt > CorruptSettingsRetention)
                {
                    file.Delete();
                }
            }

            files = [.. Directory.GetFiles(
                    directory,
                    "settings.corrupt-*.json")
                .Select(path => new FileInfo(path))];

            var remaining = files
                .Where(file => TryGetCorruptFileDate(file.Name, out _))
                .OrderByDescending(file =>
                {
                    TryGetCorruptFileDate(file.Name, out var date);
                    return date;
                })
                .ToList();

            foreach (var file in remaining.Skip(MaxCorruptSettingsFiles))
            {
                file.Delete();
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static bool TryGetCorruptFileDate(
    string fileName,
    out DateTime date)
    {
        date = default;

        const string prefix = "settings.corrupt-";
        const string suffix = ".json";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var timestamp = fileName[
            prefix.Length..^suffix.Length];

        return DateTime.TryParseExact(
            timestamp,
            "yyyyMMdd-HHmmss",
            null,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out date);
    }

    public static void EnsureWritable()
    {
        var path = SettingsPath;
        var dir = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(dir);

        if (!File.Exists(path))
            return;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read);
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException("The settings file is not writable. It may be locked or access may be denied.");
        }
        catch (IOException)
        {
            throw new IOException("The settings file is currently locked or unavailable for writing.");
        }
    }

    public static bool TrySave(AppSettings settings)
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path)!;

            Directory.CreateDirectory(dir);

            var tempPath = path + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            using (var stream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(tempPath, path, true);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(AppSettings settings)
    {
        TrySave(settings);
    }
}
