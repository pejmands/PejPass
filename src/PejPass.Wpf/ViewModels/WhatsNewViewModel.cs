using PejPass.Wpf.Data;
using PejPass.Wpf.Records;

namespace PejPass.Wpf.ViewModels;

public sealed class WhatsNewViewModel
{
    public IReadOnlyList<ReleaseNote> Releases { get; }

    public WhatsNewViewModel()
    {
        Releases = ReleaseNotes.All;
    }
}
