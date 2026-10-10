using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class EntryEditorHistoryTests
{
    [Fact]
    public void ToEntry_WhenEditing_PreservesCredentialHistoriesAsIndependentCopies()
    {
        var passwordChangedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var usernameChangedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var existing = new VaultEntry
        {
            Title = "Example",
            Username = "current-user",
            Password = "current-password",
            PasswordHistory =
            [
                new PasswordHistoryItem
                {
                    Password = "previous-password",
                    ChangedAt = passwordChangedAt
                }
            ],
            UsernameHistory =
            [
                new UsernameHistoryItem
                {
                    Username = "previous-user",
                    ChangedAt = usernameChangedAt
                }
            ]
        };

        var viewModel = new EntryEditorViewModel(existing)
        {
            Title = "Edited example",
            Username = "edited-user",
            Password = "edited-password"
        };

        var updated = viewModel.ToEntry();

        var passwordHistory = Assert.Single(updated.PasswordHistory);
        Assert.Equal("previous-password", passwordHistory.Password);
        Assert.Equal(passwordChangedAt, passwordHistory.ChangedAt);
        Assert.NotSame(existing.PasswordHistory[0], passwordHistory);

        var usernameHistory = Assert.Single(updated.UsernameHistory);
        Assert.Equal("previous-user", usernameHistory.Username);
        Assert.Equal(usernameChangedAt, usernameHistory.ChangedAt);
        Assert.NotSame(existing.UsernameHistory[0], usernameHistory);
    }
}
