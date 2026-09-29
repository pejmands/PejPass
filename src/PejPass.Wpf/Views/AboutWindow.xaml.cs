using System.Reflection;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = version is null
            ? "Version unknown"
            : $"Version {version.ToString(3)}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
