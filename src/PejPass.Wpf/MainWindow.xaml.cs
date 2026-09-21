using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;

namespace PejPass.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestLock += (_, _) =>
        {
            var login = App.Services.GetRequiredService<LoginWindow>();
            login.Show();
            Close();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                // Clear selection instead of letting ListBox jump to first item
                if (DataContext is MainViewModel vm)
                    vm.SelectedEntry = null;
                e.Handled = true;
            }
        };
    }
}
