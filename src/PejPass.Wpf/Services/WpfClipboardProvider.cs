using System.Windows;

namespace PejPass.Wpf.Services;

public sealed class WpfClipboardProvider : IClipboardProvider
{
    public void SetText(string text)
    {
        Clipboard.SetText(text);
    }

    public bool ContainsText()
    {
        return Clipboard.ContainsText();
    }

    public string GetText()
    {
        return Clipboard.GetText();
    }

    public void Clear()
    {
        Clipboard.Clear();
    }
}
