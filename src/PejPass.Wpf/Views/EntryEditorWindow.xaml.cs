using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Views;

public partial class EntryEditorWindow : Window
{
    private static readonly PropertyInfo PasswordSelectionProperty =
        typeof(PasswordBox).GetProperty(
            "Selection",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PasswordBox.Selection was not found.");

    private static readonly MethodInfo PasswordSelectMethod =
        typeof(PasswordBox).GetMethod(
            "Select",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PasswordBox.Select was not found.");

    private static readonly Type TextRangeType =
        typeof(PasswordBox).Assembly.GetType("System.Windows.Documents.ITextRange")
        ?? throw new InvalidOperationException("ITextRange was not found.");

    private static readonly MethodInfo TextRangeStartMethod =
        TextRangeType.GetProperty("Start")?.GetGetMethod()
        ?? throw new InvalidOperationException("ITextRange.Start was not found.");

    private static readonly MethodInfo TextRangeEndMethod =
        TextRangeType.GetProperty("End")?.GetGetMethod()
        ?? throw new InvalidOperationException("ITextRange.End was not found.");

    private static readonly Type PasswordTextPointerType =
        typeof(PasswordBox).Assembly.GetType("System.Windows.Controls.PasswordTextPointer")
        ?? throw new InvalidOperationException("PasswordTextPointer was not found.");

    private static readonly PropertyInfo TextPointerOffsetProperty =
        PasswordTextPointerType.GetProperty(
            "Offset",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PasswordTextPointer.Offset was not found.");

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
            Dispatcher.BeginInvoke(AttachNotesScrollChain, DispatcherPriority.Loaded);
    }

    private void AttachNotesScrollChain()
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

    private void TotpSecretVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryEditorViewModel vm)
            return;

        if (TotpSecretBox.Visibility == Visibility.Visible)
        {
            var selection = GetPasswordBoxSelection(TotpSecretBox);

            TotpSecretTextBox.Text = vm.TotpSecret;
            TotpSecretBox.Visibility = Visibility.Collapsed;
            TotpSecretTextBox.Visibility = Visibility.Visible;

            TotpSecretTextBox.Focus();
            TotpSecretTextBox.Select(selection.Start, selection.Length);
            TotpSecretVisibilityButton.ToolTip = "Hide TOTP secret";
        }
        else
        {
            var selectionStart = TotpSecretTextBox.SelectionStart;
            var selectionLength = TotpSecretTextBox.SelectionLength;

            TotpSecretBox.Password = vm.TotpSecret;
            TotpSecretTextBox.Visibility = Visibility.Collapsed;
            TotpSecretBox.Visibility = Visibility.Visible;

            TotpSecretBox.Focus();
            SetPasswordBoxSelection(TotpSecretBox, selectionStart, selectionLength);
            TotpSecretVisibilityButton.ToolTip = "Show TOTP secret";
        }
    }

    private void PasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryEditorViewModel vm)
            return;

        if (PasswordBox.Visibility == Visibility.Visible)
        {
            var selection = GetPasswordBoxSelection();

            PasswordTextBox.Text = vm.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;

            PasswordTextBox.Focus();
            PasswordTextBox.Select(selection.Start, selection.Length);
            PasswordVisibilityButton.ToolTip = "Hide password";
        }
        else
        {
            var selectionStart = PasswordTextBox.SelectionStart;
            var selectionLength = PasswordTextBox.SelectionLength;

            PasswordBox.Password = vm.Password;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;

            PasswordBox.Focus();
            SetPasswordBoxSelection(selectionStart, selectionLength);
            PasswordVisibilityButton.ToolTip = "Show password";
        }
    }

    private PasswordBoxSelection GetPasswordBoxSelection()
    {
        return GetPasswordBoxSelection(PasswordBox);
    }

    private PasswordBoxSelection GetPasswordBoxSelection(PasswordBox passwordBox)
    {
        var selection = PasswordSelectionProperty.GetValue(passwordBox);

        if (selection is null)
            return new PasswordBoxSelection(0, 0);

        var start = TextRangeStartMethod.Invoke(selection, null);
        var end = TextRangeEndMethod.Invoke(selection, null);

        var startOffset = (int?)(TextPointerOffsetProperty.GetValue(start) as int?) ?? 0;
        var endOffset = (int?)(TextPointerOffsetProperty.GetValue(end) as int?) ?? startOffset;

        return new PasswordBoxSelection(
            startOffset,
            Math.Max(0, endOffset - startOffset));
    }

    private void SetPasswordBoxSelection(int start, int length)
    {
        SetPasswordBoxSelection(PasswordBox, start, length);
    }

    private void SetPasswordBoxSelection(PasswordBox passwordBox, int start, int length)
    {
        start = Math.Clamp(start, 0, passwordBox.Password.Length);
        length = Math.Clamp(length, 0, passwordBox.Password.Length - start);
        PasswordSelectMethod.Invoke(passwordBox, [start, length]);
    }

    private readonly record struct PasswordBoxSelection(int Start, int Length);

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
