using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Dialogs;

public partial class AppDialog : Window
{
    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    public AppDialog(string title, string message, AppDialogType type,
        string primaryText = "OK", string? secondaryText = null, string? tertiaryText = null)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);
        PreviewKeyDown += OnPreviewKeyDown;

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (string.IsNullOrEmpty(secondaryText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        if (string.IsNullOrEmpty(tertiaryText))
        {
            TertiaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            TertiaryButton.Content = tertiaryText;
            TertiaryButton.Visibility = Visibility.Visible;
            // Escape cancels when a tertiary (usually Cancel) is present
            TertiaryButton.IsCancel = true;
        }

        if (primaryText is "Delete" or "Yes" or "Delete permanently" or "Delete all permanently")
            PrimaryButton.Style = (Style)FindResource("DestructiveButton");
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (TertiaryButton.Visibility == Visibility.Visible)
        {
            Tertiary_Click(TertiaryButton, new RoutedEventArgs());
        }
        else
        {
            Result = AppDialogResult.None;
            DialogResult = false;
            Close();
        }

        e.Handled = true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Primary;
        DialogResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Secondary;
        DialogResult = false;
        Close();
    }

    private void Tertiary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Tertiary;
        DialogResult = false;
        Close();
    }
}
