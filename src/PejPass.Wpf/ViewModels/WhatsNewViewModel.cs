using CommunityToolkit.Mvvm.ComponentModel;
using PejPass.Wpf.Data;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class WhatsNewViewModel(UpdateService updateService) : ObservableObject
{
    private readonly UpdateService _updateService = updateService;

    [ObservableProperty]
    public partial IReadOnlyList<ReleaseNote> Releases { get; set; } = ReleaseNotes.All;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "Changelog";

    public WhatsNewViewModel() : this(new UpdateService())
    {
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Subtitle = "Loading…";

        try
        {
            var notes = await _updateService
                .LoadReleaseNotesAsync()
                .ConfigureAwait(true);

            Releases = notes.Count > 0 ? notes : ReleaseNotes.All;

            if (_updateService.LastManifest is not null)
            {
                var n = Releases.Count;
                Subtitle = n == 1
                    ? "Latest release notes"
                    : $"{n} releases";
            }
            else
            {
                Subtitle = "Offline changelog";
            }
        }
        catch
        {
            Releases = ReleaseNotes.All;
            Subtitle = "Offline changelog";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
