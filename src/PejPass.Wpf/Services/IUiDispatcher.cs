namespace PejPass.Wpf.Services;

public interface IUiDispatcher
{
    void Invoke(Action action);
}
