using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Views;

public partial class PasswordPromptWindow : Window
{
    public string Password { get; private set; } = string.Empty;

    public PasswordPromptWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (_, _) =>
        {
            PasswordInput.Focus();
            Keyboard.Focus(PasswordInput);
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
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
