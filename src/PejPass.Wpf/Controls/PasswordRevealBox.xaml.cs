using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PejPass.Wpf.Controls;

using PejPass.Wpf.Services;

public partial class PasswordRevealBox : UserControl
{
    private readonly SecretRevealTimer _revealTimer = new();
    private static readonly Type TextRangeType =
        typeof(PasswordBox).Assembly.GetType("System.Windows.Documents.ITextRange")
        ?? throw new InvalidOperationException("ITextRange was not found.");

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

    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.Register(
            nameof(Password),
            typeof(string),
            typeof(PasswordRevealBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordChanged));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value ?? string.Empty);
    }

    public static readonly DependencyProperty ShowRevealButtonProperty =
        DependencyProperty.Register(
            nameof(ShowRevealButton),
            typeof(bool),
            typeof(PasswordRevealBox),
            new PropertyMetadata(true));

    public bool ShowRevealButton
    {
        get => (bool)GetValue(ShowRevealButtonProperty);
        set => SetValue(ShowRevealButtonProperty, value);
    }

    public static readonly DependencyProperty IsRevealedProperty =
        DependencyProperty.Register(
            nameof(IsRevealed),
            typeof(bool),
            typeof(PasswordRevealBox),
            new PropertyMetadata(false, OnIsRevealedChanged));

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public new static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(PasswordRevealBox));

    public new static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(PasswordRevealBox));

    public new static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(PasswordRevealBox));

    public new static readonly DependencyProperty BackgroundProperty =
        Control.BackgroundProperty.AddOwner(typeof(PasswordRevealBox));

    public new static readonly DependencyProperty BorderBrushProperty =
        Control.BorderBrushProperty.AddOwner(typeof(PasswordRevealBox));

    public new static readonly DependencyProperty BorderThicknessProperty =
        Control.BorderThicknessProperty.AddOwner(typeof(PasswordRevealBox));

    public new static readonly DependencyProperty PaddingProperty =
        Control.PaddingProperty.AddOwner(typeof(PasswordRevealBox));

    public event RoutedEventHandler? PasswordChanged;

    public PasswordRevealBox()
    {
        InitializeComponent();
        _revealTimer.Tick += RevealTimer_Tick;
        PasswordBox.Password = Password;
        UpdateVisibility();
    }

    public void Clear()
    {
        StopRevealTimer();
        IsRevealed = false;
        Password = string.Empty;
        TextBox.Clear();
        PasswordBox.Clear();
    }

    public bool FocusInput()
    {
        return IsRevealed
            ? TextBox.Focus()
            : PasswordBox.Focus();
    }

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordRevealBox control)
            return;

        var password = (string?)e.NewValue ?? string.Empty;

        if (!control.IsRevealed)
        {
            if (control.PasswordBox.Password != password)
                control.PasswordBox.Password = password;
        }
        else if (control.TextBox.Text != password)
        {
            control.TextBox.Text = password;
        }
    }

    private static void OnIsRevealedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordRevealBox control)
            return;

        if (!control.IsRevealed)
            control.StopRevealTimer();

        control.UpdateVisibility();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!IsRevealed && Password != PasswordBox.Password)
            Password = PasswordBox.Password;

        PasswordChanged?.Invoke(this, e);
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsRevealed && Password != TextBox.Text)
            Password = TextBox.Text;

        PasswordChanged?.Invoke(this, e);
    }

    private void VisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        var selection = IsRevealed
            ? new PasswordBoxSelection(TextBox.SelectionStart, TextBox.SelectionLength)
            : GetPasswordBoxSelection();

        IsRevealed = !IsRevealed;

        if (IsRevealed)
        {
            StartRevealTimer();
            TextBox.Focus();
            TextBox.Select(selection.Start, selection.Length);
        }
        else
        {
            PasswordBox.Focus();
            SetPasswordBoxSelection(selection.Start, selection.Length);
        }
    }

    private void StartRevealTimer()
    {
        var seconds = App.Services.GetService<PejPass.Domain.Settings.AppSettings>()?.RevealSecretSeconds ?? 10;
        _revealTimer.Start(seconds, () => IsRevealed = false);
    }

    private void StopRevealTimer()
    {
        _revealTimer.Stop();
    }

    private void UpdateVisibility()
    {
        if (!IsInitialized)
            return;

        if (IsRevealed)
        {
            TextBox.Text = Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            TextBox.Visibility = Visibility.Visible;
            VisibilityButton.ToolTip = "Hide password";
        }
        else
        {
            PasswordBox.Password = Password;
            TextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            VisibilityButton.ToolTip = "Show password";
        }
    }

    private PasswordBoxSelection GetPasswordBoxSelection()
    {
        var selection = PasswordSelectionProperty.GetValue(PasswordBox);

        if (selection is null)
            return new PasswordBoxSelection(PasswordBox.Password.Length, 0);

        var start = TextRangeStartMethod.Invoke(selection, null);
        var end = TextRangeEndMethod.Invoke(selection, null);

        var startOffset = TextPointerOffsetProperty.GetValue(start) is int startValue
            ? startValue
            : 0;
        var endOffset = TextPointerOffsetProperty.GetValue(end) is int endValue
            ? endValue
            : startOffset;

        return new PasswordBoxSelection(
            startOffset,
            Math.Max(0, endOffset - startOffset));
    }

    private void SetPasswordBoxSelection(int start, int length)
    {
        start = Math.Clamp(start, 0, PasswordBox.Password.Length);
        length = Math.Clamp(length, 0, PasswordBox.Password.Length - start);
        PasswordSelectMethod.Invoke(PasswordBox, [start, length]);
    }

    private readonly record struct PasswordBoxSelection(int Start, int Length);
}
