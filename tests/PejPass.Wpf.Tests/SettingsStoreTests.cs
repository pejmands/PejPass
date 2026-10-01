using System.Text;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void OnlineFaviconFetching_IsDisabledByDefault()
    {
        Assert.False(new AppSettings().OnlineFaviconFetchingEnabled);
    }

    private static readonly SemaphoreSlim SettingsFileLock = new(1, 1);

    [Fact]
    public async Task OlderSettingsWithoutOnlineFaviconProperty_LoadAsDisabled()
    {
        await SettingsFileLock.WaitAsync();

        var path = AppSettings.SettingsFilePath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        byte[]? original = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;

        try
        {
            const string json = """
                {
                  "lastVaultPath": null,
                  "autoLockMinutes": 10,
                  "clipboardClearSeconds": 30,
                  "revealSecretSeconds": 10,
                  "theme": 0,
                  "fontSize": 1,
                  "zoom": 1,
                  "sortMode": 0,
                  "windowsHelloEnabled": true
                }
                """;

            await File.WriteAllTextAsync(path, json, Encoding.UTF8);

            var settings = SettingsStore.Load();

            Assert.False(settings.OnlineFaviconFetchingEnabled);
        }
        finally
        {
            if (original is null)
                File.Delete(path);
            else
                await File.WriteAllBytesAsync(path, original);

            SettingsFileLock.Release();
        }
    }
}
