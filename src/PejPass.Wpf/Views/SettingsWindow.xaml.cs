using System.ComponentModel;
using System.Windows;
using PejPass.Wpf.ViewModels;

namespace PejPass.Wpf.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private bool _committed;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;

        viewModel.RequestClose += (_, _) =>
        {
            _committed = true;
            DialogResult = true;
            Close();
        };

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_committed) return;

        // User closed with X / Alt+F4 — same as Cancel: revert preview
        _vm.CancelCommand.Execute(null);
        _committed = true;
    }
}
