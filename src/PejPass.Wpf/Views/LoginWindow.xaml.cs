using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PejPass.Wpf.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (_, _) =>
        {
            // Open main window and close login
            var main = App.Services.GetRequiredService<MainWindow>();
            main.Show();
            Close();
        };

        Loaded += (_, _) =>
        {
            MasterPasswordBox.Focus();
        };
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
        {
            vm.MasterPassword = pb.Password;
        }
    }
}
