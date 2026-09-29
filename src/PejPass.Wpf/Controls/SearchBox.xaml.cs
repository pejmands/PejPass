using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PejPass.Wpf.Controls;

public partial class SearchBox : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SearchBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SearchBox),
            new PropertyMetadata("Search..."));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public SearchBox()
    {
        InitializeComponent();
    }

    public void FocusInput()
    {
        InputBox.Focus();
    }

    public void SelectAll()
    {
        InputBox.SelectAll();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Text = string.Empty;
        InputBox.Focus();
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (!string.IsNullOrEmpty(Text))
        {
            Text = string.Empty;
            e.Handled = true;
        }
    }
}
