using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Entities;
using PejPass.Domain.Health;

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

    private VaultHealthReport _report = new();

    public VaultHealthViewModel(IEnumerable<VaultEntry> entries)
    {
        _entries = entries.ToList();
        IsScanning = true;
        IsReady = false;
        ScanProgress = 0;
        ScanStatus = $"Scanning {_entries.Count} entries…";
    }

    public async Task StartScanAsync()
    {
        IsScanning = true;
        IsReady = false;
        ScanProgress = 8;
        ScanStatus = $"Scanning {_entries.Count} entries…";

        await Task.Yield();

        try
        {
            var report = await Task.Run(() => VaultHealthAnalyzer.Analyze(_entries)).ConfigureAwait(true);

            ScanProgress = 92;
            ScanStatus = "Building report…";
            await Task.Yield();

            _report = report;
            TotalIssues = _report.Total;
            DuplicateCount = _report.DuplicateCount;
            WeakCount = _report.WeakCount;
            MissingTotpCount = _report.MissingTotpCount;
            StaleCount = _report.StaleCount;
            ApplyFilter();

            ScanProgress = 100;
            ScanStatus = _report.Total == 0
                ? "No issues found."
                : $"{_report.Total} issue(s) found.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            IsReady = true;
        }
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        if (IsReady)
            ApplyFilter();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await StartScanAsync();

    private void ApplyFilter()
    {
        Issues.Clear();
        IEnumerable<HealthIssue> source = _report.Issues;

        source = SelectedFilterIndex switch
        {
            1 => source.Where(i => i.Kind == HealthIssueKind.DuplicatePassword),
            2 => source.Where(i => i.Kind == HealthIssueKind.WeakPassword),
            3 => source.Where(i => i.Kind == HealthIssueKind.MissingTotp),
            4 => source.Where(i => i.Kind == HealthIssueKind.StalePassword),
            _ => source
        };

        foreach (var i in source.OrderBy(x => x.EntryTitle, StringComparer.OrdinalIgnoreCase))
            Issues.Add(new HealthIssueRow(i));
    }

    [RelayCommand]
    private void OpenEntry(HealthIssueRow? row)
    {
        if (row is null) return;
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
