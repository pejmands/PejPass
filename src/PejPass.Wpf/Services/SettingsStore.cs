using PejPass.Domain.Settings;
using PejPass.Wpf.Controls;
using System.IO;
using System.Text.Json;

namespace PejPass.Wpf.Services;

public static class SettingsStore
{
    private const int MaxAutoLockMinutes = 1440;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        var path = AppSettings.SettingsFilePath;

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
            var settings = new AppSettings();
            TrySave(settings);
            return settings;
        }
    }

    private static bool Normalize(JsonElement root, AppSettings settings)
    {
        var defaults = new AppSettings();
        var needsSave = false;

        if (root.TryGetProperty("lastVaultPath", out var lastVaultPath) &&
            (lastVaultPath.ValueKind is JsonValueKind.String or JsonValueKind.Null))
        {
            settings.LastVaultPath = lastVaultPath.ValueKind == JsonValueKind.Null
                ? null
                : lastVaultPath.GetString();
        }
        else
        {
            settings.LastVaultPath = defaults.LastVaultPath;
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

    public static void EnsureWritable()
    {
        var path = AppSettings.SettingsFilePath;
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
            var path = AppSettings.SettingsFilePath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
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
