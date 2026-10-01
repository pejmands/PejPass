using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using System.IO;
using System.Text;

namespace PejPass.Wpf.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _testDirectory;

    public SettingsStoreTests()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "PejPass.SettingsStoreTests",
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(_testDirectory);

        SettingsStore.SettingsPath = Path.Combine(
            _testDirectory,
            "settings.json");
    }

    [Fact]
    public void OnlineFaviconFetching_IsDisabledByDefault()
    {
        Assert.False(new AppSettings().OnlineFaviconFetchingEnabled);
    }

    private static readonly SemaphoreSlim SettingsFileLock = new(1, 1);

    [Fact]
    public async Task OlderSettingsWithoutOnlineFaviconProperty_LoadAsDisabled()
    {
        await SettingsFileLock.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            var path = SettingsStore.SettingsPath;

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

            await File.WriteAllTextAsync(
                path,
                json,
                Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var settings = SettingsStore.Load();

            Assert.False(settings.OnlineFaviconFetchingEnabled);
        }
        finally
        {
            SettingsFileLock.Release();
        }
    }

    [Fact]
    public void Load_RemovesStaleTempFile()
    {
        var path = SettingsStore.SettingsPath;
        var tempPath = path + ".tmp";

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllText(tempPath, "stale temp");

        SettingsStore.Load();

        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void Load_RemovesCorruptSettingsFilesOlderThan90Days()
    {
        var path = SettingsStore.SettingsPath;
        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var oldFile = Path.Combine(
            directory,
            "settings.corrupt-20250101-120000.json");

        var recentFile = Path.Combine(
            directory,
            "settings.corrupt-20260901-120000.json");

        try
        {
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(recentFile, "recent");

            SettingsStore.Load();

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(recentFile));
        }
        finally
        {
            if (File.Exists(oldFile))
                File.Delete(oldFile);

            if (File.Exists(recentFile))
                File.Delete(recentFile);
        }
    }

    [Fact]
    public void Load_KeepsMaximumFiveCorruptSettingsFiles()
    {
        var path = SettingsStore.SettingsPath;
        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var files = Enumerable.Range(1, 7)
            .Select(i => Path.Combine(
                directory,
                $"settings.corrupt-2026090{i}-000000.json"))
            .ToList();

        try
        {
            foreach (var file in files)
            {
                File.WriteAllText(file, "test");
            }

            SettingsStore.Load();

            var remaining = Directory.GetFiles(
                directory,
                "settings.corrupt-*.json");

            Assert.Equal(5, remaining.Length);

            Assert.False(File.Exists(files[0]));
            Assert.False(File.Exists(files[1]));

            for (var i = 2; i < files.Count; i++)
            {
                Assert.True(File.Exists(files[i]));
            }
        }
        finally
        {
            foreach (var file in files)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    [Fact]
    public void Load_WhenSettingsFileIsCorrupt_CreatesBackup()
    {
        var path = SettingsStore.SettingsPath;
        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var hadOriginalFile = File.Exists(path);
        var originalContent = hadOriginalFile
            ? File.ReadAllText(path)
            : null;

        var backupsBefore = Directory.GetFiles(
            directory,
            "settings.corrupt-*.json");

        var backupsCreated = new List<string>();

        try
        {
            File.WriteAllText(path, "{ invalid json");

            var settings = SettingsStore.Load();

            Assert.NotNull(settings);

            var backupsAfter = Directory.GetFiles(
                directory,
                "settings.corrupt-*.json");

            backupsCreated.AddRange(
                backupsAfter.Except(backupsBefore));

            Assert.Single(backupsCreated);
        }
        finally
        {
            foreach (var file in backupsCreated)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            if (hadOriginalFile)
            {
                File.WriteAllText(path, originalContent!);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(
                _testDirectory,
                true);
        }

        SettingsStore.SettingsPath =
            AppSettings.SettingsFilePath;
    }
}
