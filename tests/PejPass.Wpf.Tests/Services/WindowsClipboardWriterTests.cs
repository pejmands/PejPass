using PejPass.Wpf.Services;
using System.Text;
using System.Windows;

namespace PejPass.Wpf.Tests.Services;

public sealed class WindowsClipboardWriterTests
{
    [Fact]
    public void CreatePayloads_IncludesUnicodeTextAndZeroDwordsForHistoryAndCloudProtection()
    {
        const string text = "sensitive-test-value";

        var payloads = WindowsClipboardWriter.CreatePayloads(text);

        Assert.Equal(
            [
                DataFormats.UnicodeText,
                "ExcludeClipboardContentFromMonitorProcessing",
                "CanIncludeInClipboardHistory",
                "CanUploadToCloudClipboard"
            ],
            payloads.Select(payload => payload.FormatName));

        Assert.Equal(Encoding.Unicode.GetBytes(text + '\0'), payloads[0].Bytes);

        foreach (var payload in payloads.Skip(1))
        {
            Assert.Equal(sizeof(uint), payload.Bytes.Length);
            Assert.All(payload.Bytes, value => Assert.Equal((byte)0, value));
        }
    }
}
