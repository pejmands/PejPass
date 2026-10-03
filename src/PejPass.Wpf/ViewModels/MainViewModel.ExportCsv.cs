using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PejPass.Application.Interfaces;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

/// <summary>
/// CSV export only — partial so MainViewModel.cs stays untouched.
/// Resolves <see cref="ICsvExportService"/> from DI at call time (no ctor change).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (Entries.Count == 0)
        {
            DialogService.Info("There are no entries to export.", "Export CSV");
            return;
        }

        if (!DialogService.Confirm(
                "WARNING: CSV export is NOT encrypted.\n\n" +
                "Anyone with this file can read all usernames and passwords in plain text.\n\n" +
                "• Do not email or upload this file\n" +
                "• Delete it after use\n" +
                "• Prefer encrypted Backup (.pejpass) for safe storage\n\n" +
                "Continue with unencrypted CSV export?",
                "Security warning — unencrypted export",
                yesText: "Export CSV anyway",
                noText: "Cancel"))
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Export passwords as CSV (UNENCRYPTED)",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"PejPass-export-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var csvExport = App.Services.GetRequiredService<ICsvExportService>();
            await csvExport.ExportToCsvAsync(dlg.FileName, Entries);

            DialogService.Warning(
                $"Saved {Entries.Count} entries to:\n{dlg.FileName}\n\n" +
                "This file is unencrypted. Delete it when you no longer need it.",
                "CSV exported");
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error($"CSV export failed:\n{ex.Message}", "Export CSV");
            StatusMessage = "CSV export failed.";
        }
    }

    [RelayCommand]
    private async Task ExportPejPassCsvAsync()
    {
        if (Entries.Count == 0)
        {
            DialogService.Info("There are no entries to export.", "Export PejPass CSV");
            return;
        }

        if (!DialogService.Confirm(
                "WARNING: PejPass CSV is NOT encrypted.\n\n" +
                "It contains passwords, TOTP secrets, tags and custom field values in plain text.\n\n" +
                "Store it securely and delete it after use.\n\nContinue?",
                "Security warning — unencrypted export",
                yesText: "Export PejPass CSV",
                noText: "Cancel"))
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Export full PejPass CSV (UNENCRYPTED)",
            Filter = "PejPass CSV (*.pejpass.csv)|*.pejpass.csv|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".pejpass.csv",
            FileName = $"PejPass-full-export-{DateTime.Now:yyyyMMdd-HHmm}.pejpass.csv",
            AddExtension = true
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var csvExport = App.Services.GetRequiredService<ICsvExportService>();
            await csvExport.ExportPejPassCsvAsync(dlg.FileName, Entries);
            DialogService.Warning(
                $"Saved {Entries.Count} entries to:\n{dlg.FileName}\n\n" +
                "This file contains sensitive data in plain text. Delete it when you no longer need it.",
                "PejPass CSV exported");
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error($"PejPass CSV export failed:\n{ex.Message}", "Export PejPass CSV");
            StatusMessage = "PejPass CSV export failed.";
        }
    }
}
