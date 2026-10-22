using System.Windows;
using System.Windows.Interop;
using Windows.Security.Credentials.UI;

namespace PejPass.Wpf.Services;

public enum WindowsHelloVerificationResult
{
    Verified,
    AuthenticationFailed,
    Cancelled,
    Unavailable,
    Error
}

/// <summary>
/// Thin wrapper around UserConsentVerifier (same pattern as pejmands/Authenticator).
/// </summary>
public static class WindowsHelloHelper
{
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            return availability == UserConsentVerifierAvailability.Available;
        }
        catch { return false; }
    }

    /// <summary>
    /// Prompts Windows Hello tied to the owner window and classifies the outcome.
    /// Only RetriesExhausted is treated as an authentication failure.
    /// </summary>
    public static async Task<WindowsHelloVerificationResult> VerifyAsync(
        Window owner,
        string message = "Unlock PejPass")
    {
        try
        {
            var hwnd = new WindowInteropHelper(owner).EnsureHandle();
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, message);
            return result switch
            {
                UserConsentVerificationResult.Verified => WindowsHelloVerificationResult.Verified,
                UserConsentVerificationResult.RetriesExhausted => WindowsHelloVerificationResult.AuthenticationFailed,
                UserConsentVerificationResult.Canceled => WindowsHelloVerificationResult.Cancelled,
                _ => WindowsHelloVerificationResult.Unavailable
            };
        }
        catch { return WindowsHelloVerificationResult.Error; }
    }
}
