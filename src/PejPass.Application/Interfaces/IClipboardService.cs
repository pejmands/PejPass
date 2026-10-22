namespace PejPass.Application.Interfaces;

public interface IClipboardService
{
    /// <summary>
    /// Copies text and schedules automatic clearing after the given timeout.
    /// </summary>
    void CopyWithTimeout(string text, TimeSpan timeout);

    /// <summary>
    /// Clears the clipboard only when it still contains text copied by PejPass.
    /// </summary>
    void ClearIfOwned();
}
