using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class MainViewModelSearchTests
{
    [Fact]
    public void MatchesSearch_DoesNotMatchSecretCustomFieldValue()
    {
        var entry = new VaultEntry
        {
            Title = "Router",
            CustomFields =
            [
                new CustomField
                {
                    Name = "Recovery code",
                    Value = "secret-recovery-73921",
                    IsSecret = true
                }
            ]
        };

        Assert.False(MainViewModel.MatchesSearch(entry, "secret-recovery-73921"));
    }

    [Fact]
    public void MatchesSearch_CanMatchSecretCustomFieldName()
    {
        var entry = new VaultEntry
        {
            CustomFields =
            [
                new CustomField
                {
                    Name = "Recovery code",
                    Value = "secret-recovery-73921",
                    IsSecret = true
                }
            ]
        };

        Assert.True(MainViewModel.MatchesSearch(entry, "Recovery code"));
    }

    [Fact]
    public void MatchesSearch_StillMatchesNonSecretCustomFieldValues()
    {
        var entry = new VaultEntry
        {
            CustomFields =
            [
                new CustomField
                {
                    Name = "Department",
                    Value = "engineering",
                    IsSecret = false
                }
            ]
        };

        Assert.True(MainViewModel.MatchesSearch(entry, "engineering"));
    }
}
