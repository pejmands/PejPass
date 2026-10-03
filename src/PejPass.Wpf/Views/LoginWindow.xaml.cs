using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

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
                Width = 320
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
                Content = new TextBlock
                {
                    Text = "×",
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 1)
                },
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Focusable = true,
                IsTabStop = true,
                ToolTip = "Remove from recents",
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = recent
            };
            AutomationProperties.SetName(
                removeButton,
                $"Remove {recent.DisplayName} from recents");

            var removeButtonTemplate = new ControlTemplate(typeof(Button));
            var buttonBorder = new FrameworkElementFactory(typeof(Border))
            {
                Name = "ButtonBorder"
            };
            buttonBorder.SetValue(
                Border.CornerRadiusProperty,
                new CornerRadius(5));
            buttonBorder.SetValue(
                Border.BackgroundProperty,
                new TemplateBindingExtension(BackgroundProperty));
            buttonBorder.SetValue(
                Border.BorderBrushProperty,
                new TemplateBindingExtension(BorderBrushProperty));
            buttonBorder.SetValue(
                Border.BorderThicknessProperty,
                new TemplateBindingExtension(BorderThicknessProperty));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(
                HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            contentPresenter.SetValue(
                VerticalAlignmentProperty,
                VerticalAlignment.Center);
            contentPresenter.SetValue(
                HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            contentPresenter.SetValue(
                VerticalAlignmentProperty,
                VerticalAlignment.Center);
            contentPresenter.SetValue(
                ContentPresenter.RecognizesAccessKeyProperty,
                true);
            buttonBorder.AppendChild(contentPresenter);
            removeButtonTemplate.VisualTree = buttonBorder;

            var removeHoverTrigger = new Trigger
            {
                Property = IsMouseOverProperty,
                Value = true
            };
            removeHoverTrigger.Setters.Add(new Setter(
                BackgroundProperty,
                new DynamicResourceExtension("SurfacePressedBrush")));
            removeHoverTrigger.Setters.Add(new Setter(
                ForegroundProperty,
                new DynamicResourceExtension("TextBrush")));

            removeButtonTemplate.Triggers.Add(removeHoverTrigger);

            var removeFocusTrigger = new Trigger
            {
                Property = IsKeyboardFocusedProperty,
                Value = true
            };
            removeFocusTrigger.Setters.Add(new Setter(
                BackgroundProperty,
                new DynamicResourceExtension("SurfaceHoverBrush")));
            removeFocusTrigger.Setters.Add(new Setter(
                ForegroundProperty,
                new DynamicResourceExtension("TextBrush")));
            removeButtonTemplate.Triggers.Add(removeFocusTrigger);

            var removeButtonStyle = new Style(typeof(Button));
            removeButtonStyle.Setters.Add(new Setter(
                BackgroundProperty,
                System.Windows.Media.Brushes.Transparent));
            removeButtonStyle.Setters.Add(new Setter(
                ForegroundProperty,
                new DynamicResourceExtension("MutedBrush")));
            removeButtonStyle.Setters.Add(new Setter(
                BorderThicknessProperty,
                new Thickness(0)));
            removeButtonStyle.Setters.Add(new Setter(
                TemplateProperty,
                removeButtonTemplate));

            removeButton.Style = removeButtonStyle;

            removeButton.AddHandler(
                PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(RecentRemoveButton_PreviewMouseLeftButtonDown),
                true);
            removeButton.Click += RecentRemoveButton_Click;
            Grid.SetColumn(removeButton, 1);
            header.Children.Add(removeButton);

            menu.Items.Add(new MenuItem
            {
                Header = header,
                ToolTip = recent.Path,
                StaysOpenOnClick = true,
                Style = (Style)FindResource("PejPassContextMenuItem"),
                Command = viewModel.SelectRecentVaultCommand,
                CommandParameter = recent
            });
        }

        menu.Items.Add(new Separator
        {
            Style = (Style)FindResource("PejPassContextMenuSeparator"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var clearAllItem = new MenuItem
        {
            Header = "Clear all recents",
            Style = (Style)FindResource("PejPassContextMenuItem")
        };
        clearAllItem.Click += ClearAllRecents_Click;
        menu.Items.Add(clearAllItem);

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void RecentRemoveButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RemoveRecentVault(sender);
    }

    private void RecentRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RemoveRecentVault(sender);
    }

    private void RemoveRecentVault(object sender)
    {
        if (sender is Button { Tag: RecentVaultItem recent } &&
            DataContext is LoginViewModel viewModel)
        {
            RecentVaultsMenu.IsOpen = false;
            viewModel.RemoveRecentVaultCommand.Execute(recent);
        }
    }

    private void ClearAllRecents_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LoginViewModel viewModel)
            return;

        if (DialogService.Confirm(
            "Remove all recent vaults from this list? This will not delete any vault files.",
            "Clear all recents",
            yesText: "Clear all",
            noText: "Cancel"))
        {
            RecentVaultsMenu.IsOpen = false;
            viewModel.ClearRecentVaultsCommand.Execute(null);
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
