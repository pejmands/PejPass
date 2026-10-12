using PejPass.Domain.Exceptions;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class EntryEditorViewModelLimitTests
{
    [Fact]
    public void IsValid_WhenTagCountExceedsLimit_ReportsTagCount()
    {
        var viewModel = new EntryEditorViewModel(null)
        {
            Title = "Example",
            TagsText = string.Join(
                ", ",
                Enumerable.Range(0, VaultLimits.MaxCollectionItemsPerEntry + 1).Select(i => $"tag-{i}"))
        };

        Assert.False(viewModel.IsValid());
        Assert.Contains(
            VaultLimits.MaxCollectionItemsPerEntry.ToString("N0"),
            viewModel.TagsErrorMessage ?? string.Empty);
    }

    [Fact]
    public void IsValid_WhenTagExceedsLengthLimit_ReportsTagLength()
    {
        var viewModel = new EntryEditorViewModel(null)
        {
            Title = "Example",
            TagsText = new string('x', VaultLimits.MaxTagCharacters + 1)
        };

        Assert.False(viewModel.IsValid());
        Assert.Contains(
            VaultLimits.MaxTagCharacters.ToString("N0"),
            viewModel.TagsErrorMessage ?? string.Empty);
    }

    [Fact]
    public void AddCustomFieldCommand_CannotExceedLimit()
    {
        var viewModel = new EntryEditorViewModel(null);

        for (var i = 0; i < VaultLimits.MaxCollectionItemsPerEntry; i++)
        {
            Assert.True(viewModel.AddCustomFieldCommand.CanExecute(null));
            viewModel.AddCustomFieldCommand.Execute(null);
        }

        Assert.Equal(VaultLimits.MaxCollectionItemsPerEntry, viewModel.CustomFields.Count);
        Assert.False(viewModel.AddCustomFieldCommand.CanExecute(null));
    }
}
