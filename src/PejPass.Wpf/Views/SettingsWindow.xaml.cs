using PejPass.Wpf.Controls;
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
        App.PrepareCustomChrome(this);

        DataContext = viewModel;
        _vm = viewModel;

        viewModel.ValidationFailed += (_, _) => FocusFirstInvalidField();
        ZoomBehavior.GlobalZoomChanged += OnGlobalZoomChanged;
        Closed += OnClosed;

        viewModel.RequestClose += (_, _) =>
        {
            _committed = true;
            try { DialogResult = true; } catch { /* already closing */ }
            Close();
        };

        Closing += OnClosing;
    }

    private void OnGlobalZoomChanged(object? sender, double zoom) =>
        _vm.UpdateZoomFromGlobal(zoom);

    private void OnClosed(object? sender, EventArgs e)
    {
        ZoomBehavior.GlobalZoomChanged -= OnGlobalZoomChanged;
    }

    private void FocusFirstInvalidField()
    {
        if (!string.IsNullOrEmpty(_vm.AutoLockError))
        {
            AutoLockTextBox.Focus();
            return;
        }

        if (!string.IsNullOrEmpty(_vm.ClipboardError))
            ClipboardTextBox.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_committed) return;
        _vm.RevertPreview();
        _committed = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
