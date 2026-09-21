using System.Windows;
using PejPass.Wpf.ViewModels;

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
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
