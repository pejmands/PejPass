using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

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
            var header = new Grid
            {
                MinWidth = 280,
                MaxWidth = 360
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var details = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 12, 1)
            };

            var name = new TextBlock
            {
                Text = recent.DisplayName,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var path = new TextBlock
            {
                Text = recent.Path,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            details.Children.Add(name);
            details.Children.Add(path);
            Grid.SetColumn(details, 0);
            header.Children.Add(details);

            var removeButton = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Focusable = false,
                ToolTip = "Remove from recents",
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                Tag = recent
            };
            removeButton.Click += RecentRemoveButton_Click;
            Grid.SetColumn(removeButton, 1);
            header.Children.Add(removeButton);

            menu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = header,
                ToolTip = recent.Path,
                StaysOpenOnClick = true,
                Style = (Style)FindResource("PejPassContextMenuItem"),
                Command = viewModel.SelectRecentVaultCommand,
                CommandParameter = recent
            });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Clear all recents",
            Style = (Style)FindResource("PejPassContextMenuItem"),
            Command = viewModel.ClearRecentVaultsCommand,
            CommandParameter = this
        });

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void RecentRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button { Tag: RecentVaultItem recent } &&
            DataContext is LoginViewModel viewModel)
        {
            viewModel.RemoveRecentVaultCommand.Execute(recent);
        }
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
