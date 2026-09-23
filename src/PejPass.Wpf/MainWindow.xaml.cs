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
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.HasSelection)
                or nameof(MainViewModel.HasNotes)
                or nameof(MainViewModel.SelectedEntry))
            {
                Dispatcher.BeginInvoke(AttachNotesScrollChain,
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        viewModel.RequestLock += (_, _) =>
        {
            var login = App.Services.GetRequiredService<LoginWindow>();
            System.Windows.Application.Current.MainWindow = login;
            login.Show();
            Close();
        };

        Closed += (_, _) =>
        {
            viewModel.StopBackgroundTimers();

            if (System.Windows.Application.Current?.Windows.OfType<LoginWindow>().Any(w => w.IsVisible) == true)
                return;

            LoginViewModel.ClearSession();
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

            AttachTagFilterMouseWheel();
            Dispatcher.BeginInvoke(AttachNotesScrollChain, System.Windows.Threading.DispatcherPriority.Loaded);
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
        if (FindName("TagFilterScroll") is ScrollViewer named)
        {
            named.PreviewMouseWheel -= TagFilterScroll_OnPreviewMouseWheel;
            named.PreviewMouseWheel += TagFilterScroll_OnPreviewMouseWheel;
            return;
        }

        foreach (var sv in FindVisualChildren<ScrollViewer>(this))
        {
            if (sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                && sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                && sv.Height is >= 36 and <= 52)
            {
                sv.PreviewMouseWheel -= TagFilterScroll_OnPreviewMouseWheel;
                sv.PreviewMouseWheel += TagFilterScroll_OnPreviewMouseWheel;
                return;
            }
        }
    }

    private void TagFilterScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        if (sv.ExtentWidth <= sv.ViewportWidth)
            return;

        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void AttachNotesScrollChain()
    {
        ScrollViewer? notes = null;
        ScrollViewer? detail = null;

        foreach (var sv in FindVisualChildren<ScrollViewer>(this))
        {
            // Notes strip: fixed MaxHeight 160, no horizontal scroll
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

        // Detail pane: the vertical Auto scroller that contains the notes strip
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
