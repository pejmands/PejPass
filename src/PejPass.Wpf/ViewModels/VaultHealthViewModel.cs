using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
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

        Issues.Clear();
        _allIssues.Clear();
        TotalIssues = 0;
        DuplicateCount = 0;
        WeakCount = 0;
        MissingTotpCount = 0;
        StaleCount = 0;

        // Paint the cleared state before work starts
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
            // password → how many entries share it
            var passwordCounts = _entries
                .Where(e => !string.IsNullOrEmpty(e.Password))
                .GroupBy(e => e.Password, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var utc = DateTimeOffset.UtcNow;
            var filter = SelectedFilterIndex;

            for (var i = 0; i < n; i++)
            {
                var e = _entries[i];

                if (!string.IsNullOrEmpty(e.Password) &&
                    passwordCounts.TryGetValue(e.Password, out var peers) &&
                    peers > 1)
                {
                    AddIssue(new HealthIssue(
                        HealthIssueKind.DuplicatePassword,
                        e.Id,
                        e.Title,
                        peers == 2
                            ? "Same password used on 2 entries"
                            : $"Same password used on {peers} entries"),
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
                ScanStatus = $"Scanning {i + 1} / {n}";

                // No sleep — just let the dispatcher render pending UI changes.
                // Without this, a tight loop starves layout/render and everything jumps at the end.
                await YieldUiAsync().ConfigureAwait(true);
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
            Issues.Add(new HealthIssueRow(issue));
    }

    /// <summary>
    /// Returns control to the WPF dispatcher so bindings / layout / render can run.
    /// Not a timed delay — only pumps the message queue.
    /// </summary>
    private static async Task YieldUiAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            await Task.Yield();
            return;
        }

        // Background: after input, before idle — enough for ProgressBar + list to paint
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

    partial void OnSelectedFilterIndexChanged(int value)
    {
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
