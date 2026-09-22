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

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = AppSettings.SettingsFilePath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-critical — ignore persistence failures
        }
    }
}
