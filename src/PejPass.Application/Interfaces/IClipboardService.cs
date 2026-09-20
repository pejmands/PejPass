namespace PejPass.Application.Interfaces;

public interface IClipboardService
{
    /// <summary>
    /// Copies text and schedules automatic clearing after the given timeout.
    /// </summary>
    void CopyWithTimeout(string text, TimeSpan timeout);

    void Clear();
}
