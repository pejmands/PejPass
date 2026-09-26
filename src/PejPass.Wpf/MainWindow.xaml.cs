using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf;

public partial class MainWindow : Window
{
    private readonly VaultSession _vaultSession;

    public MainWindow(MainViewModel viewModel, VaultSession vaultSession)
    {
        InitializeComponent();
        DataContext = viewModel;

        _vaultSession = vaultSession;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.HasSelection)
                or nameof(MainViewModel.HasNotes)
                or nameof(MainViewModel.SelectedEntry))
            {
                Dispatcher.BeginInvoke(AttachNotesScrollChain,
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (e.PropertyName is nameof(MainViewModel.ShowTagFilters))
            {
                Dispatcher.BeginInvoke(AttachTagFilterMouseWheel,
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        viewModel.RequestLock += (_, _) =>
        {
            var login = App.Services.GetRequiredService<LoginWindow>();
            App.PrepareCustomChrome(login);

            System.Windows.Application.Current.MainWindow = login;
            login.Show();
            Close();
        };

        Closed += (_, _) =>
        {
            viewModel.StopBackgroundTimers();

            if (System.Windows.Application.Current?.Windows.OfType<LoginWindow>().Any(w => w.IsVisible) == true)
                return;

            _vaultSession.Clear();
            System.Windows.Application.Current?.Shutdown();
        };

        viewModel.RequestScrollToEntry += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (viewModel.SelectedEntry is null) return;
                EntryList.ScrollIntoView(viewModel.SelectedEntry);
                EntryList.UpdateLayout();
                EntryList.ScrollIntoView(viewModel.SelectedEntry);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };

        PreviewKeyDown += OnPreviewKeyDown;

        FaviconService.FaviconsBatchReady += () =>
        {
            try { EntryList.Items.Refresh(); }
            catch { /* list may be disposing */ }
        };

        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                FaviconService.Prefetch(vm.Entries.Select(e => (e.Url, e.Title)));

            Dispatcher.BeginInvoke(() =>
            {
                AttachTagFilterMouseWheel();
                AttachNotesScrollChain();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (e.Key == Key.Escape)
        {
            vm.SelectedEntry = null;
            e.Handled = true;
            return;
        }

        if (IsTypingInInput())
        {
            if (ctrl && e.Key == Key.L)
            {
                vm.LockCommand.Execute(null);
                e.Handled = true;
            }
            else if (ctrl && (e.Key == Key.F || e.Key == Key.K))
            {
                FocusSearch();
                e.Handled = true;
            }
            return;
        }

        if (!ctrl)
        {
            if (e.Key == Key.Delete && vm.SelectedEntry is not null)
            {
                if (vm.DeleteEntryCommand.CanExecute(vm.SelectedEntry))
                    vm.DeleteEntryCommand.Execute(vm.SelectedEntry);
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.F:
            case Key.K:
                FocusSearch();
                e.Handled = true;
                break;

            case Key.N:
                if (vm.AddEntryCommand.CanExecute(null))
                    vm.AddEntryCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.E:
                if (vm.SelectedEntry is not null && vm.EditEntryCommand.CanExecute(vm.SelectedEntry))
                    vm.EditEntryCommand.Execute(vm.SelectedEntry);
                e.Handled = true;
                break;

            case Key.L:
                vm.LockCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.C when shift:
                vm.CopyUsernameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.C:
                vm.CopyPasswordCommand.Execute(vm.SelectedEntry);
                e.Handled = true;
                break;

            case Key.T when shift:
                if (vm.OpenTrashCommand.CanExecute(null))
                    vm.OpenTrashCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.T:
                vm.CopyTotpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.B:
                if (vm.ExportBackupCommand.CanExecute(null))
                    vm.ExportBackupCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.H:
                if (vm.OpenHealthCommand.CanExecute(null))
                    vm.OpenHealthCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S:
                if (vm.OpenSettingsCommand.CanExecute(null))
                    vm.OpenSettingsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private static bool IsTypingInInput()
    {
        return Keyboard.FocusedElement is TextBox or PasswordBox;
    }

    private void AttachTagFilterMouseWheel()
    {
        ScrollViewer? target = FindName("TagFilterScroll") as ScrollViewer;

        if (target is null)
        {
            foreach (var sv in FindVisualChildren<ScrollViewer>(this))
            {
                if (sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                    && sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                    && sv.Height is >= 36 and <= 52)
                {
                    target = sv;
                    break;
                }
            }
        }

        if (target is null)
            return;

        target.RemoveHandler(UIElement.PreviewMouseWheelEvent,
            (MouseWheelEventHandler)TagFilterScroll_OnPreviewMouseWheel);
        target.AddHandler(UIElement.PreviewMouseWheelEvent,
            (MouseWheelEventHandler)TagFilterScroll_OnPreviewMouseWheel,
            handledEventsToo: true);

        foreach (var btn in FindVisualChildren<Button>(target))
            btn.ToolTip = null;
    }

    private void TagFilterScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        if (sv.ExtentWidth <= sv.ViewportWidth + 0.5)
            return;

        var notches = e.Delta / 120.0;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - notches * 48.0);
        e.Handled = true;
    }

    private void AttachNotesScrollChain()
    {
        ScrollViewer? notes = null;
        ScrollViewer? detail = null;

        foreach (var sv in FindVisualChildren<ScrollViewer>(this))
        {
            if (notes is null
                && sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                && !double.IsInfinity(sv.MaxHeight)
                && !double.IsNaN(sv.MaxHeight)
                && Math.Abs(sv.MaxHeight - 160) < 0.5)
            {
                notes = sv;
                continue;
            }
        }

        if (notes is not null)
        {
            var current = VisualTreeHelper.GetParent(notes);
            while (current is not null)
            {
                if (current is ScrollViewer sv
                    && sv.VerticalScrollBarVisibility == ScrollBarVisibility.Auto
                    && !ReferenceEquals(sv, notes))
                {
                    detail = sv;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        if (notes is not null)
            NestedScrollChain.Attach(notes, detail);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
