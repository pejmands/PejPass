using System.Windows;
using System.Windows.Interop;
using Windows.Security.Credentials.UI;

namespace PejPass.Wpf.Services;

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
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Prompts Windows Hello tied to the owner window. True if verified.
    /// </summary>
    public static async Task<bool> VerifyAsync(Window owner, string message = "Unlock PejPass")
    {
        try
        {
            var hwnd = new WindowInteropHelper(owner).EnsureHandle();
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, message);
            return result == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
        }
    }
}
