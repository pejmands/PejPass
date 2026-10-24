using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Dialogs;

public partial class AppDialog : Window
{
    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    public bool UsesModalResult { get; set; } = true;

    public AppDialog(string title, string message, AppDialogType type,
        string primaryText = "OK", string? secondaryText = null, string? tertiaryText = null,
        bool isDestructive = false)
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

        if (isDestructive)
        {
            PrimaryButton.Style = (Style)FindResource("DestructiveButton");
            PrimaryButton.IsDefault = false;

            if (TertiaryButton.Visibility == Visibility.Visible)
                TertiaryButton.IsDefault = true;
            else if (SecondaryButton.Visibility == Visibility.Visible)
                SecondaryButton.IsDefault = true;
        }
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
            if (UsesModalResult)
                DialogResult = false;
            Close();
        }

        e.Handled = true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Primary;
        if (UsesModalResult)
            DialogResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Secondary;
        if (UsesModalResult)
            DialogResult = false;
        Close();
    }

    private void Tertiary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Tertiary;
        if (UsesModalResult)
            DialogResult = false;
        Close();
    }
}
