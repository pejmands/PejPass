using System.Windows;
using System.Windows.Media;

namespace PejPass.Wpf.Dialogs;

public partial class AppDialog : Window
{
    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    public AppDialog(string title, string message, AppDialogType type,
        string primaryText = "OK", string? secondaryText = null)
    {
        InitializeComponent();

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

        // Tint primary button by type
        PrimaryButton.Background = type switch
        {
            AppDialogType.Error or AppDialogType.Confirm when primaryText is "Delete" or "Yes"
                => (Brush)FindResource("DangerBrush"),
            AppDialogType.Warning => (Brush)FindResource("WarningBrush"),
            AppDialogType.Success => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("AccentBrush")
        };

        if (type is AppDialogType.Error or AppDialogType.Warning)
            PrimaryButton.Foreground = (Brush)FindResource("ButtonTextBrush");
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
}
