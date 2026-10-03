using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RecentVaultSelected += (_, _) =>
        {
            ClearPasswordInputs();

            Dispatcher.BeginInvoke(
                () => MasterPasswordBox.FocusInput(),
                System.Windows.Threading.DispatcherPriority.Input);
        };

        viewModel.ValidationFailed += (_, _) =>
        {
            if (!string.IsNullOrEmpty(viewModel.VaultPathError))
                VaultPathTextBox.Focus();
            else if (!string.IsNullOrEmpty(viewModel.PasswordError))
                MasterPasswordBox.FocusInput();
        };

        viewModel.RequestClose += (_, _) =>
        {
            MasterPasswordBox.Clear();
            ConfirmMasterPasswordBox.Clear();

            var main = App.Services.GetRequiredService<MainWindow>();

            var vaultFilePath = viewModel.VaultPath;
            var vaultFileName = Path.GetFileNameWithoutExtension(vaultFilePath);
            main.Title = string.IsNullOrWhiteSpace(vaultFileName)
                ? "PejPass"
                : $"PejPass — {vaultFileName}";

            App.PrepareCustomChrome(main);
            App.SetCustomWindowTitle(main, main.Title, vaultFilePath);

            System.Windows.Application.Current.MainWindow = main;

            main.Show();
            Close();
        };

        Loaded += async (_, _) =>
        {
            await Dispatcher.InvokeAsync(
                () => MasterPasswordBox.FocusInput(),
                System.Windows.Threading.DispatcherPriority.Input);

            await viewModel.RefreshWindowsHelloVisibilityAsync();
        };
    }

    private void RecentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button ||
            button.ContextMenu is not { } menu ||
            DataContext is not LoginViewModel viewModel)
        {
            return;
        }

        menu.Items.Clear();

        foreach (var recent in viewModel.RecentVaults)
        {
            var header = new StackPanel
            {
                MaxWidth = 340
            };

            var name = new TextBlock
            {
                Text = recent.DisplayName,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var path = new TextBlock
            {
                Text = recent.Path,
                FontSize = 11,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
            };
            path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            header.Children.Add(name);
            header.Children.Add(path);

            menu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = header,
                ToolTip = recent.Path,
                Style = (Style)FindResource("PejPassContextMenuItem"),
                Command = viewModel.SelectRecentVaultCommand,
                CommandParameter = recent
            });
        }

        menu.Items.Add(new Separator());

        var removeMenu = new System.Windows.Controls.MenuItem
        {
            Header = "Remove a recent vault",
            Style = (Style)FindResource("PejPassContextMenuItem")
        };

        foreach (var recent in viewModel.RecentVaults)
        {
            removeMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = recent.DisplayName,
                ToolTip = recent.Path,
                Style = (Style)FindResource("PejPassContextMenuItem"),
                Command = viewModel.RemoveRecentVaultCommand,
                CommandParameter = recent
            });
        }

        menu.Items.Add(removeMenu);
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Clear all recents",
            Style = (Style)FindResource("PejPassContextMenuItem"),
            Command = viewModel.ClearRecentVaultsCommand
        });

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void ClearPasswordInputs()
    {
        MasterPasswordBox.Clear();
        ConfirmMasterPasswordBox.Clear();

        MasterPasswordBox.IsRevealed = false;
        ConfirmMasterPasswordBox.IsRevealed = false;
    }

    private void VaultMode_Checked(object sender, RoutedEventArgs e)
    {
        ClearPasswordInputs();

        Dispatcher.BeginInvoke(
            () => MasterPasswordBox.FocusInput(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void MasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.MasterPassword = MasterPasswordBox.Password;
    }

    private void ConfirmMasterPasswordBox_PasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.ConfirmMasterPassword = ConfirmMasterPasswordBox.Password;
    }
}
