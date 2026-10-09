using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PejPass.Wpf.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        DataContext = viewModel;
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        KeyDown -= OnKeyDown;

        if (DataContext is HistoryViewModel viewModel)
            viewModel.Dispose();

        DataContext = null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void HistoryListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
            item.IsSelected = true;
    }
}
