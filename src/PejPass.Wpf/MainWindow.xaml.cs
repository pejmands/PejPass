using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using PejPass.Domain.Entities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf;

public partial class MainWindow : Window
{
    private readonly VaultSession _vaultSession;
    private readonly DispatcherTimer _snackbarTimer;
    private Point _dragStartPoint;
    private VaultEntry? _dragStartEntry;


    public MainWindow(MainViewModel viewModel, VaultSession vaultSession)
    {
        InitializeComponent();
        DataContext = viewModel;

        _vaultSession = vaultSession;

        _snackbarTimer = new DispatcherTimer();
        _snackbarTimer.Tick += (_, _) => HideSnackbar();
        SnackbarService.Shown += OnSnackbarShown;


        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.HasSelection)
                or nameof(MainViewModel.HasNotes)
                or nameof(MainViewModel.SelectedEntry))
            {
                Dispatcher.BeginInvoke(AttachNotesScrollChain,
                    DispatcherPriority.Loaded);
            }

            if (e.PropertyName is nameof(MainViewModel.ShowTagFilters))
            {
                Dispatcher.BeginInvoke(AttachTagFilterMouseWheel,
                    DispatcherPriority.Loaded);
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
            FaviconService.OnlineFetchingChanged -= OnOnlineFetchingChanged;
            SnackbarService.Shown -= OnSnackbarShown;
            _snackbarTimer.Stop();
            SnackbarBorder.Visibility = Visibility.Collapsed;
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
            }, DispatcherPriority.Loaded);
        };

        PreviewKeyDown += OnPreviewKeyDown;
        EntryList.SelectionChanged += (_, _) => UpdateSelectedEntriesToolbar();
        EntryList.PreviewMouseLeftButtonDown += EntryList_PreviewMouseLeftButtonDown;
        EntryList.PreviewMouseMove += EntryList_PreviewMouseMove;
        EntryList.DragOver += EntryList_DragOver;
        EntryList.Drop += EntryList_Drop;
        FaviconService.OnlineFetchingChanged += OnOnlineFetchingChanged;

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
            }, DispatcherPriority.Loaded);
        };
    }

    private void ClearContentFilters_Click(object sender, RoutedEventArgs e)
    {
        ContentFilterPopup.IsOpen = false;
    }

    private void TagSortMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainViewModel vm)
            return;

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("PejPassContextMenu"),
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            FlowDirection = FlowDirection.LeftToRight
        };

        var mostUsedItem = new MenuItem
        {
            Header = vm.SelectedTagSortIndex == 0 ? "✓  Most used" : "Most used",
            Style = (Style)FindResource("PejPassContextMenuItem")
        };
        mostUsedItem.Click += (_, _) => vm.SelectedTagSortIndex = 0;

        var alphabeticalItem = new MenuItem
        {
            Header = vm.SelectedTagSortIndex == 1 ? "✓  Alphabetical" : "Alphabetical",
            Style = (Style)FindResource("PejPassContextMenuItem")
        };
        alphabeticalItem.Click += (_, _) => vm.SelectedTagSortIndex = 1;

        menu.Items.Add(mostUsedItem);
        menu.Items.Add(alphabeticalItem);
        menu.IsOpen = true;
    }

    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(EntryList);
        _dragStartEntry = FindEntryFromSource(e.OriginalSource as DependencyObject);
    }

    private void EntryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _dragStartEntry is null ||
            DataContext is not MainViewModel vm ||
            vm.SelectedSortIndex != (int)Domain.Settings.EntrySortMode.Manual)
        {
            return;
        }

        var position = e.GetPosition(EntryList);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var draggedEntries = EntryList.SelectedItems
            .Cast<VaultEntry>()
            .Contains(_dragStartEntry)
                ? EntryList.SelectedItems.Cast<VaultEntry>()
                    .OrderBy(entry => EntryList.Items.IndexOf(entry))
                    .ToList()
                : new List<VaultEntry> { _dragStartEntry };

        if (draggedEntries.Count == 0)
            return;

        var data = new DataObject(typeof(List<VaultEntry>), draggedEntries);
        DragDrop.DoDragDrop(EntryList, data, DragDropEffects.Move);
        _dragStartEntry = null;
    }

    private void EntryList_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            vm.SelectedSortIndex != (int)Domain.Settings.EntrySortMode.Manual ||
            !e.Data.GetDataPresent(typeof(List<VaultEntry>)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var dragged = e.Data.GetData(typeof(List<VaultEntry>)) as List<VaultEntry>;
        var target = FindEntryFromSource(e.OriginalSource as DependencyObject);

        e.Effects = dragged is { Count: > 0 } &&
                    target is not null &&
                    !dragged.Any(entry => entry.Id == target.Id) &&
                    dragged.All(entry => entry.IsFavorite == target.IsFavorite)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void EntryList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            vm.SelectedSortIndex != (int)Domain.Settings.EntrySortMode.Manual ||
            e.Data.GetData(typeof(List<VaultEntry>)) is not List<VaultEntry> dragged ||
            FindEntryFromSource(e.OriginalSource as DependencyObject) is not { } target ||
            dragged.Any(entry => entry.Id == target.Id) ||
            dragged.Any(entry => entry.IsFavorite != target.IsFavorite))
        {
            return;
        }

        var item = ItemsControl.ContainerFromElement(EntryList, e.OriginalSource as DependencyObject)
            as ListBoxItem;
        var insertAfter = item is not null &&
                          e.GetPosition(item).Y >= item.ActualHeight / 2;

        e.Handled = true;
        await vm.MoveEntriesAsync(dragged, target, insertAfter);
    }

    private VaultEntry? FindEntryFromSource(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, EntryList))
        {
            if (source is ListBoxItem item)
                return item.DataContext as VaultEntry;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void UpdateSelectedEntriesToolbar()
    {
        var count = EntryList.SelectedItems.Count;
        var isMultiSelection = count > 1;

        SelectedEntriesCountText.Text = $"{count} items selected";
        MultiSelectionPanel.Visibility = isMultiSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        EntryDetailsScrollViewer.Visibility = count == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var selected = EntryList.SelectedItems.Cast<Domain.Entities.VaultEntry>().ToArray();
        if (vm.DeleteSelectedEntriesCommand.CanExecute(selected))
            vm.DeleteSelectedEntriesCommand.Execute(selected);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (IsTypingInInput())
        {
            if (ctrl && e.Key == Key.L)
            {
                vm.LockCommand.Execute(null);
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.K)
            {
                FocusSearch();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.SelectedEntry = null;
            e.Handled = true;
            return;
        }

        if (!ctrl)
        {
            if (e.Key == Key.Enter &&
                EntryList.IsKeyboardFocusWithin &&
                vm.SelectedEntry is not null &&
                vm.EditEntryCommand.CanExecute(vm.SelectedEntry))
            {
                vm.EditEntryCommand.Execute(vm.SelectedEntry);
                e.Handled = true;
                return;
            }

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

    private void OnOnlineFetchingChanged(bool enabled)
    {
        if (!enabled)
            return;

        if (DataContext is MainViewModel vm)
            FaviconService.Prefetch(vm.Entries.Select(e => (e.Url, e.Title)));

        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded)
                EntryList.Items.Refresh();
        }, DispatcherPriority.Background);
    }

    private void OnSnackbarShown(object? sender, SnackbarEventArgs e)
    {
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

    private void FocusSearch()
    {
        SearchBox.FocusInput();
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

        target.RemoveHandler(PreviewMouseWheelEvent,
            (MouseWheelEventHandler)TagFilterScroll_OnPreviewMouseWheel);
        target.AddHandler(PreviewMouseWheelEvent,
            (MouseWheelEventHandler)TagFilterScroll_OnPreviewMouseWheel,
            handledEventsToo: true);

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
