using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class SettingsViewModelSaveTests : IDisposable
{
    private readonly string _directory;
    private readonly string _previousSettingsPath;

    public SettingsViewModelSaveTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "PejPass.SettingsSaveTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);

        _previousSettingsPath = SettingsStore.SettingsPath;
        SettingsStore.SettingsPath = Path.Combine(_directory, "settings.json");
    }

    [Fact(Timeout = 15000)]
    public void Save_WhenWindowsHelloIsEnabledDuringActiveSession_CachesCurrentVaultKey()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.WindowsHelloEnabled = false;

            using var session = new VaultSession();
            using var cache = new SessionPasswordCache(host.Settings, TimeProvider.System);
            using var material = CreateMaterial();
            var vaultPath = Path.Combine(_directory, "active-vault.pejpass");
            session.Open(new Vault(), vaultPath, material);

            var viewModel = new SettingsViewModel(
                host.Settings,
                new ThemeService(host.Settings),
                null!,
                session,
                cache,
                (_, _) => { },
                SucceedingStartup)
            {
                WindowsHelloAvailable = true,
                WindowsHelloEnabled = true
            };

            viewModel.SaveCommand.Execute(null);

            Assert.True(cache.HasCacheFor(vaultPath));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Save_WhenStartupRegistrationFails_WritesNothingAndKeepsOtherEdits()
    {
        WpfTestHost.Run(host =>
        {
            var errors = new List<(string Message, string Title)>();
            var viewModel = CreateViewModel(host, FailingStartup, errors);
            var closed = 0;
            viewModel.RequestClose += (_, _) => closed++;

            viewModel.StartWithWindows = true;
            viewModel.AutoCheckForUpdates = true;

            viewModel.SaveCommand.Execute(null);

            Assert.False(File.Exists(SettingsStore.SettingsPath));
            Assert.False(host.Settings.StartWithWindows);
            Assert.False(host.Settings.AutoCheckForUpdates);

            Assert.False(viewModel.StartWithWindows);
            Assert.True(viewModel.AutoCheckForUpdates);

            Assert.Equal(0, closed);
            Assert.Single(errors);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Save_WhenStartupSucceeds_RegistersOnceAndPersists()
    {
        WpfTestHost.Run(host =>
        {
            var calls = new List<bool>();
            var errors = new List<(string Message, string Title)>();
            var viewModel = CreateViewModel(
                host,
                (enabled, out error) =>
                {
                    calls.Add(enabled);
                    error = null;
                    return true;
                },
                errors);
            var closed = 0;
            viewModel.RequestClose += (_, _) => closed++;

            viewModel.StartWithWindows = true;

            viewModel.SaveCommand.Execute(null);

            Assert.Equal([true], calls);
            Assert.True(host.Settings.StartWithWindows);
            Assert.True(SettingsStore.Load().StartWithWindows);
            Assert.Equal(1, closed);
            Assert.Empty(errors);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Save_WhenStartupUnchanged_DoesNotTouchRegistry()
    {
        WpfTestHost.Run(host =>
        {
            var calls = new List<bool>();
            var viewModel = CreateViewModel(
                host,
                (enabled, out error) =>
                {
                    calls.Add(enabled);
                    error = null;
                    return true;
                },
                []);

            viewModel.SaveCommand.Execute(null);

            Assert.Empty(calls);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Save_WhenWritingSettingsFails_RollsBackStartupRegistration()
    {
        WpfTestHost.Run(host =>
        {
            // A directory at the settings-file path makes TrySave fail after registration.
            Directory.CreateDirectory(SettingsStore.SettingsPath);

            var calls = new List<bool>();
            var errors = new List<(string Message, string Title)>();
            var viewModel = CreateViewModel(
                host,
                (enabled, out error) =>
                {
                    calls.Add(enabled);
                    error = null;
                    return true;
                },
                errors);
            var closed = 0;
            viewModel.RequestClose += (_, _) => closed++;

            viewModel.StartWithWindows = true;

            viewModel.SaveCommand.Execute(null);

            Assert.Equal([true, false], calls);
            Assert.False(host.Settings.StartWithWindows);
            Assert.False(viewModel.StartWithWindows);
            Assert.Equal(0, closed);
            Assert.Single(errors);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Save_InAppearanceOnlyMode_DoesNotTouchStartupOrHiddenSettings()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.AutoCheckForUpdates = true;
            host.Settings.MinimizeToSystemTray = true;
            host.Settings.CloseToSystemTray = true;
            host.Settings.LockVaultWhenHiddenToTray = true;

            var calls = new List<bool>();
            var viewModel = CreateViewModel(
                host,
                (enabled, out error) =>
                {
                    calls.Add(enabled);
                    error = null;
                    return true;
                },
                []);
            var closed = 0;
            viewModel.RequestClose += (_, _) => closed++;

            viewModel.ConfigureAppearanceOnly(true);
            viewModel.AutoCheckForUpdates = false;
            viewModel.MinimizeToSystemTray = false;
            viewModel.CloseToSystemTray = false;
            viewModel.LockVaultWhenHiddenToTray = false;
            viewModel.StartWithWindows = true;

            viewModel.SaveCommand.Execute(null);

            Assert.Empty(calls);
            Assert.False(host.Settings.StartWithWindows);
            Assert.True(host.Settings.AutoCheckForUpdates);
            Assert.True(host.Settings.MinimizeToSystemTray);
            Assert.True(host.Settings.CloseToSystemTray);
            Assert.True(host.Settings.LockVaultWhenHiddenToTray);

            var saved = SettingsStore.Load();
            Assert.True(saved.AutoCheckForUpdates);
            Assert.True(saved.MinimizeToSystemTray);
            Assert.True(saved.CloseToSystemTray);
            Assert.True(saved.LockVaultWhenHiddenToTray);
            Assert.False(saved.StartWithWindows);
            Assert.Equal(1, closed);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Save_LockVaultWhenHiddenToTray_PersistsDisabledValue()
    {
        WpfTestHost.Run(host =>
        {
            var viewModel = CreateViewModel(host, SucceedingStartup, []);
            viewModel.LockVaultWhenHiddenToTray = false;

            viewModel.SaveCommand.Execute(null);

            Assert.False(host.Settings.LockVaultWhenHiddenToTray);
            Assert.False(SettingsStore.Load().LockVaultWhenHiddenToTray);
        }, TestContext.Current.CancellationToken);
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = Enumerable.Repeat((byte)0x31, 32).ToArray();
        var salt = Enumerable.Repeat((byte)0x32, 16).ToArray();

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(salt);
        }
    }

    private static bool SucceedingStartup(bool enabled, out string? error)
    {
        error = null;
        return true;
    }

    private static SettingsViewModel CreateViewModel(
        WpfTestHost host,
        StartupRegistrar startup,
        List<(string Message, string Title)> errors) =>
        new(
            host.Settings,
            new ThemeService(host.Settings),
            null!,
            new VaultSession(),
            new SessionPasswordCache(host.Settings, TimeProvider.System),
            (message, title) => errors.Add((message, title)),
            startup);

    private static bool FailingStartup(bool enabled, out string? error)
    {
        error = "Simulated registry failure.";
        return false;
    }

    public void Dispose()
    {
        SettingsStore.SettingsPath = _previousSettingsPath;

        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }
}
