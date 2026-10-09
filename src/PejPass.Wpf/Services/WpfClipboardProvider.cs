using System.Windows;

namespace PejPass.Wpf.Services;

public sealed class WpfClipboardProvider : IClipboardProvider
{
    public void SetText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", true);
        Clipboard.SetDataObject(data, copy: true);
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
