using PejPass.Wpf.ViewModels;
using System.Reflection;
using System.Timers;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class MainViewModelAutoLockTimerTests
{
    [Fact(Timeout = 15000)]
    public void RestartingAutoLockTimer_DisposesPreviousTimer_AndDisableClearsCurrentTimer()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.AutoLockMinutes = 1;
            var viewModel = host.CreateMainViewModel();
            var timerField = GetTimerField();

            try
            {
                var firstTimer = Assert.IsType<Timer>(timerField.GetValue(viewModel));

                InvokeStartAutoLockTimer(viewModel);

                var secondTimer = Assert.IsType<Timer>(timerField.GetValue(viewModel));
                Assert.NotSame(firstTimer, secondTimer);
                Assert.True(secondTimer.Enabled);
                Assert.Throws<ObjectDisposedException>(() => firstTimer.Start());

                host.Settings.AutoLockMinutes = 0;
                InvokeStartAutoLockTimer(viewModel);

                Assert.Null(timerField.GetValue(viewModel));
                Assert.Throws<ObjectDisposedException>(() => secondTimer.Start());
            }
            finally
            {
                viewModel.StopBackgroundTimers();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void StopBackgroundTimers_DisposesAutoLockTimer()
    {
        WpfTestHost.Run(host =>
        {
            host.Settings.AutoLockMinutes = 1;
            var viewModel = host.CreateMainViewModel();
            var timerField = GetTimerField();

            var timer = Assert.IsType<Timer>(timerField.GetValue(viewModel));

            try
            {
                viewModel.StopBackgroundTimers();

                Assert.Null(timerField.GetValue(viewModel));
                Assert.Throws<ObjectDisposedException>(() => timer.Start());
            }
            finally
            {
                viewModel.StopBackgroundTimers();
            }
        }, TestContext.Current.CancellationToken);
    }

    private static FieldInfo GetTimerField()
    {
        var field = typeof(MainViewModel).GetField(
            "_autoLockTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return field!;
    }

    private static void InvokeStartAutoLockTimer(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod(
            "StartAutoLockTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(viewModel, null);
    }
}
