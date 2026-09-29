using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Input;

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

            var vaultFilePath = viewModel.VaultPath;
            var vaultFileName = Path.GetFileNameWithoutExtension(vaultFilePath);
            main.Title = string.IsNullOrWhiteSpace(vaultFileName)
                ? "PejPass"
                : $"PejPass — {vaultFileName}";

            App.PrepareCustomChrome(main);
            App.SetCustomWindowTitle(main, main.Title, vaultFilePath);

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

        MasterPasswordBox.IsRevealed = false;
        ConfirmMasterPasswordBox.IsRevealed = false;
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
            vm.ConfirmMasterPassword = ConfirmMasterPasswordBox.Password;
    }
}
