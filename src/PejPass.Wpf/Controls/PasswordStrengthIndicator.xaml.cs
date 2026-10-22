using System.Windows;
using System.Windows.Controls;

namespace PejPass.Wpf.Controls;

public partial class PasswordStrengthIndicator : UserControl
{
    public PasswordStrengthIndicator()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty StrengthLabelProperty =
        DependencyProperty.Register(
            nameof(StrengthLabel),
            typeof(string),
            typeof(PasswordStrengthIndicator));

    public string StrengthLabel
    {
        get => (string)GetValue(StrengthLabelProperty);
        set => SetValue(StrengthLabelProperty, value);
    }


    public static readonly DependencyProperty StrengthLevelProperty =
        DependencyProperty.Register(
            nameof(StrengthLevel),
            typeof(int),
            typeof(PasswordStrengthIndicator));

    public int StrengthLevel
    {
        get => (int)GetValue(StrengthLevelProperty);
        set => SetValue(StrengthLevelProperty, value);
    }


    public static readonly DependencyProperty StrengthProgressProperty =
        DependencyProperty.Register(
            nameof(StrengthProgress),
            typeof(double),
            typeof(PasswordStrengthIndicator));

    public double StrengthProgress
    {
        get => (double)GetValue(StrengthProgressProperty);
        set => SetValue(StrengthProgressProperty, value);
    }
}
