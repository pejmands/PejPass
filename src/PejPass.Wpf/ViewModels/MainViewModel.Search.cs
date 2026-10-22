using CommunityToolkit.Mvvm.Input;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;
}
