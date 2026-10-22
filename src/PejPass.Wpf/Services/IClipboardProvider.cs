namespace PejPass.Wpf.Services;

public interface IClipboardProvider
{
    void SetText(string text);
    bool ContainsText();
    string GetText();
    void Clear();
}
