using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
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
        if (DataContext is not EntryEditorViewModel vm)
            return;

        if (string.IsNullOrWhiteSpace(vm.Title))
        {
            DialogService.Warning("Title is required.", "Validation");
            return;
        }

        if (!vm.IsTotpSecretValid())
        {
            DialogService.Warning(
                "TOTP secret is invalid.\n\nEnter a valid Base32 authenticator key, or clear the field to disable TOTP.",
                "Invalid TOTP");
            return;
        }

        Result = vm.ToEntry();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
