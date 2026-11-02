using PejPass.Wpf.Dialogs;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PejPass.Wpf.Tests.Dialogs;

public sealed class AppDialogBehaviorTests
{
    [Theory]
    [InlineData(AppDialogType.Info, "AccentBrush")]
    [InlineData(AppDialogType.Success, "SuccessBrush")]
    [InlineData(AppDialogType.Warning, "WarningBrush")]
    [InlineData(AppDialogType.Error, "DangerBrush")]
    [InlineData(AppDialogType.Confirm, "AccentBrush")]
    public void DialogType_ConfiguresItsSemanticIcon(
        AppDialogType type,
        string expectedBrushKey)
    {
        WpfTestHost.Run(_ =>
        {
            var dialog = new AppDialog("Dialog", "Message", type);
            var icon = GetTypeIcon(dialog);
            var expectedBrush = Assert.IsAssignableFrom<Brush>(
                Application.Current!.FindResource(expectedBrushKey));

            Assert.NotNull(icon.Data);
            Assert.Equal(2.2, icon.StrokeThickness);
            Assert.Same(expectedBrush, icon.Stroke);

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void DialogTypes_UseDistinctIconGeometries()
    {
        WpfTestHost.Run(_ =>
        {
            var geometries = Enum.GetValues<AppDialogType>()
                .Select(type =>
                {
                    var dialog = new AppDialog("Dialog", "Message", type);
                    var geometry = GetTypeIcon(dialog).Data!.ToString();
                    dialog.Close();
                    return geometry;
                })
                .ToArray();

            Assert.Equal(
                geometries.Length,
                geometries.Distinct(StringComparer.Ordinal).Count());
        }, TestContext.Current.CancellationToken);
    }

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
    public void DestructiveChoiceDialog_EnterDefaultsToCancelTertiary()
    {
        WpfTestHost.Run(_ =>
        {
            var dialog = new AppDialog(
                "Restore mode",
                "Choose how to restore.",
                AppDialogType.Confirm,
                "Replace",
                "Merge",
                "Cancel",
                isDestructive: true);

            var primary = GetButton(dialog, "PrimaryButton");
            var secondary = GetButton(dialog, "SecondaryButton");
            var tertiary = GetButton(dialog, "TertiaryButton");

            Assert.False(primary.IsDefault);
            Assert.False(secondary.IsDefault);
            Assert.True(tertiary.IsDefault);

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

    private static Path GetTypeIcon(AppDialog dialog)
    {
        return Assert.IsType<Path>(dialog.FindName("TypeIcon"));
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
