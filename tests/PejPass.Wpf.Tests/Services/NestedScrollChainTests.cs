using PejPass.Wpf.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.Services;

public sealed class NestedScrollChainTests
{
    [Fact(Timeout = 15000)]
    public void ParentStartedWheelGesture_KeepsScrollingParentWhenContentMovesUnderPointer()
    {
        WpfTestHost.Run(_ =>
        {
            using var fixture = new NestedScrollFixture();

            RaiseWheel(fixture.BeforeChild, -120);
            fixture.Window.UpdateLayout();

            var parentOffsetAfterFirstTick = fixture.Parent.VerticalOffset;
            Assert.True(parentOffsetAfterFirstTick > 0);

            RaiseWheel(fixture.InnerContent, -120);
            fixture.Window.UpdateLayout();

            Assert.Equal(0, fixture.Inner.VerticalOffset);
            Assert.True(fixture.Parent.VerticalOffset > parentOffsetAfterFirstTick);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void ChildStartedWheelGesture_KeepsScrollingChildWhenPointerMovesOutsideIt()
    {
        WpfTestHost.Run(_ =>
        {
            using var fixture = new NestedScrollFixture();

            RaiseWheel(fixture.InnerContent, -120);
            fixture.Window.UpdateLayout();

            var innerOffsetAfterFirstTick = fixture.Inner.VerticalOffset;
            Assert.True(innerOffsetAfterFirstTick > 0);

            RaiseWheel(fixture.BeforeChild, -120);
            fixture.Window.UpdateLayout();

            Assert.True(fixture.Inner.VerticalOffset > innerOffsetAfterFirstTick);
            Assert.Equal(0, fixture.Parent.VerticalOffset);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void WheelGestureStartedAfterPause_UsesNewPointerTarget()
    {
        WpfTestHost.Run(_ =>
        {
            using var fixture = new NestedScrollFixture();

            RaiseWheel(fixture.BeforeChild, -120);
            fixture.Window.UpdateLayout();
            Assert.True(fixture.Parent.VerticalOffset > 0);

            Thread.Sleep(550);

            RaiseWheel(fixture.InnerContent, -120);
            fixture.Window.UpdateLayout();

            Assert.True(fixture.Inner.VerticalOffset > 0);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void ChildAtScrollBoundary_PassesWheelToParent()
    {
        WpfTestHost.Run(_ =>
        {
            using var fixture = new NestedScrollFixture();

            fixture.Inner.ScrollToVerticalOffset(fixture.Inner.ScrollableHeight);
            fixture.Window.UpdateLayout();

            RaiseWheel(fixture.InnerContent, -120);
            fixture.Window.UpdateLayout();

            Assert.True(fixture.Parent.VerticalOffset > 0);
        }, TestContext.Current.CancellationToken);
    }

    private static void RaiseWheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent
        };

        target.RaiseEvent(args);
    }

    private sealed class NestedScrollFixture : IDisposable
    {
        public Window Window { get; }
        public ScrollViewer Parent { get; }
        public ScrollViewer Inner { get; }
        public StackPanel InnerContent { get; }
        public Border BeforeChild { get; }

        public NestedScrollFixture()
        {
            InnerContent = new StackPanel { Height = 600 };
            for (var index = 0; index < 12; index++)
            {
                InnerContent.Children.Add(new Border
                {
                    Height = 50,
                    Background = Brushes.Transparent
                });
            }

            Inner = new ScrollViewer
            {
                Height = 100,
                Width = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = InnerContent
            };

            BeforeChild = new Border
            {
                Height = 80,
                Background = Brushes.Transparent
            };

            var content = new StackPanel
            {
                Width = 220
            };
            content.Children.Add(BeforeChild);
            content.Children.Add(Inner);
            content.Children.Add(new Border
            {
                Height = 500,
                Background = Brushes.Transparent
            });

            Parent = new ScrollViewer
            {
                Width = 250,
                Height = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };

            Window = new Window
            {
                Width = 300,
                Height = 250,
                Content = Parent,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -1000,
                Top = -1000
            };

            NestedScrollChain.Attach(Inner, Parent);
            Window.Show();
            Window.UpdateLayout();

            Assert.True(Parent.ScrollableHeight > 0);
            Assert.True(Inner.ScrollableHeight > 0);
        }

        public void Dispose()
        {
            Window.Close();
        }
    }
}
