using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class HistoryViewModelLoadTests
{
    [Fact(Timeout = 15000)]
    public void Reload_WithManySnapshots_RebuildsFilteredItemsOncePerBatchAndMapsEntryTitles()
    {
        WpfTestHost.Run(_ =>
        {
            const int entryCount = 400;
            const int repeatedSnapshots = 400;
            const int snapshotCount = entryCount + repeatedSnapshots;

            using var session = new VaultSession();
            using var material = CreateMaterial();

            var entries = Enumerable.Range(0, entryCount)
                .Select(index => new VaultEntry
                {
                    Title = $"Current entry {index:D4}"
                })
                .ToList();

            var history = entries.Select((entry, index) => new EntryHistoryItem
            {
                EntryId = entry.Id,
                Title = $"Previous entry {index:D4}",
                ChangedAt = DateTimeOffset.UtcNow.AddSeconds(-index)
            }).ToList();

            history.AddRange(Enumerable.Range(0, repeatedSnapshots)
                .Select(index => new EntryHistoryItem
                {
                    EntryId = entries[0].Id,
                    Title = $"Older entry version {index:D4}",
                    ChangedAt = DateTimeOffset.UtcNow.AddDays(-1).AddSeconds(-index)
                }));

            var vault = new Vault
            {
                Entries = entries,
                History = history
            };

            session.Open(
                vault,
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
                material);

            using var viewModel = new HistoryViewModel(
                session,
                null!,
                new AppSettings(),
                (title, message) => { });

            var filteredCollectionChanges = 0;
            viewModel.FilteredItems.CollectionChanged += (_, _) =>
                filteredCollectionChanges++;

            var loadMethod = typeof(HistoryViewModel).GetMethod(
                "Load",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(loadMethod);
            loadMethod!.Invoke(viewModel, null);

            Assert.Equal(snapshotCount, viewModel.Items.Count);
            Assert.Equal(snapshotCount, viewModel.FilteredItems.Count);
            Assert.True(
                filteredCollectionChanges <= snapshotCount + 5,
                $"Expected one batch refresh, but observed {filteredCollectionChanges} filtered collection changes.");

            var expectedEntry = entries[217];
            var row = Assert.Single(
                viewModel.Items,
                item => item.EntryId == expectedEntry.Id);

            Assert.Equal(expectedEntry.Title, row.Title);
        }, TestContext.Current.CancellationToken);
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = new byte[32];
        var salt = new byte[16];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(salt);

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
