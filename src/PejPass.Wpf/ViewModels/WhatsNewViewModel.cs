using CommunityToolkit.Mvvm.ComponentModel;
using PejPass.Wpf.Records;
using PejPass.Wpf.Services;

namespace PejPass.Wpf.ViewModels;

public partial class WhatsNewViewModel(UpdateService updateService) : ObservableObject
{
    private readonly UpdateService _updateService = updateService;

    [ObservableProperty]
    public partial IReadOnlyList<ReleaseNote> Releases { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "Changelog";

    public WhatsNewViewModel() : this(new UpdateService())
    {
    }

    public async Task LoadAsync()
    {
        // Phase 1 — show disk/memory cache immediately (no network wait).
        var cached = _updateService.GetCachedReleaseNotes();
        if (cached.Count > 0)
        {
            Releases = cached;
            Subtitle = FormatSubtitle(cached.Count, isFromNetwork: false);
            IsLoading = false;
        }
        else
        {
            IsLoading = true;
            Subtitle = "Loading…";
        }

        // Phase 2 — refresh from update.json; update UI + cache when data arrives.
        try
        {
            var result = await _updateService
                .LoadReleaseNotesAsync()
                .ConfigureAwait(true);

            var notes = result.Notes;
            if (notes.Count > 0)
            {
                Releases = notes;
                Subtitle = FormatSubtitle(
                    notes.Count,
                    result.IsFromNetwork);
            }
            else if (cached.Count == 0)
            {
                Releases = [];
                Subtitle = "No changelog yet. Connect once to download release notes.";
            }
        }
        catch
        {
            if (cached.Count == 0)
            {
                Releases = [];
                Subtitle = "Offline — no cached changelog yet.";
            }
            else
            {
                Subtitle = FormatSubtitle(cached.Count, isFromNetwork: false);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static string FormatSubtitle(int count, bool isFromNetwork)
    {
        var label = count == 1 ? "1 release" : $"{count} releases";
        return isFromNetwork ? label : $"{label} (cached)";
    }
}
