using System.Globalization;
using System.IO;

namespace PejPass.Wpf.Services;

internal static class PublicSuffixList
{
    private const string ResourceName = "PejPass.Wpf.Data.public_suffix_list.dat";
    private static readonly Lazy<Rules> CachedRules = new(LoadRules, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static int GetPublicSuffixLength(string[] labels)
    {
        var rules = CachedRules.Value;
        var publicSuffixLength = 1;
        var exceptionLength = 0;

        for (var start = 0; start < labels.Length; start++)
        {
            var suffix = string.Join(".", labels[start..]);
            var length = labels.Length - start;

            if (rules.Exceptions.Contains(suffix) && length > exceptionLength)
                exceptionLength = length;

            if (rules.Exact.Contains(suffix) && length > publicSuffixLength)
                publicSuffixLength = length;

            if (start + 1 < labels.Length)
            {
                var wildcardSuffix = string.Join(".", labels[(start + 1)..]);
                if (rules.Wildcards.Contains(wildcardSuffix) && length > publicSuffixLength)
                    publicSuffixLength = length;
            }
        }

        return exceptionLength > 0 ? exceptionLength - 1 : publicSuffixLength;
    }

    private static Rules LoadRules()
    {
        using var stream = typeof(PublicSuffixList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Public Suffix List resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var wildcards = new HashSet<string>(StringComparer.Ordinal);
        var exceptions = new HashSet<string>(StringComparer.Ordinal);
        var idn = new IdnMapping();

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var target = exact;
            if (line.StartsWith("!"))
            {
                target = exceptions;
                line = line[1..];
            }
            else if (line.StartsWith("*.", StringComparison.Ordinal))
            {
                target = wildcards;
                line = line[2..];
            }

            try
            {
                target.Add(string.Join(".", line.Split('.').Select(label => idn.GetAscii(label))).ToLowerInvariant());
            }
            catch (ArgumentException)
            {
                // Ignore malformed rules rather than preventing the vault UI from starting.
            }
        }

        return new Rules(exact, wildcards, exceptions);
    }

    private sealed record Rules(
        HashSet<string> Exact,
        HashSet<string> Wildcards,
        HashSet<string> Exceptions);
}
