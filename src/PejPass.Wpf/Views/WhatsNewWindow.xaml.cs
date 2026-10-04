using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        WhatsNewViewModel vm;
        try
        {
            var updateService = App.Services.GetService<UpdateService>() ?? new UpdateService();
            vm = new WhatsNewViewModel(updateService);
        }
        catch
        {
            vm = new WhatsNewViewModel();
        }

        DataContext = vm;

        Loaded += async (_, _) => await vm.LoadAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
