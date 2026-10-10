using Microsoft.Extensions.DependencyInjection;
using PejPass.Wpf.Services;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace PejPass.Wpf.Controls;

public partial class PasswordRevealBox : UserControl
{
    private readonly SecretRevealTimer _revealTimer = new();
    private bool _keyboardActivation;

    private static readonly PasswordBoxReflection? Reflection = TryCreateReflection();

    private static PasswordBoxReflection? TryCreateReflection()
    {
        try
        {
            var assembly = typeof(PasswordBox).Assembly;
            var textRangeType = assembly.GetType("System.Windows.Documents.ITextRange");
            var passwordTextPointerType = assembly.GetType("System.Windows.Controls.PasswordTextPointer");

            if (textRangeType is null || passwordTextPointerType is null)
                return null;

            var selectionProperty = typeof(PasswordBox).GetProperty(
                "Selection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var selectMethod = typeof(PasswordBox).GetMethod(
                "Select",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var startMethod = textRangeType.GetProperty("Start")?.GetGetMethod();
            var endMethod = textRangeType.GetProperty("End")?.GetGetMethod();
            var offsetProperty = passwordTextPointerType.GetProperty(
                "Offset",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (selectionProperty is null ||
                selectMethod is null ||
                startMethod is null ||
                endMethod is null ||
                offsetProperty is null)
            {
                return null;
            }

            return new PasswordBoxReflection(
                selectionProperty,
                selectMethod,
                startMethod,
                endMethod,
                offsetProperty);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record PasswordBoxReflection(
        PropertyInfo SelectionProperty,
        MethodInfo SelectMethod,
        MethodInfo StartMethod,
        MethodInfo EndMethod,
        PropertyInfo OffsetProperty);

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
        var keepKeyboardFocus = _keyboardActivation;
        _keyboardActivation = false;

        var selection = IsRevealed
            ? new PasswordBoxSelection(TextBox.SelectionStart, TextBox.SelectionLength)
            : GetPasswordBoxSelection();

        IsRevealed = !IsRevealed;

        if (IsRevealed)
        {
            StartRevealTimer();

            if (!keepKeyboardFocus)
                TextBox.Focus();

            TextBox.Select(selection.Start, selection.Length);
        }
        else
        {
            if (!keepKeyboardFocus)
                PasswordBox.Focus();

            SetPasswordBoxSelection(selection.Start, selection.Length);
        }
    }

    private void VisibilityButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
            _keyboardActivation = true;
    }

    private void VisibilityButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _keyboardActivation = false;
    }

    private void StartRevealTimer()
    {
        var seconds = App.Services.GetService<Domain.Settings.AppSettings>()?.RevealSecretSeconds ?? 10;
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
            AutomationProperties.SetName(VisibilityButton, "Hide password");
        }
        else
        {
            PasswordBox.Password = Password;
            TextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            VisibilityButton.ToolTip = "Show password";
            AutomationProperties.SetName(VisibilityButton, "Show password");
        }

        AutomationProperties.SetHelpText(
            VisibilityButton,
            IsRevealed ? "Password is visible." : "Password is hidden.");
    }

    private PasswordBoxSelection GetPasswordBoxSelection()
    {
        var fallback = new PasswordBoxSelection(PasswordBox.Password.Length, 0);

        try
        {
            if (Reflection is not { } reflection)
                return fallback;

            var selection = reflection.SelectionProperty.GetValue(PasswordBox);
            if (selection is null)
                return fallback;

            var start = reflection.StartMethod.Invoke(selection, null);
            var end = reflection.EndMethod.Invoke(selection, null);

            if (start is null || end is null ||
                reflection.OffsetProperty.GetValue(start) is not int startOffset ||
                reflection.OffsetProperty.GetValue(end) is not int endOffset)
            {
                return fallback;
            }

            startOffset = Math.Clamp(startOffset, 0, PasswordBox.Password.Length);
            endOffset = Math.Clamp(endOffset, startOffset, PasswordBox.Password.Length);

            return new PasswordBoxSelection(
                startOffset,
                endOffset - startOffset);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void SetPasswordBoxSelection(int start, int length)
    {
        start = Math.Clamp(start, 0, PasswordBox.Password.Length);
        length = Math.Clamp(length, 0, PasswordBox.Password.Length - start);

        try
        {
            if (Reflection is { } reflection)
                reflection.SelectMethod.Invoke(PasswordBox, [start, length]);
        }
        catch (Exception)
        {
            PasswordBox.Focus();
        }
    }

    private readonly record struct PasswordBoxSelection(int Start, int Length);
}
