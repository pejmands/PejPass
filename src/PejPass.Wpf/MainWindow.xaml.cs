using System.Windows;
using PejPass.Wpf.ViewModels;

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
            var login = App.Services.GetRequiredService<Views.LoginWindow>();
            login.Show();
            Close();
        };
    }
}
