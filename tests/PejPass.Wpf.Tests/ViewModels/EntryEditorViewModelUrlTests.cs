using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class EntryEditorViewModelUrlTests
{
    [Fact]
    public void ExistingNonHttpUrl_CanBePreservedWhileEditingEntry()
    {
        var existing = new VaultEntry
        {
            Title = "Chrome entry",
            Url = "android://hash@com.example.app/"
        };

        var viewModel = new EntryEditorViewModel(existing);

        Assert.Null(viewModel.UrlErrorMessage);
        Assert.True(viewModel.IsValid());

        viewModel.Title = "Updated title";

        Assert.True(viewModel.IsValid());
        Assert.Equal(existing.Url, viewModel.ToEntry().Url);
    }

    [Fact]
    public void ChangedNonHttpUrl_RemainsInvalid()
    {
        var existing = new VaultEntry
        {
            Title = "Chrome entry",
            Url = "android://hash@com.example.app/"
        };

        var viewModel = new EntryEditorViewModel(existing)
        {
            Url = "javascript:alert(1)"
        };

        Assert.NotNull(viewModel.UrlErrorMessage);
        Assert.False(viewModel.IsValid());
    }
}
