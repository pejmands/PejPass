using System.Windows;

namespace PejPass.Wpf.Dialogs;

public static class DialogService
{
    private static Window? Owner =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;

    public static void Info(string message, string title = "PejPass")
    {
        Show(title, message, AppDialogType.Info, "OK");
    }

    public static void Success(string message, string title = "Success")
    {
        Show(title, message, AppDialogType.Success, "OK");
    }

    public static void Warning(string message, string title = "Warning")
    {
        Show(title, message, AppDialogType.Warning, "OK");
    }

    public static void Error(string message, string title = "Error")
    {
        Show(title, message, AppDialogType.Error, "OK");
    }

    public static bool Confirm(string message, string title = "Confirm",
        string yesText = "Yes", string noText = "Cancel")
    {
        var dlg = new AppDialog(title, message, AppDialogType.Confirm, yesText, noText)
        {
            Owner = Owner
        };
        dlg.ShowDialog();
        return dlg.Result == AppDialogResult.Primary;
    }

    /// <summary>
    /// Three-way choice. Primary / Secondary / Tertiary (usually Cancel).
    /// Closing without a button yields Tertiary.
    /// </summary>
    public static AppDialogResult Choose(
        string message,
        string title,
        string primaryText,
        string secondaryText,
        string tertiaryText = "Cancel")
    {
        var dlg = new AppDialog(title, message, AppDialogType.Confirm,
            primaryText, secondaryText, tertiaryText)
        {
            Owner = Owner
        };
        dlg.ShowDialog();
        return dlg.Result == AppDialogResult.None ? AppDialogResult.Tertiary : dlg.Result;
    }

    public static bool ConfirmDelete(string itemName)
    {
        return Confirm(
            $"Delete \"{itemName}\"?\n\nThis cannot be undone.",
            "Delete Entry",
            yesText: "Delete",
            noText: "Cancel");
    }

    private static void Show(string title, string message, AppDialogType type, string primary)
    {
        var dlg = new AppDialog(title, message, type, primary)
        {
            Owner = Owner
        };
        dlg.ShowDialog();
    }
}
