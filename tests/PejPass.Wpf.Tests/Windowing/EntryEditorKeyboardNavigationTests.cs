using PejPass.Wpf.ViewModels;
using PejPass.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PejPass.Wpf.Tests.Windowing;

public sealed class EntryEditorKeyboardNavigationTests
{
    [Fact(Timeout = 15000)]
    public void EmptyCustomFieldsContainer_IsSkippedAfterAddFieldButton()
    {
        WpfTestHost.Run(_ =>
        {
            var window = new EntryEditorWindow(new EntryEditorViewModel(null));
            var addFieldButton = Assert.IsType<Button>(window.FindName("AddCustomFieldButton"));
            var notesTextBox = Assert.IsType<TextBox>(window.FindName("NotesTextBox"));
            var customFields = Assert.IsType<ItemsControl>(window.FindName("CustomFieldsItemsControl"));

            window.Show();
            window.UpdateLayout();

            Assert.False(customFields.Focusable);
            Assert.False(customFields.IsTabStop);

            Keyboard.Focus(addFieldButton);

            Assert.True(addFieldButton.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(notesTextBox, Keyboard.FocusedElement);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void PopulatedCustomFieldsContainer_DoesNotAddAnExtraTabStop()
    {
        WpfTestHost.Run(_ =>
        {
            var viewModel = new EntryEditorViewModel(null);
            viewModel.AddCustomFieldCommand.Execute(null);

            var window = new EntryEditorWindow(viewModel);
            var addFieldButton = Assert.IsType<Button>(window.FindName("AddCustomFieldButton"));
            var customFields = Assert.IsType<ItemsControl>(window.FindName("CustomFieldsItemsControl"));

            window.Show();
            window.UpdateLayout();

            var firstFieldName = FindVisualChildren<TextBox>(customFields).First();

            Assert.False(customFields.Focusable);
            Assert.False(customFields.IsTabStop);

            Keyboard.Focus(addFieldButton);

            Assert.True(addFieldButton.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(firstFieldName, Keyboard.FocusedElement);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
