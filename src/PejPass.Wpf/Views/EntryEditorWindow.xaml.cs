using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.ComponentModel;
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
        App.SetCustomWindowTitle(
            this,
            viewModel.Original is null ? "Add Entry" : "Edit Entry");

        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        PasswordBox.Password = viewModel.Password;
        TotpSecretBox.Password = viewModel.TotpSecret;

        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(AttachNestedScrollChains, DispatcherPriority.Loaded);
    }

    private void AttachNestedScrollChains()
    {
        ScrollViewer? formScroll = null;
        foreach (var sv in FindVisualChildren<ScrollViewer>(this))
        {
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
            break;
        }

        foreach (var passwordBox in FindVisualChildren<PasswordBox>(this))
            NestedScrollChain.Attach(passwordBox, formScroll);
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not EntryEditorViewModel vm)
            return;

        if (e.PropertyName == nameof(EntryEditorViewModel.Password) &&
            PasswordBox.Password != vm.Password)
        {
            PasswordBox.Password = vm.Password;
        }

        if (e.PropertyName == nameof(EntryEditorViewModel.TotpSecret) &&
            TotpSecretBox.Password != vm.TotpSecret)
        {
            TotpSecretBox.Password = vm.TotpSecret;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryEditorViewModel vm &&
            vm.Password != PasswordBox.Password)
        {
            vm.Password = PasswordBox.Password;
        }
    }

    private void TotpSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryEditorViewModel vm &&
            vm.TotpSecret != TotpSecretBox.Password)
        {
            vm.TotpSecret = TotpSecretBox.Password;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is EntryEditorViewModel vm)
            vm.PropertyChanged -= ViewModel_PropertyChanged;

        base.OnClosed(e);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryEditorViewModel vm)
            return;

        if (!vm.IsValid())
        {
            if (!string.IsNullOrEmpty(vm.TitleErrorMessage))
            {
                TitleTextBox.Focus();
                return;
            }

            if (!string.IsNullOrEmpty(vm.TotpErrorMessage))
            {
                TotpSecretBox.Focus();
                return;
            }

            FocusFirstInvalidCustomField(vm);
            return;
        }

        Result = vm.ToEntry();
        DialogResult = true;
        Close();
    }

    private void FocusFirstInvalidCustomField(EntryEditorViewModel vm)
    {
        var field = vm.CustomFields.FirstOrDefault(f => !string.IsNullOrEmpty(f.ErrorMessage));
        if (field is null)
            return;

        var container = CustomFieldsItemsControl.ItemContainerGenerator.ContainerFromItem(field) as FrameworkElement;
        if (container is null)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => FocusFirstInvalidCustomField(vm));
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                var target = FindCustomFieldInput(container, field);
                if (target is null)
                    return;

                target.BringIntoView();
                target.Focus();
            });
    }

    private static Control? FindCustomFieldInput(FrameworkElement container, CustomFieldItem field)
    {
        if (string.IsNullOrWhiteSpace(field.Name))
            return FindVisualChildren<TextBox>(container).FirstOrDefault();

        if (field.IsSecret)
            return FindVisualChildren<PasswordBox>(container)
                .FirstOrDefault(passwordBox => passwordBox.Visibility == Visibility.Visible);

        return FindVisualChildren<TextBox>(container)
            .Where(textBox => textBox.Visibility == Visibility.Visible)
            .LastOrDefault();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
