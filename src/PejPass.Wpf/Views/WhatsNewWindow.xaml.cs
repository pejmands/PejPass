using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();

        DataContext = new WhatsNewViewModel();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
