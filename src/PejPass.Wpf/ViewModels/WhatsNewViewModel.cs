using CommunityToolkit.Mvvm.ComponentModel;
using PejPass.Wpf.Data;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class WhatsNewViewModel : ObservableObject
{
    private readonly UpdateService _updateService;

    [ObservableProperty]
    private IReadOnlyList<ReleaseNote> releases = ReleaseNotes.All;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string subtitle = "Latest changes and improvements";

    public WhatsNewViewModel(UpdateService updateService)
    {
        _updateService = updateService;
    }

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

            Releases = notes;

            Subtitle = _updateService.LastManifest is not null
                ? "From update manifest"
                : "Offline changelog";
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
