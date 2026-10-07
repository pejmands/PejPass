using Microsoft.Extensions.DependencyInjection;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Adorners;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf;

public partial class MainWindow : Window
{
    private readonly VaultSession _vaultSession;
    private readonly DispatcherTimer _snackbarTimer;
    private readonly AppSettings _settings;
    private bool _fastDragScrollHintShown;
    private bool _fastDragScrollUsed;
    private Point _dragStartPoint;
    private VaultEntry? _dragStartEntry;
    private DropIndicatorAdorner? _dropIndicatorAdorner;
    private VaultEntry? _lastDropTarget;
    private bool _lastDropInsertAfter;

    /// <summary>
    /// Set when we intentionally close MainWindow to switch to LoginWindow (lock).
    /// Prevents minimize-to-tray from cancelling that transition.
    /// </summary>
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel, VaultSession vaultSession)
    {
        InitializeComponent();
        DataContext = viewModel;

        _vaultSession = vaultSession;
        _settings = App.Services.GetRequiredService<AppSettings>();

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

        viewModel.RequestLock += (_, e) =>
        {
            _allowClose = true;
            var login = App.Services.GetRequiredService<LoginWindow>();
            App.PrepareCustomChrome(login);

            System.Windows.Application.Current.MainWindow = login;

            if (e.ShowLogin)
                login.Show();
            else
                login.Hide();

            Close();
        };

        Closing += (_, e) =>
        {
            if (_allowClose)
                return;

            // Only redirect close to the tray when the user enabled the setting.
            var tray = App.Services.GetRequiredService<SystemTrayService>();
            if (_settings.MinimizeToSystemTray && tray.TryHideToTray(this, e))
                return;
        };

        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                return;

            // Minimize button → tray only when the user enabled the setting.
            if (!_settings.MinimizeToSystemTray)
                return;

            var tray = App.Services.GetRequiredService<SystemTrayService>();
            Hide();
            tray.Show();
        };

        Closed += (_, _) =>
        {
            FaviconService.OnlineFetchingChanged -= OnOnlineFetchingChanged;
            SnackbarService.Shown -= OnSnackbarShown;

            _snackbarTimer.Stop();
            SnackbarBorder.Visibility = Visibility.Collapsed;

            if (_dropIndicatorAdorner is not null)
            {
                var layer = AdornerLayer.GetAdornerLayer(EntryList);
                layer?.Remove(_dropIndicatorAdorner);
                _dropIndicatorAdorner = null;
            }

            viewModel.StopBackgroundTimers();

            if (System.Windows.Application.Current?.Windows.OfType<LoginWindow>().Any(w => w.IsVisible) == true)
                return;

            // If we are still running because of tray mode, do not clear the session
            // or force shutdown — the window was merely hidden.
            var tray = App.Services.GetService<SystemTrayService>();
            if (_settings.MinimizeToSystemTray && tray is { IsVisible: true })
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
        EntryList.PreviewMouseRightButtonDown += EntryList_PreviewMouseRightButtonDown;
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
                AttachDropIndicatorAdorner();

                AttachTagFilterMouseWheel();
                AttachNotesScrollChain();
            }, DispatcherPriority.Loaded);
        };
    }

    private void AttachDropIndicatorAdorner()
    {
        if (_dropIndicatorAdorner is not null)
            return;

        var layer = AdornerLayer.GetAdornerLayer(EntryList);

        if (layer is null)
            return;

        _dropIndicatorAdorner = new DropIndicatorAdorner(
            EntryList,
            (Brush)FindResource("AccentBrush"));

        layer.Add(_dropIndicatorAdorner);
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

    private void ManualOrderMenuButton_Click(object sender, RoutedEventArgs e)
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

        var resetItem = new MenuItem
        {
            Header = "Reset Manual Order",
            Style = (Style)FindResource("PejPassContextMenuItem"),
            IsEnabled = vm.SelectedSortIndex == (int)EntrySortMode.Manual
        };

        AddManualOrderResetItem(resetItem, vm, "A → Z", EntrySortMode.TitleAsc);
        AddManualOrderResetItem(resetItem, vm, "Z → A", EntrySortMode.TitleDesc);
        AddManualOrderResetItem(resetItem, vm, "Newest", EntrySortMode.NewestFirst);
        AddManualOrderResetItem(resetItem, vm, "Oldest", EntrySortMode.OldestFirst);

        menu.Items.Add(resetItem);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void AddManualOrderResetItem(
        MenuItem parent,
        MainViewModel vm,
        string header,
        EntrySortMode sortMode)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)FindResource("PejPassContextMenuItem")
        };

        item.Click += async (_, _) => await vm.ResetManualOrderAsync(sortMode);
        parent.Items.Add(item);
    }

    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPendingDrag();

        _dragStartPoint = e.GetPosition(EntryList);
        _dragStartEntry = FindEntryFromSource(e.OriginalSource as DependencyObject);
    }

    private void EntryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _dragStartEntry is null ||
            DataContext is not MainViewModel vm ||
            vm.SelectedSortIndex != (int)EntrySortMode.Manual)
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
                : [_dragStartEntry];

        if (draggedEntries.Count == 0)
            return;

        var data = new DataObject(typeof(List<VaultEntry>), draggedEntries);

        try
        {
            DragDrop.DoDragDrop(EntryList, data, DragDropEffects.Move);
        }
        finally
        {
            if (_fastDragScrollUsed)
                MarkFastDragScrollTipSeen();

            HideFastDragScrollHint();
            _dropIndicatorAdorner?.Hide();
            _fastDragScrollHintShown = false;
            _fastDragScrollUsed = false;
            _dragStartEntry = null;
        }
    }

    private void EntryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPendingDrag();

        if (DataContext is not MainViewModel vm ||
            FindEntryFromSource(e.OriginalSource as DependencyObject) is not { } clickedEntry)
        {
            return;
        }


        if (ItemsControl.ContainerFromElement(
            EntryList, e.OriginalSource as DependencyObject) is not ListBoxItem item)
            return;

        if (!item.IsSelected)
        {
            EntryList.SelectedItems.Clear();
            item.IsSelected = true;
        }

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("PejPassContextMenu"),
            PlacementTarget = item,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            FlowDirection = FlowDirection.LeftToRight
        };

        var selectedEntries = EntryList.SelectedItems
            .Cast<VaultEntry>()
            .ToList();

        if (selectedEntries.Count == 0)
        {
            selectedEntries.Add(clickedEntry);
        }

        var hasMixedFavorites = selectedEntries
            .Select(e => e.IsFavorite)
            .Distinct()
            .Count() > 1;

        var canMoveSelection =
            !hasMixedFavorites &&
            vm.SelectedSortIndex == (int)EntrySortMode.Manual;

        var selectedForMove = GetSelectedEntriesForGroup(clickedEntry);

        var moveToTop = CreateReorderMenuItem("Move to Top");
        moveToTop.IsEnabled =
            canMoveSelection &&
            CanMoveEntriesToEdge(vm, selectedForMove, clickedEntry.IsFavorite, true);

        moveToTop.Click += async (_, _) =>
        {
            await vm.MoveEntriesToEdgeAsync(selectedForMove, clickedEntry.IsFavorite, moveToTop: true);
        };

        var moveToBottom = CreateReorderMenuItem("Move to Bottom");
        moveToBottom.IsEnabled =
            canMoveSelection &&
            CanMoveEntriesToEdge(vm, selectedForMove, clickedEntry.IsFavorite, false);

        moveToBottom.Click += async (_, _) =>
        {
            await vm.MoveEntriesToEdgeAsync(selectedForMove, clickedEntry.IsFavorite, moveToTop: false);
        };

        menu.Items.Add(moveToTop);
        menu.Items.Add(moveToBottom);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CancelPendingDrag()
    {
        _dragStartEntry = null;
        _dragStartPoint = default;

        HideFastDragScrollHint();
        _fastDragScrollHintShown = false;
        _fastDragScrollUsed = false;
    }

    private MenuItem CreateReorderMenuItem(string header) => new()
    {
        Header = header,
        Style = (Style)FindResource("PejPassContextMenuItem")
    };

    private List<VaultEntry> GetSelectedEntriesForGroup(VaultEntry contextEntry)
    {
        var selected = EntryList.SelectedItems
            .Cast<VaultEntry>()
            .Where(entry => entry.IsFavorite == contextEntry.IsFavorite)
            .OrderBy(entry => EntryList.Items.IndexOf(entry))
            .ToList();

        return selected.Count > 0 ? selected : [contextEntry];
    }

    private static bool CanMoveEntriesToEdge(
        MainViewModel vm,
        List<VaultEntry> movingEntries,
        bool isFavorite,
        bool moveToTop)
    {
        if (movingEntries.Count == 0)
            return false;

        var movingIds = movingEntries
            .Select(entry => entry.Id)
            .ToHashSet();

        var group = vm.Entries
            .Where(entry => entry.IsFavorite == isFavorite)
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (group.Count <= movingEntries.Count)
            return false;

        var edgeEntries = moveToTop
            ? group.Take(movingEntries.Count)
            : group.Skip(group.Count - movingEntries.Count);

        return edgeEntries.Any(entry => !movingIds.Contains(entry.Id));
    }

    private void EntryList_DragOver(object sender, DragEventArgs e)
    {
        AutoScrollEntryListDuringDrag(e);

        if (DataContext is not MainViewModel vm ||
            vm.SelectedSortIndex != (int)EntrySortMode.Manual ||
            !e.Data.GetDataPresent(typeof(List<VaultEntry>)))
        {
            _dropIndicatorAdorner?.Hide();
            _lastDropTarget = null;

            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }


        if (e.Data.GetData(typeof(List<VaultEntry>)) is not List<VaultEntry> dragged || dragged.Count == 0)
        {
            _dropIndicatorAdorner?.Hide();
            _lastDropTarget = null;

            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var target = FindEntryFromSource(e.OriginalSource as DependencyObject);

        if (target is not null)
        {
            _lastDropTarget = target;
        }
        else
        {
            target = _lastDropTarget;
        }

        if (target is null)
        {
            _dropIndicatorAdorner?.Hide();

            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }


        if (EntryList.ItemContainerGenerator
            .ContainerFromItem(target) is not ListBoxItem item)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var insertAfter =
            e.GetPosition(item).Y >= item.ActualHeight / 2;

        _lastDropInsertAfter = insertAfter;

        UpdateDropIndicator(target, insertAfter);

        var isValid =
            !dragged.Any(entry => entry.Id == target.Id) &&
            dragged.All(entry => entry.IsFavorite == target.IsFavorite);

        e.Effects = isValid
            ? DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void UpdateDropIndicator(VaultEntry? target, bool insertAfter)
    {
        if (_dropIndicatorAdorner is null || target is null)
            return;

        if (EntryList.ItemContainerGenerator
            .ContainerFromItem(target) is not ListBoxItem item)
            return;

        var point = item.TranslatePoint(
            new Point(0, 0),
            EntryList);

        var y = point.Y + (insertAfter ? item.ActualHeight : 0);

        _dropIndicatorAdorner.ShowAt(y);
    }

    private void AutoScrollEntryListDuringDrag(DragEventArgs e)
    {
        var scrollViewer = FindVisualChildren<ScrollViewer>(EntryList).FirstOrDefault();
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0)
            return;

        var position = e.GetPosition(scrollViewer);
        const double edgeSize = 40;
        var altPressed = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        var isNearTop = position.Y < edgeSize;
        var isNearBottom = position.Y > scrollViewer.ViewportHeight - edgeSize;
        var canScrollUp = scrollViewer.VerticalOffset > 0;
        var canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

        var isInScrollableEdge =
            (isNearTop && canScrollUp) ||
            (isNearBottom && canScrollDown);

        if (isInScrollableEdge && !_settings.HasSeenFastDragScrollTip)
            ShowFastDragScrollHint();

        if (isInScrollableEdge && altPressed)
            _fastDragScrollUsed = true;

        var previousOffset = scrollViewer.VerticalOffset;
        var scrollStep = altPressed ? 100 : 20;

        if (isNearTop && canScrollUp)
        {
            scrollViewer.ScrollToVerticalOffset(
                Math.Max(0, previousOffset - scrollStep));
        }
        else if (isNearBottom && canScrollDown)
        {
            scrollViewer.ScrollToVerticalOffset(
                Math.Min(scrollViewer.ScrollableHeight,
                    previousOffset + scrollStep));
        }


    }

    private void ShowFastDragScrollHint()
    {
        if (_fastDragScrollHintShown)
            return;

        FastDragScrollHintBorder.Visibility = Visibility.Visible;
        FastDragScrollHintBorder.UpdateLayout();
        _fastDragScrollHintShown = true;
    }

    private void HideFastDragScrollHint()
    {
        FastDragScrollHintBorder.Visibility = Visibility.Collapsed;
    }

    private void MarkFastDragScrollTipSeen()
    {
        if (_settings.HasSeenFastDragScrollTip)
            return;

        _settings.HasSeenFastDragScrollTip = true;
        SettingsStore.TrySave(_settings);
    }

    private async void EntryList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            vm.SelectedSortIndex != (int)EntrySortMode.Manual ||
            e.Data.GetData(typeof(List<VaultEntry>)) is not List<VaultEntry> dragged)
        {
            _dropIndicatorAdorner?.Hide();
            _lastDropTarget = null;
            return;
        }

        var target = FindEntryFromSource(e.OriginalSource as DependencyObject)
                     ?? _lastDropTarget;

        if (target is null ||
            dragged.Any(entry => entry.Id == target.Id) ||
            dragged.Any(entry => entry.IsFavorite != target.IsFavorite))
        {
            _dropIndicatorAdorner?.Hide();
            _lastDropTarget = null;
            return;
        }


        if (EntryList.ItemContainerGenerator
            .ContainerFromItem(target) is not ListBoxItem item)
        {
            _dropIndicatorAdorner?.Hide();
            _lastDropTarget = null;
            return;
        }

        var insertAfter = _lastDropTarget == target
            ? _lastDropInsertAfter
            : e.GetPosition(item).Y >= item.ActualHeight / 2;

        e.Handled = true;

        _dropIndicatorAdorner?.Hide();
        _lastDropTarget = null;

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

        var selected = EntryList.SelectedItems.Cast<VaultEntry>().ToArray();
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
