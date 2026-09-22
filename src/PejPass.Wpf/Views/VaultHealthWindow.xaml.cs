using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class VaultHealthWindow : Window
{
    public Guid? SelectedEntryId { get; private set; }

    public VaultHealthWindow(VaultHealthViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestOpenEntry += (_, id) =>
        {
            SelectedEntryId = id;
            DialogResult = true;
            Close();
        };

        // Open UI immediately, then scan on background
        Loaded += async (_, _) => await viewModel.StartScanAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
