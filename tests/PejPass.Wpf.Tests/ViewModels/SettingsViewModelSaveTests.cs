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
                (bool enabled, out string? error) =>
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
                (bool enabled, out string? error) =>
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
                (bool enabled, out string? error) =>
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
    public void Save_InAppearanceOnlyMode_NeverTouchesStartupRegistration()
    {
        WpfTestHost.Run(host =>
        {
            var calls = new List<bool>();
            var viewModel = CreateViewModel(
                host,
                (bool enabled, out string? error) =>
                {
                    calls.Add(enabled);
                    error = null;
                    return true;
                },
                []);
            var closed = 0;
            viewModel.RequestClose += (_, _) => closed++;

            viewModel.ConfigureAppearanceOnly(true);
            viewModel.StartWithWindows = true;

            viewModel.SaveCommand.Execute(null);

            Assert.Empty(calls);
            Assert.False(host.Settings.StartWithWindows);
            Assert.Equal(1, closed);
        }, TestContext.Current.CancellationToken);
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
