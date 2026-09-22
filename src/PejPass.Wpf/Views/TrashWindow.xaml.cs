using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class TrashWindow : Window
{
    public TrashWindow(TrashViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
