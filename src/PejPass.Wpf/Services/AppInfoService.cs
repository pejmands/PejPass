using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace PejPass.Wpf.Services;

/// <summary>
/// Identity of the running build, read from PejPass.exe metadata.
/// Version matches what Windows shows in File Properties.
/// Release date uses the executable's last-write time.
/// </summary>
public static class AppInfoService
{
    public const string Name = "PejPass";

    public const string RepositoryUrl =
        "https://github.com/pejmands/PejPass";

    /// <summary>
    /// Manifest URL.
    /// Local test: http://localhost/update.json
    /// </summary>
    public const string UpdateManifestUrl =
        "https://pejmands.github.io/PejPass/update.json";

    private static readonly Lazy<(string Version, string ReleaseDate)> FileInfo =
        new(ReadFromExecutable);

    /// <summary>
    /// Product version of the running executable (e.g. "0.1.0").
    /// </summary>
    public static string Version => FileInfo.Value.Version;

    /// <summary>
    /// Human-readable date from the executable file timestamp.
    /// </summary>
    public static string ReleaseDate => FileInfo.Value.ReleaseDate;

    private static (string Version, string ReleaseDate) ReadFromExecutable()
    {
        try
        {
            var path = ResolveExecutablePath();

            if (path is not null && File.Exists(path))
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                var version = PickVersion(fvi);
                var releaseDate = File.GetLastWriteTime(path)
                    .ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

                return (version, releaseDate);
            }

            // Fallback: assembly attributes (still from the loaded binary)
            var asm = Assembly.GetExecutingAssembly();
            var informational = asm
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            var versionFromAsm = NormalizeVersion(informational)
                ?? NormalizeVersion(asm.GetName().Version?.ToString())
                ?? "0.0.0";

            return (versionFromAsm, string.Empty);
        }
        catch
        {
            return ("0.0.0", string.Empty);
        }
    }

    private static string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath)
            && File.Exists(Environment.ProcessPath))
            return Environment.ProcessPath;

        var location = Assembly.GetExecutingAssembly().Location;
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    private static string PickVersion(FileVersionInfo fvi)
    {
        // Prefer ProductVersion (InformationalVersion), then FileVersion.
        var raw = FirstNonEmpty(fvi.ProductVersion, fvi.FileVersion);
        return NormalizeVersion(raw) ?? "0.0.0";
    }

    private static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var v = raw.Trim();

        // Strip SourceLink / commit suffix: "0.1.0+abc123" → "0.1.0"
        var plus = v.IndexOf('+');
        if (plus > 0)
            v = v[..plus];

        // Strip leading v
        if (v.StartsWith('v') || v.StartsWith('V'))
            v = v[1..];

        // Display 0.1.0.0 as 0.1.0 when revision is zero.
        // Fully qualify System.Version — property "Version" would shadow it.
        if (System.Version.TryParse(v, out var parsed)
            && parsed.Revision == 0)
        {
            return parsed.Build <= 0
                ? $"{parsed.Major}.{parsed.Minor}"
                : $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        }

        return v;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
