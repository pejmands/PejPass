using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class MainViewModelSearchTests
{
    [Fact(Timeout = 15000)]
    public void SearchTextChanges_AreDebouncedAndOnlyLatestQueryIsApplied()
    {
        WpfTestHost.Run(host =>
        {
            var vm = host.CreateMainViewModel();

            try
            {
                vm.Entries.Add(new VaultEntry { Title = "First Entry" });
                vm.Entries.Add(new VaultEntry { Title = "Second Entry" });

                vm.SearchText = "First";
                vm.SearchText = "Second";

                Assert.Empty(vm.FilteredEntries);

                PumpFor(TimeSpan.FromMilliseconds(350));

                var entry = Assert.Single(vm.FilteredEntries);
                Assert.Equal("Second Entry", entry.Title);
            }
            finally
            {
                vm.StopBackgroundTimers();
            }
        }, TestContext.Current.CancellationToken);
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
    }

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
