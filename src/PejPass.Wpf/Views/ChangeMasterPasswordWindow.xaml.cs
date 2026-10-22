using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class ChangeMasterPasswordWindow : Window
{
    public ChangeMasterPasswordWindow(ChangeMasterPasswordViewModel viewModel)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);
        DataContext = viewModel;

        viewModel.RequestClose += (_, _) =>
        {
            DialogResult = viewModel.Success;
            Close();
        };

        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();

        if (DataContext is ChangeMasterPasswordViewModel viewModel)
            viewModel.ClearSensitiveInputs();

        DataContext = null;
        base.OnClosed(e);
    }

    private void CurrentPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm &&
            sender is System.Windows.Controls.PasswordBox box)
        {
            vm.CurrentPassword = box.Password;
        }
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm)
            vm.NewPassword = NewPasswordBox.Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm)
            vm.ConfirmPassword = ConfirmPasswordBox.Password;
    }
}
