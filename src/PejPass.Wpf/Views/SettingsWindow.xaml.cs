using System.Windows;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }
}
