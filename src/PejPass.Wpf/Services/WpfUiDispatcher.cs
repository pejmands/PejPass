using System.Windows.Threading;

namespace PejPass.Wpf.Services;

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = dispatcher;

    public void Invoke(Action action)
    {
        _dispatcher.Invoke(action);
    }
}
