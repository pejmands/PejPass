using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

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

    private void NewPasswordBoxTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm &&
            vm.NewPassword != NewPasswordBoxTextBox.Text)
            vm.NewPassword = NewPasswordBoxTextBox.Text;
    }

    private void NewPasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePassword(NewPasswordBox, NewPasswordBoxTextBox, NewPasswordVisibilityButton);
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm && sender is PasswordBox box)
            vm.ConfirmPassword = box.Password;
    }

    private void ConfirmPasswordBoxTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm &&
            vm.ConfirmPassword != ConfirmPasswordBoxTextBox.Text)
            vm.ConfirmPassword = ConfirmPasswordBoxTextBox.Text;
    }

    private void ConfirmPasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePassword(ConfirmPasswordBox, ConfirmPasswordBoxTextBox, ConfirmPasswordVisibilityButton);
    }

    private static void TogglePassword(
        PasswordBox passwordBox,
        TextBox textBox,
        Button visibilityButton)
    {
        if (passwordBox.Visibility == Visibility.Visible)
        {
            textBox.Text = passwordBox.Password;
            passwordBox.Visibility = Visibility.Collapsed;
            textBox.Visibility = Visibility.Visible;
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
            visibilityButton.ToolTip = "Hide password";
        }
        else
        {
            passwordBox.Password = textBox.Text;
            textBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;
            passwordBox.Focus();
            visibilityButton.ToolTip = "Show password";
        }
    }
}
