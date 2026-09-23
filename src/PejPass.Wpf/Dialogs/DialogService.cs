using System.Windows;

namespace PejPass.Wpf.Dialogs;

public static class DialogService
{
    /// <summary>
    /// Prefer an active visible window; never use a closed/hidden MainWindow leftover from Login→Main switch.
    /// </summary>
    private static Window? Owner
    {
        get
        {
            var app = System.Windows.Application.Current;
            if (app is null) return null;

            Window? best = null;
            foreach (Window w in app.Windows)
            {
                if (!w.IsVisible || !w.IsLoaded)
                    continue;
                if (w.IsActive)
                    return w;
                best ??= w;
            }

            if (best is not null)
                return best;

            if (app.MainWindow is { IsLoaded: true, IsVisible: true } main)
                return main;

            return null;
        }
    }

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
        // Center on screen if no owner (e.g. activation from second instance)
        if (dlg.Owner is null)
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }
}
