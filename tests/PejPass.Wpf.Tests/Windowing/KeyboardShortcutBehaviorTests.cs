using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class KeyboardShortcutBehaviorTests
{
    [Fact(Timeout = 15000)]
    public void MainWindow_BusyState_HandlesNonLockKeyboardShortcut()
    {
        WpfTestHost.Run(host =>
        {
            var window = host.CreateMainWindow();
            System.Windows.Application.Current!.MainWindow = window;
            window.Show();

            var vm = Assert.IsType<MainViewModel>(window.DataContext);
            vm.IsBusy = true;

            var source = PresentationSource.FromVisual(window)
                ?? throw new InvalidOperationException("The main window has no presentation source.");

            var args = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                source,
                Environment.TickCount,
                Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };

            window.RaiseEvent(args);

            Assert.True(args.Handled);
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(Key.L, ModifierKeys.Control, true)]
    [InlineData(Key.N, ModifierKeys.Control, false)]
    [InlineData(Key.L, ModifierKeys.None, false)]
    public void IsBusyLockShortcut_OnlyAllowsControlL(
        Key key,
        ModifierKeys modifiers,
        bool expected)
    {
        Assert.Equal(expected, MainWindow.IsBusyLockShortcut(key, modifiers));
    }
}
