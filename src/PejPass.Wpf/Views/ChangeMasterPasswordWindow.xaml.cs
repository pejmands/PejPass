using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PejPass.Wpf.Views;

public partial class ChangeMasterPasswordWindow : Window
{
    public ChangeMasterPasswordWindow(ChangeMasterPasswordViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (_, _) =>
        {
            DialogResult = viewModel.Success;
            Close();
        };

        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    private void CurrentPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm && sender is PasswordBox box)
            vm.CurrentPassword = box.Password;
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm && sender is PasswordBox box)
            vm.NewPassword = box.Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm && sender is PasswordBox box)
            vm.ConfirmPassword = box.Password;
    }
}
