using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Views;

public partial class EntryEditorWindow : Window
{
    public VaultEntry? Result { get; private set; }

    public EntryEditorWindow(EntryEditorViewModel viewModel)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);

        DataContext = viewModel;
        Title = viewModel.Original is null ? "Add Entry" : "Edit Entry";

        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(AttachNotesScrollChain, DispatcherPriority.Loaded);
    }

    private void AttachNotesScrollChain()
    {
        ScrollViewer? formScroll = null;
        foreach (var sv in FindVisualChildren<ScrollViewer>(this))
        {
            // Outer form scroller (not the TextBox internal one)
            if (sv.TemplatedParent is null)
            {
                formScroll = sv;
                break;
            }
        }

        foreach (var tb in FindVisualChildren<TextBox>(this))
        {
            if (!tb.AcceptsReturn)
                continue;
            if (tb.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
                continue;

            NestedScrollChain.Attach(tb, formScroll);
            return;
        }
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryEditorViewModel vm)
            return;

        if (string.IsNullOrWhiteSpace(vm.Title))
        {
            DialogService.Warning("Title is required.", "Validation");
            return;
        }

        if (!vm.IsTotpSecretValid())
        {
            DialogService.Warning(
                "TOTP secret is invalid.\n\nEnter a valid Base32 authenticator key, or clear the field to disable TOTP.",
                "Invalid TOTP");
            return;
        }

        Result = vm.ToEntry();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
