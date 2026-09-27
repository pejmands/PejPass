using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Views;

public partial class PasswordPromptWindow : Window
{
    public string Password { get; private set; } = string.Empty;

    public PasswordPromptWindow(string title, string message)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (_, _) =>
        {
            PasswordInput.Focus();
            Keyboard.Focus(PasswordInput);
        };

        PasswordInput.PasswordChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(PasswordInput.Password))
                PasswordErrorText.Visibility = Visibility.Collapsed;
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordInput.Password))
        {
            PasswordErrorText.Text = "⚠ Password is required.";
            PasswordErrorText.Visibility = Visibility.Visible;
            PasswordInput.Focus();
            Keyboard.Focus(PasswordInput);
            return;
        }

        PasswordErrorText.Visibility = Visibility.Collapsed;
        Password = PasswordInput.Password;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
