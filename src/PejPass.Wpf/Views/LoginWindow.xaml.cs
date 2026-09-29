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
        MasterPasswordTextBox.Clear();
        ConfirmMasterPasswordBox.Clear();
        ConfirmMasterPasswordTextBox.Clear();

        MasterPasswordBox.Visibility = Visibility.Visible;
        MasterPasswordTextBox.Visibility = Visibility.Collapsed;

        ConfirmMasterPasswordBox.Visibility = Visibility.Visible;
        ConfirmMasterPasswordTextBox.Visibility = Visibility.Collapsed;
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

    private void MasterPasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is LoginViewModel vm &&
            vm.MasterPassword != MasterPasswordTextBox.Text)
        {
            vm.MasterPassword = MasterPasswordTextBox.Text;
        }
    }

    private void MasterPasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (MasterPasswordBox.Visibility == Visibility.Visible)
        {
            MasterPasswordTextBox.Text = MasterPasswordBox.Password;
            MasterPasswordBox.Visibility = Visibility.Collapsed;
            MasterPasswordTextBox.Visibility = Visibility.Visible;
            MasterPasswordTextBox.Focus();
            MasterPasswordTextBox.CaretIndex = MasterPasswordTextBox.Text.Length;
            MasterPasswordVisibilityButton.ToolTip = "Hide password";
        }
        else
        {
            MasterPasswordBox.Password = MasterPasswordTextBox.Text;
            MasterPasswordTextBox.Visibility = Visibility.Collapsed;
            MasterPasswordBox.Visibility = Visibility.Visible;
            MasterPasswordBox.Focus();
            MasterPasswordVisibilityButton.ToolTip = "Show password";
        }
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

    private void ConfirmMasterPasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is LoginViewModel vm &&
            vm.ConfirmMasterPassword != ConfirmMasterPasswordTextBox.Text)
        {
            vm.ConfirmMasterPassword = ConfirmMasterPasswordTextBox.Text;
        }
    }

    private void ConfirmMasterPasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmMasterPasswordBox.Visibility == Visibility.Visible)
        {
            ConfirmMasterPasswordTextBox.Text = ConfirmMasterPasswordBox.Password;
            ConfirmMasterPasswordBox.Visibility = Visibility.Collapsed;
            ConfirmMasterPasswordTextBox.Visibility = Visibility.Visible;
            ConfirmMasterPasswordTextBox.Focus();
            ConfirmMasterPasswordTextBox.CaretIndex = ConfirmMasterPasswordTextBox.Text.Length;
            ConfirmMasterPasswordVisibilityButton.ToolTip = "Hide password";
        }
        else
        {
            ConfirmMasterPasswordBox.Password = ConfirmMasterPasswordTextBox.Text;
            ConfirmMasterPasswordTextBox.Visibility = Visibility.Collapsed;
            ConfirmMasterPasswordBox.Visibility = Visibility.Visible;
            ConfirmMasterPasswordBox.Focus();
            ConfirmMasterPasswordVisibilityButton.ToolTip = "Show password";
        }
    }
}
