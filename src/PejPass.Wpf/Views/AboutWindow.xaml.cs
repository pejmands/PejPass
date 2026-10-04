using System.Reflection;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text =
            $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/pejmands/PejPass",
            UseShellExecute = true
        });
    }
}
