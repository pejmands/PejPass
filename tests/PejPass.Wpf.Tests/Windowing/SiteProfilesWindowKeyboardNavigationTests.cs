using PejPass.Domain.Entities;
using PejPass.Wpf.Controls;
using PejPass.Wpf.Views;
using System.Windows.Controls;
using System.Windows.Input;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class SiteProfilesWindowKeyboardNavigationTests
{
    [Fact(Timeout = 15000)]
    public void TabFromSave_SkipsNonInteractiveContainers()
    {
        WpfTestHost.Run(_ =>
        {
            var window = new SiteProfilesWindow(Array.Empty<SiteProfile>());
            var titleBar = Assert.IsType<AppTitleBar>(window.FindName("WindowTitleBar"));
            var profilesScrollViewer = Assert.IsType<ScrollViewer>(
                window.FindName("ProfilesScrollViewer"));
            var profilesItemsControl = Assert.IsType<ItemsControl>(
                window.FindName("ProfilesItemsControl"));
            var addGroupButton = Assert.IsType<Button>(window.FindName("AddGroupButton"));
            var saveButton = Assert.IsType<Button>(window.FindName("SaveButton"));

            window.Show();
            window.UpdateLayout();

            Assert.False(titleBar.Focusable);
            Assert.False(titleBar.IsTabStop);
            Assert.False(profilesScrollViewer.Focusable);
            Assert.False(profilesScrollViewer.IsTabStop);
            Assert.False(profilesItemsControl.Focusable);
            Assert.False(profilesItemsControl.IsTabStop);

            Keyboard.Focus(saveButton);

            Assert.True(saveButton.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(addGroupButton, Keyboard.FocusedElement);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
