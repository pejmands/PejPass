using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.IO;

namespace PejPass.Wpf.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ValidationFailed += (_, _) =>
        {
            if (!string.IsNullOrEmpty(viewModel.VaultPathError))
                VaultPathTextBox.Focus();
            else if (!string.IsNullOrEmpty(viewModel.PasswordError))
                MasterPasswordBox.Focus();
        };

        viewModel.RequestClose += (_, _) =>
        {
            MasterPasswordBox.Clear();
            ConfirmMasterPasswordBox.Clear();

            var main = App.Services.GetRequiredService<MainWindow>();

            var vaultFileName = Path.GetFileName(viewModel.VaultPath);
            main.Title = string.IsNullOrWhiteSpace(vaultFileName)
                ? "PejPass"
                : $"PejPass — {vaultFileName}";

            App.PrepareCustomChrome(main);

            System.Windows.Application.Current.MainWindow = main;

            main.Show();
            Close();
        };

        Loaded += async (_, _) =>
        {
            await Dispatcher.InvokeAsync(
                () => Keyboard.Focus(MasterPasswordBox),
                System.Windows.Threading.DispatcherPriority.Input);

            await viewModel.RefreshWindowsHelloVisibilityAsync();
        };
    }

    private void ClearPasswordInputs()
    {
        MasterPasswordBox.Clear();
        ConfirmMasterPasswordBox.Clear();
    }

    private void VaultMode_Checked(object sender, RoutedEventArgs e)
    {
        ClearPasswordInputs();
    }

    private void MasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.MasterPassword = MasterPasswordBox.Password;
    }

    private void ConfirmMasterPasswordBox_PasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            vm.ConfirmMasterPassword =
                ConfirmMasterPasswordBox.Password;
        }
    }
}
