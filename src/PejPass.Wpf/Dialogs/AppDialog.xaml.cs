using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PejPass.Wpf.Dialogs;

public partial class AppDialog : Window
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private void EnsureTopmost()
    {
        if (!Topmost || !IsVisible)
            return;

        // Reassert both WPF's Topmost state and the native topmost z-order.
        Topmost = false;
        Topmost = true;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(
                hwnd,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpShowWindow);
        }
    }
    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    public bool UsesModalResult { get; set; } = true;

    private void ConfigureTypeIcon(AppDialogType type)
    {
        var (geometry, brushKey) = type switch
        {
            AppDialogType.Info => (
                "M16,2 C23.732,2 30,8.268 30,16 C30,23.732 23.732,30 16,30 C8.268,30 2,23.732 2,16 C2,8.268 8.268,2 16,2 Z M16,13 L16,22 M16,8 L16,8.1",
                "AccentBrush"),
            AppDialogType.Success => (
                "M16,2 C23.732,2 30,8.268 30,16 C30,23.732 23.732,30 16,30 C8.268,30 2,23.732 2,16 C2,8.268 8.268,2 16,2 Z M9,16 L14,21 L23,11",
                "SuccessBrush"),
            AppDialogType.Warning => (
                "M16,3 L30,28 L2,28 Z M16,11 L16,19 M16,23 L16,23.1",
                "WarningBrush"),
            AppDialogType.Error => (
                "M16,2 C23.732,2 30,8.268 30,16 C30,23.732 23.732,30 16,30 C8.268,30 2,23.732 2,16 C2,8.268 8.268,2 16,2 Z M11,11 L21,21 M21,11 L11,21",
                "DangerBrush"),
            AppDialogType.Confirm => (
                "M16,2 C23.732,2 30,8.268 30,16 C30,23.732 23.732,30 16,30 C8.268,30 2,23.732 2,16 C2,8.268 8.268,2 16,2 Z M11,11 C11,7 15,5.5 18.5,7 C22.5,9 21.5,13 18.5,15 C16.5,16 16,17 16,19 M16,23 L16,23.1",
                "AccentBrush"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported dialog type.")
        };

        TypeIcon.Data = Geometry.Parse(geometry);
        TypeIcon.SetResourceReference(Shape.StrokeProperty, brushKey);
    }

    public AppDialog(string title, string message, AppDialogType type,
        string primaryText = "OK", string? secondaryText = null, string? tertiaryText = null,
        bool isDestructive = false)
    {
        InitializeComponent();
        ConfigureTypeIcon(type);
        App.PrepareCustomChrome(this);
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => EnsureTopmost();
        Loaded += (_, _) =>
        {
            EnsureTopmost();
            Activate();
        };
        Activated += (_, _) => EnsureTopmost();

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (string.IsNullOrEmpty(secondaryText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        if (string.IsNullOrEmpty(tertiaryText))
        {
            TertiaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            TertiaryButton.Content = tertiaryText;
            TertiaryButton.Visibility = Visibility.Visible;
            // Escape cancels when a tertiary (usually Cancel) is present
            TertiaryButton.IsCancel = true;
        }

        if (isDestructive)
        {
            PrimaryButton.Style = (Style)FindResource("DestructiveButton");
            PrimaryButton.IsDefault = false;

            if (TertiaryButton.Visibility == Visibility.Visible)
                TertiaryButton.IsDefault = true;
            else if (SecondaryButton.Visibility == Visibility.Visible)
                SecondaryButton.IsDefault = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (TertiaryButton.Visibility == Visibility.Visible)
        {
            Tertiary_Click(TertiaryButton, new RoutedEventArgs());
        }
        else
        {
            Result = AppDialogResult.None;
            if (UsesModalResult)
                DialogResult = false;
            Close();
        }

        e.Handled = true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Primary;
        if (UsesModalResult)
            DialogResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Secondary;
        if (UsesModalResult)
            DialogResult = false;
        Close();
    }

    private void Tertiary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Tertiary;
        if (UsesModalResult)
            DialogResult = false;
        Close();
    }
}
