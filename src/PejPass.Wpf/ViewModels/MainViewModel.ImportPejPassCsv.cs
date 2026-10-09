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

        var initialVault = _vaultSession.Vault;
        var operationGeneration = _vaultSession.Generation;
        if (initialVault is null || !_vaultSession.IsCurrent(operationGeneration, initialVault))
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Importing PejPass CSV...";
            StatusMessage = "Importing PejPass CSV...";
            var service = App.Services.GetRequiredService<IBrowserImportService>();
            var imported = await service.ImportPejPassCsvAsync(dlg.FileName);

            if (!_vaultSession.IsCurrent(operationGeneration, initialVault))
                return;

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

            if (!_vaultSession.IsCurrent(operationGeneration, initialVault) ||
                !await EnsureVaultWritableAsync() ||
                !_vaultSession.IsCurrent(operationGeneration, initialVault))
                return;

            var vault = initialVault;
            var snapshot = vault.CreateSnapshot();
            var result = MergeImportedEntries(vault, imported);

            Entries.Clear();
            foreach (var entry in vault.Entries)
                Entries.Add(entry);

            RebuildTagFilters();
            ApplyFilter();

            if (!await SaveVaultAsync(vault, operationGeneration))
            {
                RestoreVaultSnapshot(snapshot, vault, operationGeneration);
                return;
            }

            DialogService.Success(
                $"Import finished.\n\n" +
                $"Added to list:              {result.AddedToList}\n" +
                $"Restored from trash → list: {result.RestoredFromTrash}\n" +
                $"Skipped (already in list):  {result.SkippedAlreadyInList}\n\n" +
                $"Vault now: {vault.Entries.Count} entries · {vault.Trash.Count} in trash\n\n" +
                $"Saved to:\n{_vaultSession.VaultPath}",
                "Import complete");
            ResetAutoLockTimer();
        }
        catch (Exception)
        {
            DialogService.Error("PejPass CSV import failed. Check the file and try again.", "Import PejPass CSV");
            StatusMessage = "PejPass CSV import failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
