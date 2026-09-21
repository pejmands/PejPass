using PejPass.Domain.Entities;
using PejPass.Domain.Security;

namespace PejPass.Domain.Health;

public enum HealthIssueKind
{
    DuplicatePassword,
    WeakPassword,
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
    public int MissingTotpCount => Issues.Count(i => i.Kind == HealthIssueKind.MissingTotp);
    public int StaleCount => Issues.Count(i => i.Kind == HealthIssueKind.StalePassword);

    public int Total => Issues.Count;
}

/// <summary>0–100 progress with a short status label.</summary>
public sealed record HealthScanProgress(double Percent, string Status);

public static class VaultHealthAnalyzer
{
    /// <summary>Password not updated for this long is considered stale.</summary>
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

        // Phase 1 (0–25%): find duplicate passwords
        progress?.Report(new HealthScanProgress(2, "Checking duplicate passwords…"));

        var byPassword = list
            .Where(e => !string.IsNullOrEmpty(e.Password))
            .GroupBy(e => e.Password, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in byPassword)
        {
            var titles = string.Join(", ", group.Select(e => e.Title));
            foreach (var e in group)
            {
                report.Issues.Add(new HealthIssue(
                    HealthIssueKind.DuplicatePassword,
                    e.Id,
                    e.Title,
                    $"Same password as: {titles}"));
            }
        }

        progress?.Report(new HealthScanProgress(25, "Checking strength, TOTP, age…"));

        // Phase 2 (25–100%): per-entry checks with smooth progress
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

            // Update UI every ~2% or every 10 items (whichever comes first)
            var pct = 25 + (i + 1) * 75.0 / n;
            if (i == n - 1 || (i + 1) % Math.Max(1, n / 40) == 0)
            {
                progress?.Report(new HealthScanProgress(
                    Math.Min(99, pct),
                    $"Scanning {i + 1} / {n}…"));
            }
        }

        progress?.Report(new HealthScanProgress(100, "Done."));
        return report;
    }
}
