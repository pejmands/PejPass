using PejPass.Wpf.Dialogs;
using System.Reflection;
using System.Windows.Controls;

namespace PejPass.Wpf.Tests.Dialogs;

public sealed class AppDialogBehaviorTests
{
    [Fact(Timeout = 15000)]
    public void DestructiveDialog_EnterDefaultsToCancel()
    {
        WpfTestHost.Run(_ =>
        {
            var dialog = new AppDialog(
                "Delete permanently",
                "This cannot be undone.",
                AppDialogType.Confirm,
                "Delete permanently",
                "Cancel",
                isDestructive: true);

            var primary = GetButton(dialog, "PrimaryButton");
            var secondary = GetButton(dialog, "SecondaryButton");

            Assert.False(primary.IsDefault);
            Assert.True(secondary.IsDefault);

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void OrdinaryDialog_EnterStillDefaultsToPrimaryAction()
    {
        WpfTestHost.Run(_ =>
        {
            var dialog = new AppDialog(
                "Confirm",
                "Continue?",
                AppDialogType.Confirm,
                "Yes",
                "Cancel");

            var primary = GetButton(dialog, "PrimaryButton");
            var secondary = GetButton(dialog, "SecondaryButton");

            Assert.True(primary.IsDefault);
            Assert.False(secondary.IsDefault);

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static Button GetButton(AppDialog dialog, string name)
    {
        var field = typeof(AppDialog).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(field);
        return Assert.IsType<Button>(field!.GetValue(dialog));
    }
}
