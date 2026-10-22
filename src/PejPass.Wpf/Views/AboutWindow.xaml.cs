using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        AboutViewModel vm;
        try
        {
            vm = App.Services.GetService<AboutViewModel>() ?? new AboutViewModel();
        }
        catch
        {
            vm = new AboutViewModel();
        }

        DataContext = vm;
        vm.ApplyKnownUpdateState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            vm.CancelPending();

        Close();
    }
}
