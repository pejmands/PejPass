using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PejPass.Wpf.Tests.Services;

public sealed class FontScaleTests
{
    private static readonly Regex Reference = new(@"AppFontSize(\d+)", RegexOptions.Compiled);

    [Theory]
    [InlineData(FontSizeMode.Small, 12d / 13d)]
    [InlineData(FontSizeMode.Medium, 1d)]
    [InlineData(FontSizeMode.Large, 15d / 13d)]
    public void ScaleFor_ReturnsExpectedScale(FontSizeMode mode, double expected)
    {
        Assert.Equal(expected, FontScale.ScaleFor(mode), precision: 6);
    }

    [Fact]
    public void ResourceKey_IsCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
            Assert.Equal("AppFontSize26", FontScale.ResourceKey(26));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Steps_ContainSizeUsedByEntryTitle()
    {
        Assert.Contains(26d, FontScale.Steps);
    }

    [Fact]
    public void EveryAppFontSizeReference_IsDefinedInSteps()
    {
        var projectDirectory = FindWpfProjectDirectory();
        var defined = FontScale.Steps.Select(step => (int)step).ToHashSet();
        var missing = new SortedDictionary<int, List<string>>();

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var relative = Path.GetRelativePath(projectDirectory, file);
            if (IsBuildOutput(relative))
                continue;

            foreach (Match match in Reference.Matches(File.ReadAllText(file)))
            {
                var size = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (defined.Contains(size))
                    continue;

                if (!missing.TryGetValue(size, out var files))
                    missing[size] = files = [];

                if (!files.Contains(relative))
                    files.Add(relative);
            }
        }

        Assert.True(
            missing.Count == 0,
            "AppFontSize resources referenced but not defined in FontScale.Steps: " +
            string.Join("; ", missing.Select(item => $"{item.Key} ({string.Join(", ", item.Value)})")));
    }

    [Fact(Timeout = 15000)]
    public void ApplyFontSize_DefinesEveryStepForEveryMode()
    {
        WpfTestHost.Run(_ =>
        {
            var resources = System.Windows.Application.Current!.Resources;

            try
            {
                foreach (var mode in Enum.GetValues<FontSizeMode>())
                {
                    App.ApplyFontSize(mode);
                    var scale = FontScale.ScaleFor(mode);

                    foreach (var step in FontScale.Steps)
                    {
                        var value = Assert.IsType<double>(resources[FontScale.ResourceKey(step)]);
                        Assert.Equal(step * scale, value, precision: 6);
                    }

                    var defaultSize = Assert.IsType<double>(resources["AppFontSize"]);
                    Assert.Equal(FontScale.BaseSize * scale, defaultSize, precision: 6);
                }
            }
            finally
            {
                App.ApplyFontSize(FontSizeMode.Medium);
            }
        }, TestContext.Current.CancellationToken);
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var first = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindWpfProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "PejPass.Wpf");

            if (File.Exists(Path.Combine(directory.FullName, "PejPass.sln")) &&
                Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/PejPass.Wpf above the test output directory.");
    }
}
