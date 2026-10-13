using PejPass.Domain.Entities;
using PejPass.Domain.Health;
using PejPass.Domain.Security;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class VaultHealthViewModelRuleTests
{
    [Theory]
    [InlineData(PasswordStrengthLevel.Empty, HealthIssueKind.WeakPassword)]
    [InlineData(PasswordStrengthLevel.VeryWeak, HealthIssueKind.WeakPassword)]
    [InlineData(PasswordStrengthLevel.Weak, HealthIssueKind.WeakPassword)]
    [InlineData(PasswordStrengthLevel.Fair, HealthIssueKind.PasswordNeedsImprovement)]
    [InlineData(PasswordStrengthLevel.Strong, null)]
    [InlineData(PasswordStrengthLevel.VeryStrong, null)]
    public void GetPasswordIssueKind_SeparatesWeakFromFairPasswords(
        PasswordStrengthLevel level,
        HealthIssueKind? expected)
    {
        Assert.Equal(expected, VaultHealthViewModel.GetPasswordIssueKind(level));
    }

    [Fact]
    public void NeedsImprovement_HasLowerSeverityThanWeakPassword()
    {
        var weak = new HealthIssue(
            HealthIssueKind.WeakPassword,
            Guid.NewGuid(),
            "Weak",
            "Strength: Weak");
        var fair = new HealthIssue(
            HealthIssueKind.PasswordNeedsImprovement,
            Guid.NewGuid(),
            "Fair",
            "Strength: Fair — needs improvement");

        Assert.True(
            VaultHealthViewModel.GetIssueSeverity(fair) <
            VaultHealthViewModel.GetIssueSeverity(weak));
    }

    [Fact]
    public void GetPasswordLastChangedAt_IgnoresLaterNonPasswordEdits()
    {
        var now = DateTimeOffset.UtcNow;
        var entryId = Guid.NewGuid();
        var changedAt = now.AddDays(-400);
        var entry = new VaultEntry
        {
            Id = entryId,
            Password = "current-password",
            CreatedAt = now.AddDays(-800),
            UpdatedAt = now.AddDays(-1)
        };
        var history = new[]
        {
            new EntryHistoryItem
            {
                EntryId = entryId,
                Password = "previous-password",
                ChangedAt = changedAt
            },
            new EntryHistoryItem
            {
                EntryId = entryId,
                Password = "current-password",
                ChangedAt = now.AddDays(-1)
            }
        };

        Assert.Equal(
            changedAt,
            VaultHealthViewModel.GetPasswordLastChangedAt(entry, history));
    }

    [Fact]
    public void GetPasswordLastChangedAt_UsesCreatedAtWhenHistoryCannotIdentifyChange()
    {
        var now = DateTimeOffset.UtcNow;
        var createdAt = now.AddDays(-500);
        var entry = new VaultEntry
        {
            Id = Guid.NewGuid(),
            Password = "current-password",
            CreatedAt = createdAt,
            UpdatedAt = now.AddDays(-2)
        };
        var history = new[]
        {
            new EntryHistoryItem
            {
                EntryId = entry.Id,
                Password = "current-password",
                ChangedAt = now.AddDays(-2)
            }
        };

        Assert.Equal(
            createdAt,
            VaultHealthViewModel.GetPasswordLastChangedAt(entry, history));
    }
}
