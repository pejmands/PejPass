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

public static class VaultHealthAnalyzer
{
    /// <summary>Password not updated for this long is considered stale.</summary>
    public static TimeSpan StaleThreshold { get; } = TimeSpan.FromDays(365);

    public static VaultHealthReport Analyze(IEnumerable<VaultEntry> entries, DateTimeOffset? now = null)
    {
        var list = entries.ToList();
        var report = new VaultHealthReport();
        var utc = now ?? DateTimeOffset.UtcNow;

        // Duplicate passwords (same non-empty password used on 2+ entries)
        var byPassword = list
            .Where(e => !string.IsNullOrEmpty(e.Password))
            .GroupBy(e => e.Password, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

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

        foreach (var e in list)
        {
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
        }

        return report;
    }
}
