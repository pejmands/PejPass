using System.Windows;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Views;

public partial class PasswordGeneratorWindow : Window
{
    public string? GeneratedPassword => (DataContext as PasswordGeneratorViewModel)?.Result;

    public PasswordGeneratorWindow(PasswordGeneratorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestAccept += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        viewModel.RequestCancel += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
    }
}
