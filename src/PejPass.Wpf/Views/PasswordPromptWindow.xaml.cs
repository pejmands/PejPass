using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Views;

public partial class PasswordPromptWindow : Window
{
    public Func<string, Task<string?>>? ValidatePasswordAsync { get; set; }

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

    protected override void OnClosed(EventArgs e)
    {
        PasswordInput.Clear();
        ValidatePasswordAsync = null;
        base.OnClosed(e);
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Password is required.");
            return;
        }

        OkButton.IsEnabled = false;

        try
        {
            if (ValidatePasswordAsync is not null)
            {
                var error = await ValidatePasswordAsync(password);

                if (!string.IsNullOrEmpty(error))
                {
                    ShowError(error);
                    return;
                }
            }

            PasswordErrorText.Visibility = Visibility.Collapsed;
            DialogResult = true;
            Close();
        }
        catch (Exception)
        {
            ShowError("Could not validate the password. Please try again.");
        }
        finally
        {
            password = string.Empty;
            OkButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        PasswordErrorText.Text = $"⚠ {message}";
        PasswordErrorText.Visibility = Visibility.Visible;
        PasswordInput.Focus();
        Keyboard.Focus(PasswordInput);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
