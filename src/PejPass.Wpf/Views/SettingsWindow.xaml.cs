using PejPass.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;

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
            try { DialogResult = true; } catch { /* already closing */ }
            Close();
        };

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_committed) return;
        _vm.RevertPreview();
        _committed = true;
    }
}
