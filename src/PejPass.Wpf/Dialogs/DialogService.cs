using PejPass.Wpf.Services;
using System.Windows;

namespace PejPass.Wpf.Dialogs;

public static class DialogService
{
    /// <summary>
    /// Prefer an active visible window; never use a closed/hidden MainWindow leftover from Login→Main switch.
    /// </summary>
    private static readonly SemaphoreSlim AutofillConfirmationGate = new(1, 1);
    private static readonly TimeSpan AutofillConfirmationTimeout = TimeSpan.FromSeconds(60);

    private static Window? Owner
    {
        get
        {
            var app = System.Windows.Application.Current;
            if (app is null) return null;

            Window? best = null;
            foreach (Window w in app.Windows)
            {
                if (SystemTrayService.IsHostWindow(w) || !w.IsVisible || !w.IsLoaded)
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
        string yesText = "Yes", string noText = "Cancel", bool isDestructive = false,
        bool bringToFront = false)
    {
        var dlg = new AppDialog(
            title, message, AppDialogType.Confirm, yesText, noText,
            isDestructive: isDestructive)
        {
            Owner = Owner
        };
        if (bringToFront)
            dlg.Loaded += (_, _) => SingleInstance.RestoreAndFocus(dlg);

        dlg.ShowDialog();
        return dlg.Result == AppDialogResult.Primary;
    }

    public static async Task<bool> ConfirmAsync(
        string message,
        string title = "Confirm",
        string yesText = "Yes",
        string noText = "Cancel",
        bool isDestructive = false,
        bool bringToFront = false,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AutofillConfirmationTimeout);

        try
        {
            await AutofillConfirmationGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            timeout.Token.ThrowIfCancellationRequested();

            var dlg = new AppDialog(
                title, message, AppDialogType.Confirm, yesText, noText,
                isDestructive: isDestructive)
            {
                ShowInTaskbar = true,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                UsesModalResult = false
            };

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            dlg.Closed += (_, _) =>
                completion.TrySetResult(dlg.Result == AppDialogResult.Primary);

            if (bringToFront)
                dlg.Loaded += (_, _) => SingleInstance.RestoreAndFocus(dlg, preserveTopmost: true);

            dlg.Show();

            using var registration = timeout.Token.Register(() =>
            {
                try
                {
                    dlg.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (dlg.IsVisible)
                            dlg.Close();
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });

            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            AutofillConfirmationGate.Release();
        }
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
        string tertiaryText = "Cancel",
        bool isDestructive = false)
    {
        var dlg = new AppDialog(title, message, AppDialogType.Confirm,
            primaryText, secondaryText, tertiaryText, isDestructive)
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
            noText: "Cancel",
            isDestructive: true);
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
