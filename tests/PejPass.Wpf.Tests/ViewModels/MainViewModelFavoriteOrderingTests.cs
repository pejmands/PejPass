using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class MainViewModelFavoriteOrderingTests
{
    [Fact]
    public void GetNextManualSortOrder_AppendsToFavoriteGroup()
    {
        var moving = new VaultEntry
        {
            Id = Guid.NewGuid(),
            IsFavorite = false,
            SortOrder = 900
        };
        var entries = new[]
        {
            moving,
            new VaultEntry { IsFavorite = true, SortOrder = 10 },
            new VaultEntry { IsFavorite = true, SortOrder = 30 },
            new VaultEntry { IsFavorite = false, SortOrder = 20 }
        };

        var next = MainViewModel.GetNextManualSortOrder(
            entries,
            destinationIsFavorite: true,
            movingEntryId: moving.Id);

        Assert.Equal(40, next);
    }

    [Fact]
    public void GetNextManualSortOrder_AppendsToNonFavoriteGroup()
    {
        var moving = new VaultEntry
        {
            Id = Guid.NewGuid(),
            IsFavorite = true,
            SortOrder = 900
        };
        var entries = new[]
        {
            moving,
            new VaultEntry { IsFavorite = false, SortOrder = 0 },
            new VaultEntry { IsFavorite = false, SortOrder = 20 },
            new VaultEntry { IsFavorite = true, SortOrder = 10 }
        };

        var next = MainViewModel.GetNextManualSortOrder(
            entries,
            destinationIsFavorite: false,
            movingEntryId: moving.Id);

        Assert.Equal(30, next);
    }

    [Fact]
    public void GetNextManualSortOrder_StartsAtZeroForAnEmptyGroup()
    {
        var moving = new VaultEntry { IsFavorite = false };

        var next = MainViewModel.GetNextManualSortOrder(
            [moving],
            destinationIsFavorite: true,
            movingEntryId: moving.Id);

        Assert.Equal(0, next);
    }
}
