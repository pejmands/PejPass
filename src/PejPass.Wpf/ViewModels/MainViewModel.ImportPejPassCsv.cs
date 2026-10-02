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
                    $"Found {imported.Count} entries.\n\nMerge them into the current vault?\n\nIdentical entries are skipped. Entries matching the current Trash are restored to the list.",
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
            var result = MergeImportedEntries(vault, imported);

            Entries.Clear();
            foreach (var entry in vault.Entries)
                Entries.Add(entry);

            RebuildTagFilters();
            ApplyFilter();

            if (!await SaveVaultAsync())
            {
                RestoreVaultSnapshot(snapshot);
                return;
            }

            SnackbarService.Show(
                $"Import merged · +{result.AddedToList} added · {result.RestoredFromTrash} restored · {result.SkippedAlreadyInList} skipped.");
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
