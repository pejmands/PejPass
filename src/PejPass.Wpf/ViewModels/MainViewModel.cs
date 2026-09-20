using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isVaultOpen;

    [RelayCommand]
    private void LockVault()
    {
        IsVaultOpen = false;
        StatusMessage = "Vault locked.";
    }
}
