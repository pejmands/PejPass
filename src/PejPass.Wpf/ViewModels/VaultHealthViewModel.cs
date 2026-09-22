using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Domain.Health;
using PejPass.Domain.Security;

namespace PejPass.Wpf.ViewModels;

public partial class VaultHealthViewModel : ObservableObject
{
    private readonly IReadOnlyList<VaultEntry> _entries;

    [ObservableProperty] private int _selectedFilterIndex;
    [ObservableProperty] private int _totalIssues;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _weakCount;
    [ObservableProperty] private int _missingTotpCount;
    [ObservableProperty] private int _staleCount;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _scanStatus = "Preparing…";

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

    /// <summary>Full unfiltered issue list from last completed (or in-progress) scan.</summary>
    private readonly List<HealthIssue> _allIssues = [];

    public VaultHealthViewModel(IEnumerable<VaultEntry> entries)
    {
        _entries = entries.ToList();
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

        // Clear UI immediately so Rescan feels responsive
        Issues.Clear();
        _allIssues.Clear();
        TotalIssues = 0;
        DuplicateCount = 0;
        WeakCount = 0;
        MissingTotpCount = 0;
        StaleCount = 0;

        // Let the cleared UI paint
        await Task.Yield();

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
            // Precompute which passwords are shared (fast dictionary pass)
            var sharedPasswords = _entries
                .Where(e => !string.IsNullOrEmpty(e.Password))
                .GroupBy(e => e.Password, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);

            var utc = DateTimeOffset.UtcNow;
            var filter = SelectedFilterIndex;

            for (var i = 0; i < n; i++)
            {
                var e = _entries[i];
                var found = new List<HealthIssue>();

                // Duplicates
                if (!string.IsNullOrEmpty(e.Password) && sharedPasswords.Contains(e.Password))
                {
                    var peers = _entries.Count(x =>
                        string.Equals(x.Password, e.Password, StringComparison.Ordinal));
                    found.Add(new HealthIssue(
                        HealthIssueKind.DuplicatePassword,
                        e.Id,
                        e.Title,
                        peers == 2
                            ? "Same password used on 2 entries"
                            : $"Same password used on {peers} entries"));
                }

                // Weak
                if (PasswordStrength.IsWeak(e.Password))
                {
                    var level = PasswordStrength.Evaluate(e.Password);
                    found.Add(new HealthIssue(
                        HealthIssueKind.WeakPassword,
                        e.Id,
                        e.Title,
                        level == PasswordStrengthLevel.Empty
                            ? "Password is empty"
                            : $"Strength: {level}"));
                }

                // Missing TOTP
                if (string.IsNullOrWhiteSpace(e.TotpSecret))
                {
                    found.Add(new HealthIssue(
                        HealthIssueKind.MissingTotp,
                        e.Id,
                        e.Title,
                        "No authenticator (TOTP) configured"));
                }

                // Stale
                if (utc - e.UpdatedAt >= VaultHealthAnalyzer.StaleThreshold)
                {
                    var days = (int)(utc - e.UpdatedAt).TotalDays;
                    found.Add(new HealthIssue(
                        HealthIssueKind.StalePassword,
                        e.Id,
                        e.Title,
                        $"Last updated {days} days ago"));
                }

                // Commit issues for this entry to UI (one-by-one)
                foreach (var issue in found)
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
                        Issues.Add(new HealthIssueRow(issue));
                }

                // Live progress
                ScanProgress = (i + 1) * 100.0 / n;
                ScanStatus = $"Scanning {i + 1} / {n}";

                // Explicit delay so progress is visible (requested for diagnosis)
                await Task.Delay(100).ConfigureAwait(true);
            }

            ScanProgress = 100;
            ScanStatus = TotalIssues == 0
                ? "No issues found."
                : $"{TotalIssues} issue(s) found.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Scan failed: {ex.Message}";
            ScanProgress = 0;
        }
        finally
        {
            IsScanning = false;
            IsReady = true;
        }
    }

    private static bool MatchesFilter(HealthIssue issue, int filterIndex) => filterIndex switch
    {
        1 => issue.Kind == HealthIssueKind.DuplicatePassword,
        2 => issue.Kind == HealthIssueKind.WeakPassword,
        3 => issue.Kind == HealthIssueKind.MissingTotp,
        4 => issue.Kind == HealthIssueKind.StalePassword,
        _ => true
    };

    partial void OnSelectedFilterIndexChanged(int value)
    {
        // Live filter against accumulated issues (works mid-scan and after)
        Issues.Clear();
        foreach (var i in _allIssues
                     .Where(x => MatchesFilter(x, value))
                     .OrderBy(x => x.EntryTitle, StringComparer.OrdinalIgnoreCase))
        {
            Issues.Add(new HealthIssueRow(i));
        }
    }

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

    public HealthIssueRow(HealthIssue issue)
    {
        EntryId = issue.EntryId;
        EntryTitle = issue.EntryTitle;
        Detail = issue.Detail;

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
