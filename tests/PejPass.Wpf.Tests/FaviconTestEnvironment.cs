using System.Runtime.CompilerServices;

namespace PejPass.Wpf.Tests;

internal static class FaviconTestEnvironment
{
    private static string? _testAppDataDirectory;

    [ModuleInitializer]
    internal static void Initialize()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PejPass-FaviconServiceTests",
            Guid.NewGuid().ToString("N"));

        _testAppDataDirectory = root;
        Environment.SetEnvironmentVariable("PEJPASS_APPDATA_DIR", root);

        AppDomain.CurrentDomain.ProcessExit += Cleanup;
    }

    private static void Cleanup(object? sender, EventArgs e)
    {
        var root = _testAppDataDirectory;
        if (string.IsNullOrWhiteSpace(root))
            return;

        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Cleanup is best-effort and must not mask test results.
        }
    }
}
