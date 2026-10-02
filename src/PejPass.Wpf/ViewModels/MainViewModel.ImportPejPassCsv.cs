using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PejPass.Application.Interfaces;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ImportPejPassCsvAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import PejPass CSV",
            Filter = "PejPass CSV (*.pejpass.csv)|*.pejpass.csv|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Importing PejPass CSV...";
            StatusMessage = "Importing PejPass CSV...";
            var service = App.Services.GetRequiredService<IBrowserImportService>();
            var imported = await service.ImportPejPassCsvAsync(dlg.FileName);

            if (imported.Count == 0)
            {
                SnackbarService.Show("Import finished — nothing imported.", SnackbarKind.Info);
                return;
            }

            if (!DialogService.Confirm(
                    $"Found {imported.Count} entries.\n\nImport them into the current vault?\n\nExisting entries will not be overwritten; imported entries will be added.",
                    "Confirm PejPass CSV Import",
                    yesText: "Import",
                    noText: "Cancel"))
            {
                SnackbarService.Show("Import cancelled.", SnackbarKind.Info);
                return;
            }

            if (!await EnsureVaultWritableAsync())
                return;

            var vault = _vaultSession.Vault!;
            var snapshot = vault.CreateSnapshot();
            foreach (var entry in imported)
            {
                vault.AddEntry(entry);
                Entries.Add(entry);
            }

            RebuildTagFilters();
            ApplyFilter();

            if (!await SaveVaultAsync())
            {
                RestoreVaultSnapshot(snapshot);
                return;
            }

            SnackbarService.Show($"Imported {imported.Count} entries from PejPass CSV.");
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            DialogService.Error($"PejPass CSV import failed:\n{ex.Message}", "Import PejPass CSV");
            StatusMessage = "PejPass CSV import failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
