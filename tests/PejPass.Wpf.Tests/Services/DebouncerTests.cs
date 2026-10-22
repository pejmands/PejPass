using PejPass.Wpf.Services;
using System.Windows.Threading;

namespace PejPass.Wpf.Tests.Services;

public sealed class DebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    [Fact(Timeout = 15000)]
    public void Trigger_ManyTimesInARow_RunsActionOnce()
    {
        WpfTestHost.Run(_ =>
        {
            var calls = 0;
            using var debouncer = new Debouncer(Delay, () => calls++);

            for (var i = 0; i < 100; i++)
                debouncer.Trigger();

            Assert.Equal(0, calls);

            PumpFor(Settle);

            Assert.Equal(1, calls);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Trigger_AfterActionRan_RunsAgain()
    {
        WpfTestHost.Run(_ =>
        {
            var calls = 0;
            using var debouncer = new Debouncer(Delay, () => calls++);

            debouncer.Trigger();
            PumpFor(Settle);
            debouncer.Trigger();
            PumpFor(Settle);

            Assert.Equal(2, calls);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Cancel_DropsPendingInvocation()
    {
        WpfTestHost.Run(_ =>
        {
            var calls = 0;
            using var debouncer = new Debouncer(Delay, () => calls++);

            debouncer.Trigger();
            debouncer.Cancel();
            PumpFor(Settle);

            Assert.Equal(0, calls);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void Dispose_DropsPendingInvocationAndIgnoresLaterTriggers()
    {
        WpfTestHost.Run(_ =>
        {
            var calls = 0;
            var debouncer = new Debouncer(Delay, () => calls++);

            debouncer.Trigger();
            debouncer.Dispose();
            debouncer.Trigger();
            PumpFor(Settle);

            Assert.Equal(0, calls);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs the dispatcher message loop for a while so timers can fire.</summary>
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
