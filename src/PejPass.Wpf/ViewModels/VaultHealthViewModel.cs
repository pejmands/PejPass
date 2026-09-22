using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Domain.Health;
using PejPass.Domain.Security;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace PejPass.Wpf.ViewModels;

public partial class VaultHealthViewModel : ObservableObject
{
    private readonly IReadOnlyList<VaultEntry> _entries;

    [ObservableProperty]
    public partial int SelectedFilterIndex { get; set; }

    [ObservableProperty]
    public partial int TotalIssues { get; set; }

    [ObservableProperty]
    public partial int DuplicateCount { get; set; }

    [ObservableProperty]
    public partial int WeakCount { get; set; }

    [ObservableProperty]
    public partial int MissingTotpCount { get; set; }

    [ObservableProperty]
    public partial int StaleCount { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial double ScanProgress { get; set; }

    [ObservableProperty]
    public partial string ScanStatus { get; set; } = "Preparing…";

    public string[] FilterOptions { get; } =
    [
        "All",
        "Duplicate passwords",
        "Weak passwords",
        "Missing TOTP",
        "Stale (1+ year)"
    ];

    public ObservableCollection<HealthIssueRow> Issues { get; } = [];

    public event EventHandler<Guid>? RequestOpenEntry;

    private readonly List<HealthIssue> _allIssues = [];
    /// <summary>EntryId → stable group key (password hash) for sorting peers together.</summary>
    private readonly Dictionary<Guid, string> _duplicateGroupKeys = [];

    public VaultHealthViewModel(IEnumerable<VaultEntry> entries)
    {
        _entries = [.. entries];
        IsScanning = true;
        IsReady = false;
        ScanProgress = 0;
        ScanStatus = $"Scanning {_entries.Count} entries…";
    }

    partial void OnIsReadyChanged(bool value) => OpenEntryCommand.NotifyCanExecuteChanged();

    public async Task StartScanAsync()
    {
        IsScanning = true;
        IsReady = false;
        ScanProgress = 0;
        ScanStatus = $"Preparing {_entries.Count} entries…";

        Issues.Clear();
        _allIssues.Clear();
        _duplicateGroupKeys.Clear();
        TotalIssues = 0;
        DuplicateCount = 0;
        WeakCount = 0;
        MissingTotpCount = 0;
        StaleCount = 0;

        await YieldUiAsync().ConfigureAwait(true);

        var n = _entries.Count;
        if (n == 0)
        {
            ScanProgress = 100;
            ScanStatus = "No entries to scan.";
            IsScanning = false;
            IsReady = true;
            return;
        }

        try
        {
            // password → entries that share it (only groups with 2+)
            var byPassword = _entries
                .Where(e => !string.IsNullOrEmpty(e.Password))
                .GroupBy(e => e.Password, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // Stable group key per password (never store the password itself in rows)
            var groupKeyByPassword = byPassword.Keys.ToDictionary(
                p => p,
                p => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p)))[..16],
                StringComparer.Ordinal);

            var utc = DateTimeOffset.UtcNow;
            var filter = SelectedFilterIndex;

            for (var i = 0; i < n; i++)
            {
                var e = _entries[i];

                if (!string.IsNullOrEmpty(e.Password) &&
                    byPassword.TryGetValue(e.Password, out var group))
                {
                    var groupKey = groupKeyByPassword[e.Password];
                    _duplicateGroupKeys[e.Id] = groupKey;

                    AddIssue(new HealthIssue(
                        HealthIssueKind.DuplicatePassword,
                        e.Id,
                        string.IsNullOrWhiteSpace(e.Title) ? "(untitled)" : e.Title,
                        FormatDuplicateDetail(e, group)),
                        filter);
                }

                if (PasswordStrength.IsWeak(e.Password))
                {
                    var level = PasswordStrength.Evaluate(e.Password);
                    AddIssue(new HealthIssue(
                        HealthIssueKind.WeakPassword,
                        e.Id,
                        e.Title,
                        level == PasswordStrengthLevel.Empty
                            ? "Password is empty"
                            : $"Strength: {level}"),
                        filter);
                }

                if (string.IsNullOrWhiteSpace(e.TotpSecret))
                {
                    AddIssue(new HealthIssue(
                        HealthIssueKind.MissingTotp,
                        e.Id,
                        e.Title,
                        "No authenticator (TOTP) configured"),
                        filter);
                }

                if (utc - e.UpdatedAt >= VaultHealthAnalyzer.StaleThreshold)
                {
                    var days = (int)(utc - e.UpdatedAt).TotalDays;
                    AddIssue(new HealthIssue(
                        HealthIssueKind.StalePassword,
                        e.Id,
                        e.Title,
                        $"Last updated {days} days ago"),
                        filter);
                }

                ScanProgress = (i + 1) * 100.0 / n;
                ScanStatus = $"Scanning {i + 1} / {n}…";

                // Yield often enough for progress to paint on large vaults
                if ((i + 1) % 5 == 0 || i + 1 == n)
                    await YieldUiAsync().ConfigureAwait(true);
            }

            ScanProgress = 100;
            ScanStatus = TotalIssues == 0
                ? "No issues found."
                : $"{TotalIssues} issue(s) found.";

            // Final ordered view (peers of same password sit together)
            RebuildVisibleIssues(SelectedFilterIndex);
        }
        finally
        {
            IsScanning = false;
            IsReady = true;
        }
    }

    /// <summary>
    /// Names the other account(s) that share this password so the user can find them.
    /// </summary>
    private static string FormatDuplicateDetail(VaultEntry entry, List<VaultEntry> group)
    {
        var others = group
            .Where(x => x.Id != entry.Id)
            .Select(x => string.IsNullOrWhiteSpace(x.Title) ? "(untitled)" : x.Title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (others.Count == 0)
            return "Same password used on multiple entries";

        if (others.Count == 1)
            return $"Same password as «{others[0]}»";

        const int maxShow = 4;
        if (others.Count <= maxShow)
            return "Also used by: " + string.Join(", ", others.Select(t => $"«{t}»"));

        var shown = others.Take(maxShow).Select(t => $"«{t}»");
        return "Also used by: " + string.Join(", ", shown) + $", +{others.Count - maxShow} more";
    }

    private void AddIssue(HealthIssue issue, int filter)
    {
        _allIssues.Add(issue);

        switch (issue.Kind)
        {
            case HealthIssueKind.DuplicatePassword: DuplicateCount++; break;
            case HealthIssueKind.WeakPassword: WeakCount++; break;
            case HealthIssueKind.MissingTotp: MissingTotpCount++; break;
            case HealthIssueKind.StalePassword: StaleCount++; break;
        }

        TotalIssues = _allIssues.Count;

        if (MatchesFilter(issue, filter))
            Issues.Add(new HealthIssueRow(issue, GroupKeyFor(issue)));
    }

    private string GroupKeyFor(HealthIssue issue)
    {
        if (issue.Kind == HealthIssueKind.DuplicatePassword &&
            _duplicateGroupKeys.TryGetValue(issue.EntryId, out var key))
            return key;

        // Non-duplicates: stable per-entry so sort is predictable
        return issue.EntryId.ToString("N");
    }

    private void RebuildVisibleIssues(int filterIndex)
    {
        Issues.Clear();
        foreach (var i in OrderedIssues(filterIndex))
            Issues.Add(new HealthIssueRow(i, GroupKeyFor(i)));
    }

    private IEnumerable<HealthIssue> OrderedIssues(int filterIndex)
    {
        return _allIssues
            .Where(x => MatchesFilter(x, filterIndex))
            .OrderBy(x => GroupKeyFor(x), StringComparer.Ordinal)
            .ThenBy(x => x.EntryTitle, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task YieldUiAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            await Task.Yield();
            return;
        }

        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private static bool MatchesFilter(HealthIssue issue, int filterIndex) => filterIndex switch
    {
        1 => issue.Kind == HealthIssueKind.DuplicatePassword,
        2 => issue.Kind == HealthIssueKind.WeakPassword,
        3 => issue.Kind == HealthIssueKind.MissingTotp,
        4 => issue.Kind == HealthIssueKind.StalePassword,
        _ => true
    };

    partial void OnSelectedFilterIndexChanged(int value) => RebuildVisibleIssues(value);

    [RelayCommand]
    private async Task RefreshAsync() => await StartScanAsync();

    private bool CanOpenEntry(HealthIssueRow? row) => IsReady && row is not null;

    [RelayCommand(CanExecute = nameof(CanOpenEntry))]
    private void OpenEntry(HealthIssueRow? row)
    {
        if (!IsReady || row is null) return;
        RequestOpenEntry?.Invoke(this, row.EntryId);
    }
}

public sealed class HealthIssueRow
{
    public Guid EntryId { get; }
    public string EntryTitle { get; }
    public string KindLabel { get; }
    public string Detail { get; }
    public string SeverityColor { get; }
    public string GroupKey { get; }

    public HealthIssueRow(HealthIssue issue, string groupKey)
    {
        EntryId = issue.EntryId;
        EntryTitle = issue.EntryTitle;
        Detail = issue.Detail;
        GroupKey = groupKey;

        (KindLabel, SeverityColor) = issue.Kind switch
        {
            HealthIssueKind.DuplicatePassword => ("Duplicate", "#F38BA8"),
            HealthIssueKind.WeakPassword => ("Weak", "#F9E2AF"),
            HealthIssueKind.MissingTotp => ("No TOTP", "#89B4FA"),
            HealthIssueKind.StalePassword => ("Stale", "#A6ADC8"),
            _ => ("Issue", "#CDD6F4")
        };
    }
}
