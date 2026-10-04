namespace PejPass.Wpf.Services;

/// <summary>
/// Identity of the <em>installed</em> build.
/// Version and release date are baked into the binary at publish time.
/// The remote update.json is a separate source for "what is latest on the server".
/// </summary>
public static class AppInfoService
{
    public const string Name = "PejPass";

    /// <summary>
    /// Version of this running build. Bump when you publish.
    /// </summary>
    public const string Version = "0.1.0";

    /// <summary>
    /// Human-readable release date of this build (shown on About).
    /// </summary>
    public const string ReleaseDate = "October 4, 2026";

    public const string RepositoryUrl =
        "https://github.com/pejmands/PejPass";

    /// <summary>
    /// PejTools-style manifest URL.
    /// Local test: http://localhost/update.json
    /// Production: e.g. https://pejmands.github.io/pejpass/update.json
    /// </summary>
    public const string UpdateManifestUrl =
        "http://localhost/update.json";
}
