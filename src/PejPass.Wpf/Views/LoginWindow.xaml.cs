using Microsoft.Extensions.DependencyInjection;
using PejPass.Domain.Security;
using PejPass.Domain.Settings;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PejPass.Wpf.Views;

public partial class LoginWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _snackbarTimer;
    private bool _isSubscribedToSnackbar;
    private bool _allowClose;
    private bool _preserveMinimizedStateOnNextChange;
    internal bool SuppressMainWindowOnUnlock { get; private set; }
    private bool? _browserUnlockShouldShowMainWindow;
    private static int _passwordStrengthWarmupStarted;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/PejPassLocked.ico", UriKind.Absolute));
        DataContext = viewModel;
        StartPasswordStrengthWarmup();

        _settings = App.Services.GetRequiredService<AppSettings>();

        _snackbarTimer = new DispatcherTimer();
        _snackbarTimer.Tick += (_, _) => HideSnackbar();

        IsVisibleChanged += (_, _) => SetSnackbarSubscription(IsVisible);
        Closed += (_, _) =>
        {
            SetSnackbarSubscription(false);
            _snackbarTimer.Stop();
            SnackbarBorder.Visibility = Visibility.Collapsed;
        };

        TitleBar.SettingsRequested += (_, _) => OpenSettings();
        TitleBar.AboutRequested += (_, _) => OpenAbout();
        TitleBar.WhatsNewRequested += (_, _) => OpenWhatsNew();

        viewModel.RecentVaultSelected += (_, _) =>
        {
            ClearPasswordInputs();

            Dispatcher.BeginInvoke(
                () => MasterPasswordBox.FocusInput(),
                DispatcherPriority.Input);
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
            ClearPasswordInputs();

            var main = App.Services.GetRequiredService<MainWindow>();

            var vaultFilePath = viewModel.VaultPath;
            var vaultFileName = Path.GetFileNameWithoutExtension(vaultFilePath);
            main.Title = string.IsNullOrWhiteSpace(vaultFileName)
                ? "PejPass"
                : $"PejPass — {vaultFileName}";

            App.PrepareCustomChrome(main);
            App.SetCustomWindowTitle(main, main.Title, vaultFilePath);

            System.Windows.Application.Current.MainWindow = main;

            // Transition to main: allow close without triggering tray minimize.
            // Browser unlock preserves the visibility state captured before the request.
            _allowClose = true;
            if (!SuppressMainWindowOnUnlock ||
                _browserUnlockShouldShowMainWindow == true)
            {
                main.Show();
            }
            else if (main.IsVisible)
            {
                main.Hide();
            }

            Close();
        };

        Closing += (_, e) =>
        {
            if (_allowClose)
                return;

            ClearPasswordInputs();

            if (_settings.CloseToSystemTray)
            {
                var tray = App.Services.GetRequiredService<SystemTrayService>();
                if (tray.TryHideToTray(this, e))
                    return;
            }

            var trayForExit = App.Services.GetService<SystemTrayService>();
            trayForExit?.Hide();
            trayForExit?.Dispose();
            System.Windows.Application.Current.Shutdown();
        };

        StateChanged += (_, _) =>
        {
            if (_preserveMinimizedStateOnNextChange)
            {
                _preserveMinimizedStateOnNextChange = false;
                return;
            }

            if (WindowState != WindowState.Minimized ||
                !_settings.MinimizeToSystemTray)
            {
                return;
            }

            var tray = App.Services.GetRequiredService<SystemTrayService>();
            Hide();
            tray.Show();
        };

        Loaded += async (_, _) =>
        {
            await Dispatcher.InvokeAsync(
                () => MasterPasswordBox.FocusInput(),
                DispatcherPriority.Input);

            await viewModel.RefreshWindowsHelloVisibilityAsync();

            // After a portable self-update, greet the user with What's New once.
            if (UpdateService.HasPendingWhatsNew())
            {
                try
                {
                    var whatsNew = new WhatsNewWindow { Owner = this };
                    whatsNew.ShowDialog();
                }
                finally
                {
                    UpdateService.ClearPendingWhatsNew();
                }
            }
        };
    }

    internal void MatchWindowStateForBrowserLock(bool mainWindowWasVisible, WindowState mainWindowState)
    {
        if (!mainWindowWasVisible)
        {
            Hide();
            return;
        }

        if (mainWindowState == WindowState.Minimized)
        {
            Show();

            if (WindowState != WindowState.Minimized)
            {
                _preserveMinimizedStateOnNextChange = true;
                WindowState = WindowState.Minimized;
            }

            return;
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
    }

    internal async Task<bool> UnlockFromBrowserAsync(string masterPassword)
    {
        if (DataContext is not LoginViewModel viewModel)
            return false;

        _browserUnlockShouldShowMainWindow = ShouldShowMainWindowAfterBrowserUnlock();
        SuppressMainWindowOnUnlock = true;
        try
        {
            var unlocked = await viewModel.UnlockFromBrowserAsync(masterPassword);
            if (!unlocked)
                ResetBrowserUnlockState();

            return unlocked;
        }
        catch
        {
            ResetBrowserUnlockState();
            throw;
        }
    }

    internal async Task<bool> UnlockWithHelloFromBrowserAsync()
    {
        if (DataContext is not LoginViewModel viewModel)
            return false;

        _browserUnlockShouldShowMainWindow = ShouldShowMainWindowAfterBrowserUnlock();
        SuppressMainWindowOnUnlock = true;
        try
        {
            var unlocked = await viewModel.UnlockWithHelloFromBrowserAsync();
            if (!unlocked)
                ResetBrowserUnlockState();

            return unlocked;
        }
        catch
        {
            ResetBrowserUnlockState();
            throw;
        }
    }

    private bool ShouldShowMainWindowAfterBrowserUnlock()
    {
        var app = System.Windows.Application.Current;
        var mainWindow = app?.MainWindow as MainWindow
            ?? app?.Windows.OfType<MainWindow>().FirstOrDefault();

        if (mainWindow is not null)
        {
            return mainWindow.IsVisible &&
                mainWindow.WindowState != WindowState.Minimized;
        }

        return IsVisible && WindowState != WindowState.Minimized;
    }

    private void ResetBrowserUnlockState()
    {
        SuppressMainWindowOnUnlock = false;
        _browserUnlockShouldShowMainWindow = null;
    }

    private static void StartPasswordStrengthWarmup()
    {
        if (Interlocked.Exchange(ref _passwordStrengthWarmupStarted, 1) != 0)
            return;

        _ = Task.Run(static () =>
        {
            try
            {
                _ = PasswordStrength.GetScore("warmup");
            }
            catch
            {
                // Warm-up is best-effort; normal password evaluation remains unchanged.
            }
        });
    }

    private void SetSnackbarSubscription(bool subscribe)
    {
        if (_isSubscribedToSnackbar == subscribe)
            return;

        _isSubscribedToSnackbar = subscribe;

        if (subscribe)
        {
            SnackbarService.Shown += OnSnackbarShown;
            return;
        }

        SnackbarService.Shown -= OnSnackbarShown;
        HideSnackbar();
    }

    private void OnSnackbarShown(object? sender, SnackbarEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSnackbarShown(sender, e));
            return;
        }

        SnackbarText.Text = e.Message;

        var brushKey = e.Kind switch
        {
            SnackbarKind.Info => "AccentBrush",
            SnackbarKind.Warning => "WarningBrush",
            SnackbarKind.Error => "DangerBrush",
            _ => "SuccessBrush"
        };

        var icon = e.Kind switch
        {
            SnackbarKind.Info => "\uE946",
            SnackbarKind.Warning => "\uE7BA",
            SnackbarKind.Error => "\uE711",
            _ => "\uE73E"
        };

        var brush = (Brush)FindResource(brushKey);
        SnackbarBorder.BorderBrush = brush;
        SnackbarAccent.Background = brush;
        SnackbarIcon.Foreground = brush;
        SnackbarIcon.Text = icon;

        _snackbarTimer.Stop();
        _snackbarTimer.Interval = e.Duration;
        _snackbarTimer.Start();
        SnackbarBorder.Visibility = Visibility.Visible;
    }

    private void HideSnackbar()
    {
        _snackbarTimer.Stop();
        SnackbarBorder.Visibility = Visibility.Collapsed;
    }

    private void OpenSettings()
    {
        var window = App.Services.GetRequiredService<SettingsWindow>();
        if (window.DataContext is SettingsViewModel vm)
            vm.ConfigureAppearanceOnly(true);

        window.Owner = this;
        window.Title = "Appearance";
        App.SetCustomWindowTitle(window, "Appearance");
        window.Height = 560;
        window.MinHeight = 480;
        window.ShowDialog();
    }

    private void OpenAbout()
    {
        var window = new AboutWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenWhatsNew()
    {
        var window = new WhatsNewWindow { Owner = this };
        window.ShowDialog();
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
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            path.SetResourceReference(TextBlock.FontSizeProperty, "AppFontSize11");
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
                Brushes.Transparent));
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

    internal void ClearPasswordInputs()
    {
        MasterPasswordBox.Clear();
        ConfirmMasterPasswordBox.Clear();
        MasterPasswordBox.IsRevealed = false;
        ConfirmMasterPasswordBox.IsRevealed = false;

        if (DataContext is LoginViewModel viewModel)
            viewModel.ClearSensitiveInputs();
    }

    private void VaultMode_Checked(object sender, RoutedEventArgs e)
    {
        ClearPasswordInputs();

        Dispatcher.BeginInvoke(
            () => MasterPasswordBox.FocusInput(),
            DispatcherPriority.Input);
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
