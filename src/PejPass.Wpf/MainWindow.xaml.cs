using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows;

namespace PejPass.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestLock += (_, _) =>
        {
            // Return to login
            var login = App.Services.GetRequiredService<LoginWindow>();
            login.Show();
            Close();
        };
    }
}
