using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (_, _) =>
        {
            MasterPasswordBox.Clear();

            var main = App.Services.GetRequiredService<MainWindow>();
            main.Show();
            Close();
        };

        Loaded += async (_, _) =>
        {
            MasterPasswordBox.Focus();
            await viewModel.RefreshWindowsHelloVisibilityAsync();
        };
    }

    private void MasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.MasterPassword = MasterPasswordBox.Password;
    }
}
