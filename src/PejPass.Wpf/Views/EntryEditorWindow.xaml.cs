using PejPass.Domain.Entities;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace PejPass.Wpf.Views;

public partial class EntryEditorWindow : Window
{
    public VaultEntry? Result { get; private set; }

    private readonly Func<VaultEntry, bool>? _isDuplicate;

    public EntryEditorWindow(
        EntryEditorViewModel viewModel,
        Func<VaultEntry, bool>? isDuplicate = null)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);
        App.SetCustomWindowTitle(
            this,
            viewModel.Original is null ? "Add Entry" : "Edit Entry");

        _isDuplicate = isDuplicate;

        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        PasswordBox.Password = viewModel.Password;
        TotpSecretBox.Password = viewModel.TotpSecret;

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () =>
                {
                    AttachTagCaseWarningAdorner();
                    AttachNestedScrollChains();
                    RefreshTagCaseWarnings();
                    FocusTitle();
                });
        };

        TagsTextBox.TextChanged += (_, _) => RefreshTagCaseWarnings();
        TagsTextBox.SelectionChanged += (_, _) => RefreshTagCaseWarnings();
    }

    private TagCaseWarningAdorner? _tagCaseWarningAdorner;

    private void FocusTitle()
    {
        TitleTextBox.Focus();
        TitleTextBox.CaretIndex = TitleTextBox.Text.Length;
    }

    private void AttachTagCaseWarningAdorner()
    {
        var layer = AdornerLayer.GetAdornerLayer(TagsTextBox);
        if (layer is null || _tagCaseWarningAdorner is not null)
            return;

        _tagCaseWarningAdorner = new TagCaseWarningAdorner(TagsTextBox, () =>
            DataContext is EntryEditorViewModel vm ? vm : null);

        layer.Add(_tagCaseWarningAdorner);
    }

    private void RefreshTagCaseWarnings()
    {
        _tagCaseWarningAdorner?.InvalidateVisual();
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

        if (_tagCaseWarningAdorner is not null)
        {
            AdornerLayer.GetAdornerLayer(TagsTextBox)?.Remove(_tagCaseWarningAdorner);
            _tagCaseWarningAdorner = null;
        }

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

            if (!string.IsNullOrEmpty(vm.UrlErrorMessage))
            {
                UrlTextBox.BringIntoView();
                UrlTextBox.Focus();
                return;
            }

            if (!string.IsNullOrEmpty(vm.TotpErrorMessage))
            {
                FocusTotpSecret();
                return;
            }

            FocusFirstInvalidCustomField(vm);
            return;
        }

        var entry = vm.ToEntry();

        if (_isDuplicate?.Invoke(entry) == true)
        {
            DialogService.Warning(
                "An identical entry already exists in your list.",
                "Duplicate entry");
            return;
        }

        Result = entry;
        DialogResult = true;
        Close();
    }

    private void FocusTotpSecret()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                TotpSecretBox.BringIntoView();

                var passwordBox = FindVisualChildren<PasswordBox>(TotpSecretBox).FirstOrDefault();
                passwordBox?.Focus();
            });
    }

    private void FocusFirstInvalidCustomField(EntryEditorViewModel vm)
    {
        var field = vm.CustomFields.FirstOrDefault(f => !string.IsNullOrEmpty(f.ErrorMessage));
        if (field is null)
            return;

        if (CustomFieldsItemsControl.ItemContainerGenerator.ContainerFromItem(field) is not FrameworkElement container)
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


internal sealed class TagCaseWarningAdorner : Adorner
{
    private readonly TextBox _textBox;
    private readonly Func<EntryEditorViewModel?> _getViewModel;

    public TagCaseWarningAdorner(
        TextBox textBox,
        Func<EntryEditorViewModel?> getViewModel)
        : base(textBox)
    {
        _textBox = textBox;
        _getViewModel = getViewModel;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var vm = _getViewModel();
        if (vm is null || string.IsNullOrEmpty(_textBox.Text))
            return;

        var warningBrush = _textBox.TryFindResource("WarningBrush") as Brush
                           ?? Brushes.Orange;

        var pen = new Pen(warningBrush, 1.25);
        if (pen.CanFreeze)
            pen.Freeze();

        var text = _textBox.Text;
        var segmentStart = 0;

        while (segmentStart <= text.Length)
        {
            var comma = text.IndexOf(',', segmentStart);
            var segmentEnd = comma >= 0 ? comma : text.Length;

            var rawLength = segmentEnd - segmentStart;
            var leadingWhitespace = 0;

            while (leadingWhitespace < rawLength &&
                   char.IsWhiteSpace(text[segmentStart + leadingWhitespace]))
            {
                leadingWhitespace++;
            }

            var trailingWhitespace = 0;
            while (trailingWhitespace < rawLength - leadingWhitespace &&
                   char.IsWhiteSpace(text[segmentEnd - 1 - trailingWhitespace]))
            {
                trailingWhitespace++;
            }

            var tokenStart = segmentStart + leadingWhitespace;
            var tokenLength = rawLength - leadingWhitespace - trailingWhitespace;

            if (tokenLength > 0)
            {
                var token = text.Substring(tokenStart, tokenLength);

                if (vm.HasCaseVariantInOtherEntries(token))
                    DrawSquiggle(drawingContext, pen, tokenStart, tokenLength);
            }

            if (comma < 0)
                break;

            segmentStart = comma + 1;
        }
    }

    private void DrawSquiggle(
        DrawingContext drawingContext,
        Pen pen,
        int startIndex,
        int length)
    {
        var start = _textBox.GetRectFromCharacterIndex(startIndex);
        var end = _textBox.GetRectFromCharacterIndex(startIndex + length - 1, true);

        if (start == Rect.Empty || end == Rect.Empty ||
            double.IsInfinity(start.X) || double.IsInfinity(start.Y) ||
            double.IsInfinity(end.X) || double.IsInfinity(end.Y))
            return;

        var left = Math.Max(0, start.Left);
        var right = Math.Min(_textBox.ActualWidth, end.Right);

        if (right <= left)
            return;

        var baseline = Math.Min(
            Math.Max(start.Bottom - 1, 0),
            Math.Max(_textBox.ActualHeight - 1, 0));

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            const double amplitude = 1.2;
            const double wavelength = 5;

            context.BeginFigure(new Point(left, baseline), false, false);

            var x = left;
            var up = true;

            while (x < right)
            {
                var next = Math.Min(x + wavelength / 2, right);
                var mid = (x + next) / 2;

                context.QuadraticBezierTo(
                    new Point(mid, baseline + (up ? -amplitude : amplitude)),
                    new Point(next, baseline),
                    true,
                    true);

                x = next;
                up = !up;
            }
        }

        if (geometry.CanFreeze)
            geometry.Freeze();

        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
