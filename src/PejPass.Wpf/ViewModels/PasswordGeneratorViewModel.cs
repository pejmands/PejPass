using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PejPass.Domain.Security;

namespace PejPass.Wpf.ViewModels;

public partial class PasswordGeneratorViewModel : ObservableObject
{
    [ObservableProperty] private int _length = 20;
    [ObservableProperty] private bool _includeLowercase = true;
    [ObservableProperty] private bool _includeUppercase = true;
    [ObservableProperty] private bool _includeDigits = true;
    [ObservableProperty] private bool _includeSymbols = true;
    [ObservableProperty] private bool _excludeAmbiguous = true;
    [ObservableProperty] private string _preview = string.Empty;
    [ObservableProperty] private string? _error;

    public string? Result { get; private set; }

    public event EventHandler? RequestAccept;
    public event EventHandler? RequestCancel;

    public PasswordGeneratorViewModel()
    {
        Regenerate();
    }

    partial void OnLengthChanged(int value) => Regenerate();
    partial void OnIncludeLowercaseChanged(bool value) => Regenerate();
    partial void OnIncludeUppercaseChanged(bool value) => Regenerate();
    partial void OnIncludeDigitsChanged(bool value) => Regenerate();
    partial void OnIncludeSymbolsChanged(bool value) => Regenerate();
    partial void OnExcludeAmbiguousChanged(bool value) => Regenerate();

    [RelayCommand]
    private void Regenerate()
    {
        Error = null;
        try
        {
            Preview = PasswordGenerator.Generate(new PasswordGeneratorOptions
            {
                Length = Length,
                IncludeLowercase = IncludeLowercase,
                IncludeUppercase = IncludeUppercase,
                IncludeDigits = IncludeDigits,
                IncludeSymbols = IncludeSymbols,
                ExcludeAmbiguous = ExcludeAmbiguous
            });
        }
        catch (Exception ex)
        {
            Preview = string.Empty;
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void Use()
    {
        if (string.IsNullOrEmpty(Preview))
        {
            Error = "Generate a password first.";
            return;
        }

        Result = Preview;
        RequestAccept?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => RequestCancel?.Invoke(this, EventArgs.Empty);
}
