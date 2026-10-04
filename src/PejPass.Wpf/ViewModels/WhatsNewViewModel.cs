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
            Subtitle = FormatSubtitle(cached.Count, offline: true);
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
            var notes = await _updateService
                .LoadReleaseNotesAsync()
                .ConfigureAwait(true);

            if (notes.Count > 0)
            {
                Releases = notes;
                Subtitle = FormatSubtitle(
                    notes.Count,
                    offline: _updateService.LastManifest is null);
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
                Subtitle = FormatSubtitle(cached.Count, offline: true);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatSubtitle(int count, bool offline)
    {
        var label = count == 1 ? "1 release" : $"{count} releases";
        return offline ? $"{label} (cached)" : label;
    }
}
