using PejPass.Application.Interfaces;
using PejPass.Wpf.ViewModels;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class PasswordGeneratorWindow : Window
{
    private readonly bool _standalone;
    private readonly IClipboardService? _clipboard;
    private readonly TimeSpan _clipboardTimeout;

    public string? GeneratedPassword => (DataContext as PasswordGeneratorViewModel)?.Result;

    public PasswordGeneratorWindow(
        PasswordGeneratorViewModel viewModel,
        bool standalone = false,
        IClipboardService? clipboard = null,
        TimeSpan? clipboardTimeout = null)
    {
        InitializeComponent();

        _standalone = standalone;
        _clipboard = clipboard;
        _clipboardTimeout = clipboardTimeout ?? TimeSpan.FromSeconds(30);

        if (_standalone)
        {
            Title = "Password Generator";
            CancelButton.Content = "Close";
            UseButton.Content = "Copy";
        }

        App.PrepareCustomChrome(this);

        DataContext = viewModel;

        viewModel.RequestAccept += OnRequestAccept;
        viewModel.RequestCancel += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
    }

    private async void OnRequestAccept(object? sender, EventArgs e)
    {
        if (!_standalone)
        {
            DialogResult = true;
            Close();
            return;
        }

        var password = GeneratedPassword;
        if (string.IsNullOrEmpty(password) || _clipboard is null)
            return;

        try
        {
            _clipboard.CopyWithTimeout(password, _clipboardTimeout);
            UseButton.Content = "✓ Copied";
            await Task.Delay(500);
            Close();
        }
        catch
        {
            UseButton.Content = "Copy";
        }
    }
}