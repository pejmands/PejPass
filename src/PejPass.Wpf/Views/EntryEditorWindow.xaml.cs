using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class EntryEditorWindow : Window
{
    public VaultEntry? Result { get; private set; }

    public EntryEditorWindow(EntryEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = viewModel.Original is null ? "Add Entry" : "Edit Entry";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryEditorViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.Title))
            {
                MessageBox.Show("Title is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = vm.ToEntry();
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
