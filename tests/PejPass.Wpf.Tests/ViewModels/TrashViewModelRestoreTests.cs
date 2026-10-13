using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class TrashViewModelRestoreTests
{
    [Fact]
    public async Task RestoreSelected_WhenTwoTrashedEntriesAreIdentical_RestoresOnlyOne()
    {
        var vault = new Vault();
        var a = new VaultEntry { Title = "Same", Password = "x" };
        var b = new VaultEntry { Title = "Same", Password = "x" };
        vault.AddEntry(a);
        vault.AddEntry(b);
        vault.SoftDelete(a.Id);
        vault.SoftDelete(b.Id);

        var viewModel = new TrashViewModel(
            vault,
            () => Task.FromResult(true),
            () => Task.FromResult(true),
            () => true,
            candidate => vault.Entries.Any(e =>
                MainViewModel.EntryContentFingerprint(e) ==
                MainViewModel.EntryContentFingerprint(candidate)));

        await viewModel.RestoreSelectedCommand.ExecuteAsync(viewModel.Items.ToArray());

        Assert.Single(vault.Entries);
        Assert.Single(vault.Trash);
    }
}
