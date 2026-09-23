using PejPass.Domain.Entities;
using PejPass.Domain.Security;

namespace PejPass.Domain.Health;

public enum HealthIssueKind
{
    DuplicatePassword,
    WeakPassword,
    UsernameEqualsPassword,
    MissingTotp,
    StalePassword
}

public sealed record HealthIssue(
    HealthIssueKind Kind,
    Guid EntryId,
    string EntryTitle,
    string Detail);

public sealed class VaultHealthReport
{
    public List<HealthIssue> Issues { get; } = [];

    public int DuplicateCount => Issues.Count(i => i.Kind == HealthIssueKind.DuplicatePassword);
    public int WeakCount => Issues.Count(i => i.Kind == HealthIssueKind.WeakPassword);
    public int UsernameEqualsPasswordCount => Issues.Count(i => i.Kind == HealthIssueKind.UsernameEqualsPassword);
    public int MissingTotpCount => Issues.Count(i => i.Kind == HealthIssueKind.MissingTotp);
    public int StaleCount => Issues.Count(i => i.Kind == HealthIssueKind.StalePassword);

    public int Total => Issues.Count;
}

public sealed record HealthScanProgress(double Percent, string Status);

public static class VaultHealthAnalyzer
{
    public static TimeSpan StaleThreshold { get; } = TimeSpan.FromDays(365);

    public static VaultHealthReport Analyze(IEnumerable<VaultEntry> entries, DateTimeOffset? now = null)
        => Analyze(entries, progress: null, now);

    public static VaultHealthReport Analyze(
        IEnumerable<VaultEntry> entries,
        IProgress<HealthScanProgress>? progress,
        DateTimeOffset? now = null)
    {
        var list = entries.ToList();
        var report = new VaultHealthReport();
        var utc = now ?? DateTimeOffset.UtcNow;
        var n = list.Count;

        if (n == 0)
        {
            progress?.Report(new HealthScanProgress(100, "No entries to scan."));
            return report;
        }

        progress?.Report(new HealthScanProgress(1, "Checking duplicate passwords…"));

        var byPassword = list
            .Where(e => !string.IsNullOrEmpty(e.Password))
            .GroupBy(e => e.Password, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in byPassword)
        {
            var members = group.ToList();
            foreach (var e in members)
            {
                var others = members
                    .Where(x => x.Id != e.Id)
                    .Select(x => string.IsNullOrWhiteSpace(x.Title) ? "(untitled)" : x.Title.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string detail;
                if (others.Count == 0)
                    detail = "Same password used on multiple entries";
                else if (others.Count == 1)
                    detail = $"Same password as «{others[0]}»";
                else if (others.Count <= 4)
                    detail = "Also used by: " + string.Join(", ", others.Select(n => $"«{n}»"));
                else
                    detail = "Also used by: " + string.Join(", ", others.Take(4).Select(n => $"«{n}»"))
                             + $", +{others.Count - 4} more";

                report.Issues.Add(new HealthIssue(
                    HealthIssueKind.DuplicatePassword,
                    e.Id,
                    e.Title,
                    detail));
            }
        }

        progress?.Report(new HealthScanProgress(25, "Checking password strength…"));

        for (var i = 0; i < n; i++)
        {
            var e = list[i];

            if (PasswordStrength.IsWeak(e.Password))
            {
                var level = PasswordStrength.Evaluate(e.Password);
                report.Issues.Add(new HealthIssue(
                    HealthIssueKind.WeakPassword,
                    e.Id,
                    e.Title,
                    level == PasswordStrengthLevel.Empty
                        ? "Password is empty"
                        : $"Strength: {level}"));
            }

            if (!string.IsNullOrEmpty(e.Username) &&
                !string.IsNullOrEmpty(e.Password) &&
                string.Equals(e.Username, e.Password, StringComparison.OrdinalIgnoreCase))
            {
                report.Issues.Add(new HealthIssue(
                    HealthIssueKind.UsernameEqualsPassword,
                    e.Id,
                    e.Title,
                    "Username and password are the same"));
            }

            if (string.IsNullOrWhiteSpace(e.TotpSecret))
            {
                report.Issues.Add(new HealthIssue(
                    HealthIssueKind.MissingTotp,
                    e.Id,
                    e.Title,
                    "No authenticator (TOTP) configured"));
            }

            if (utc - e.UpdatedAt >= StaleThreshold)
            {
                var days = (int)(utc - e.UpdatedAt).TotalDays;
                report.Issues.Add(new HealthIssue(
                    HealthIssueKind.StalePassword,
                    e.Id,
                    e.Title,
                    $"Last updated {days} days ago"));
            }

            var pct = 25 + (i + 1) * 75.0 / n;
            progress?.Report(new HealthScanProgress(pct, $"Scanning {i + 1} / {n}…"));
        }

        progress?.Report(new HealthScanProgress(100,
            report.Total == 0 ? "No issues found." : $"{report.Total} issue(s) found."));

        return report;
    }
}
