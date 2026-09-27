using PejPass.Domain.Settings;
using System.IO;
using System.Text.Json;

namespace PejPass.Wpf.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        try
        {
            var path = AppSettings.SettingsFilePath;
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
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
