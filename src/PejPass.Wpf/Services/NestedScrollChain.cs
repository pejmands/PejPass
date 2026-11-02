using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PejPass.Wpf.Services;

public static class NestedScrollChain
{
    private const double Epsilon = 1.0;
    private const double PixelsPerNotch = 48.0;
    private const double NotchUnit = 120.0;
    private const long WheelGestureTimeoutMilliseconds = 500;

    private static readonly DependencyProperty ParentOverrideProperty =
        DependencyProperty.RegisterAttached(
            "ParentOverride",
            typeof(ScrollViewer),
            typeof(NestedScrollChain),
            new PropertyMetadata(null));

    private static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsAttached",
            typeof(bool),
            typeof(NestedScrollChain),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsHorizontalAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsHorizontalAttached",
            typeof(bool),
            typeof(NestedScrollChain),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsLocalHandlerAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsLocalHandlerAttached",
            typeof(bool),
            typeof(NestedScrollChain),
            new PropertyMetadata(false));

    private static readonly DependencyProperty ParentGestureStateProperty =
        DependencyProperty.RegisterAttached(
            "ParentGestureState",
            typeof(WheelGestureState),
            typeof(NestedScrollChain),
            new PropertyMetadata(null));

    public static void Attach(ScrollViewer inner, ScrollViewer? parentOverride = null)
    {
        if (inner is null)
            return;

        inner.SetValue(IsAttachedProperty, true);
        AttachCore(inner, parentOverride, horizontal: false);
    }

    public static void AttachHorizontal(ScrollViewer inner, ScrollViewer? parentOverride = null)
    {
        if (inner is null)
            return;

        inner.SetValue(IsHorizontalAttachedProperty, true);
        AttachCore(inner, parentOverride, horizontal: true);
    }

    private static void AttachCore(
        ScrollViewer inner,
        ScrollViewer? parentOverride,
        bool horizontal)
    {
        var parent = parentOverride
            ?? inner.GetValue(ParentOverrideProperty) as ScrollViewer
            ?? FindAncestorScrollViewer(inner);

        if (parent is not null)
        {
            inner.SetValue(ParentOverrideProperty, parent);
            AttachParent(parent);
        }
        else
        {
            AttachLocalFallback(inner, horizontal);
        }

        inner.Background ??= Brushes.Transparent;
    }

    private static void AttachParent(ScrollViewer parent)
    {
        if (parent.GetValue(ParentGestureStateProperty) is WheelGestureState)
            return;

        parent.SetValue(ParentGestureStateProperty, new WheelGestureState());
        parent.AddHandler(
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnParentPreviewMouseWheel),
            handledEventsToo: true);
    }

    private static void AttachLocalFallback(ScrollViewer inner, bool horizontal)
    {
        if (inner.GetValue(IsLocalHandlerAttachedProperty) is true)
            return;

        inner.SetValue(IsLocalHandlerAttachedProperty, true);
        var handler = horizontal
            ? new MouseWheelEventHandler(OnUnparentedHorizontalPreviewMouseWheel)
            : new MouseWheelEventHandler(OnUnparentedVerticalPreviewMouseWheel);

        inner.AddHandler(
            UIElement.PreviewMouseWheelEvent,
            handler,
            handledEventsToo: true);
    }

    public static void Attach(PasswordBox passwordBox, ScrollViewer? parentOverride = null)
    {
        if (passwordBox is null)
            return;

        void TryAttach()
        {
            passwordBox.ApplyTemplate();
            var inner = FindDescendantScrollViewer(passwordBox);
            if (inner is not null)
            {
                Attach(inner, parentOverride);
                return;
            }

            passwordBox.Dispatcher.BeginInvoke(() =>
            {
                passwordBox.ApplyTemplate();
                var scrollViewer = FindDescendantScrollViewer(passwordBox);
                if (scrollViewer is not null)
                    Attach(scrollViewer, parentOverride);
            }, DispatcherPriority.Loaded);
        }

        if (passwordBox.IsLoaded)
            TryAttach();
        else
            passwordBox.Loaded += (_, _) => TryAttach();
    }

    public static void Attach(TextBox textBox, ScrollViewer? parentOverride = null)
    {
        if (textBox is null)
            return;

        void TryAttach()
        {
            textBox.ApplyTemplate();
            var inner = FindDescendantScrollViewer(textBox);
            if (inner is not null)
            {
                Attach(inner, parentOverride);
                return;
            }

            textBox.Dispatcher.BeginInvoke(() =>
            {
                textBox.ApplyTemplate();
                var scrollViewer = FindDescendantScrollViewer(textBox);
                if (scrollViewer is not null)
                    Attach(scrollViewer, parentOverride);
            }, DispatcherPriority.Loaded);
        }

        if (textBox.IsLoaded)
            TryAttach();
        else
            textBox.Loaded += (_, _) => TryAttach();
    }

    private static void OnParentPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer parent || e.Handled)
            return;

        var state = parent.GetValue(ParentGestureStateProperty) as WheelGestureState;
        if (state is null)
            return;

        var candidate = FindNestedScrollViewer(parent, e.OriginalSource as DependencyObject);
        var owner = state.GetOwner(parent, candidate, Environment.TickCount64);

        if (ReferenceEquals(owner, parent))
        {
            ScrollBy(parent, GetVerticalOffsetDelta(e.Delta));
            e.Handled = true;
            return;
        }

        var isHorizontal = owner.GetValue(IsHorizontalAttachedProperty) is true;
        if (isHorizontal)
        {
            e.Handled = HandleHorizontalWheel(owner, parent, e.Delta);
            return;
        }

        HandleVerticalWheel(owner, parent, e.Delta);
        e.Handled = true;
    }

    private static void OnUnparentedVerticalPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner || e.Handled)
            return;

        var parent = inner.GetValue(ParentOverrideProperty) as ScrollViewer
                     ?? FindAncestorScrollViewer(inner);

        if (parent is not null)
        {
            inner.SetValue(ParentOverrideProperty, parent);
            AttachParent(parent);
            HandleVerticalWheel(inner, parent, e.Delta);
            e.Handled = true;
            return;
        }

        HandleVerticalWheel(inner, parent: null, e.Delta);
        e.Handled = true;
    }

    private static void OnUnparentedHorizontalPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner || e.Handled)
            return;

        var parent = inner.GetValue(ParentOverrideProperty) as ScrollViewer
                     ?? FindAncestorScrollViewer(inner);

        if (parent is not null)
        {
            inner.SetValue(ParentOverrideProperty, parent);
            AttachParent(parent);
            e.Handled = HandleHorizontalWheel(inner, parent, e.Delta);
            return;
        }

        e.Handled = HandleHorizontalWheel(inner, parent: null, e.Delta);
    }

    private static void HandleVerticalWheel(
        ScrollViewer inner,
        ScrollViewer? parent,
        int delta)
    {
        var offsetDelta = GetVerticalOffsetDelta(delta);

        if (inner.ScrollableHeight <= Epsilon)
        {
            ScrollBy(parent, offsetDelta);
            return;
        }

        var atTop = inner.VerticalOffset <= Epsilon;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - Epsilon;

        if ((offsetDelta > 0 && atBottom) ||
            (offsetDelta < 0 && atTop))
        {
            ScrollBy(parent, offsetDelta);
            return;
        }

        ScrollBy(inner, offsetDelta);
    }

    private static bool HandleHorizontalWheel(
        ScrollViewer inner,
        ScrollViewer? parent,
        int delta)
    {
        var offsetDelta = GetVerticalOffsetDelta(delta);

        if (inner.ScrollableWidth <= Epsilon)
        {
            if (parent is null || parent.ScrollableHeight <= Epsilon)
                return false;

            ScrollBy(parent, offsetDelta);
            return true;
        }

        var atStart = inner.HorizontalOffset <= Epsilon;
        var atEnd = inner.HorizontalOffset >= inner.ScrollableWidth - Epsilon;

        if ((offsetDelta < 0 && atStart) ||
            (offsetDelta > 0 && atEnd))
        {
            if (parent is null || parent.ScrollableHeight <= Epsilon)
                return false;

            ScrollBy(parent, offsetDelta);
            return true;
        }

        inner.ScrollToHorizontalOffset(
            Math.Clamp(
                inner.HorizontalOffset + offsetDelta,
                0,
                inner.ScrollableWidth));

        return true;
    }

    private static double GetVerticalOffsetDelta(int delta) =>
        -(delta / NotchUnit) * PixelsPerNotch;

    private static void ScrollBy(ScrollViewer? scrollViewer, double offsetDelta)
    {
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= Epsilon)
            return;

        scrollViewer.ScrollToVerticalOffset(
            Math.Clamp(
                scrollViewer.VerticalOffset + offsetDelta,
                0,
                scrollViewer.ScrollableHeight));
    }

    private static ScrollViewer? FindNestedScrollViewer(
        ScrollViewer parent,
        DependencyObject? originalSource)
    {
        var current = originalSource;

        while (current is not null && !ReferenceEquals(current, parent))
        {
            if (current is ScrollViewer inner)
            {
                var configuredParent = inner.GetValue(ParentOverrideProperty) as ScrollViewer;
                if (configuredParent is null || ReferenceEquals(configuredParent, parent))
                    return inner;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject child)
    {
        var current = GetParent(child);

        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;

            current = GetParent(current);
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        var current = GetParent(child);

        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is Visual or Visual3D)
            return VisualTreeHelper.GetParent(child);

        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement)
                ?? (contentElement as FrameworkContentElement)?.Parent;
        }

        return LogicalTreeHelper.GetParent(child);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer direct)
            return direct;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }

    private sealed class WheelGestureState
    {
        private ScrollViewer? _owner;
        private long _lastWheelAt;

        public ScrollViewer GetOwner(
            ScrollViewer parent,
            ScrollViewer? candidate,
            long timestamp)
        {
            var gestureExpired = timestamp - _lastWheelAt > WheelGestureTimeoutMilliseconds;
            var ownerWasDetached = _owner is not null &&
                !ReferenceEquals(_owner, parent) &&
                !IsDescendantOf(_owner, parent);

            if (_owner is null || gestureExpired || ownerWasDetached)
                _owner = candidate ?? parent;

            _lastWheelAt = timestamp;
            return _owner;
        }
    }
}
