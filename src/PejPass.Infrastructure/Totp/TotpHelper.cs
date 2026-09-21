using OtpNet;

namespace PejPass.Infrastructure.Totp;

/// <summary>
/// Thin wrapper around Otp.NET for TOTP generation (30s step, 6 digits, SHA1).
/// </summary>
public static class TotpHelper
{
    public const int StepSeconds = 30;

    public static bool TryGetCode(string? base32Secret, out string code, out int remainingSeconds)
    {
        code = string.Empty;
        remainingSeconds = 0;

        if (string.IsNullOrWhiteSpace(base32Secret))
            return false;

        try
        {
            // Allow spaces and lowercase; strip common noise
            var cleaned = base32Secret
                .Replace(" ", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Trim()
                .ToUpperInvariant();

            var key = Base32Encoding.ToBytes(cleaned);
            var totp = new OtpNet.Totp(key, step: StepSeconds, totpSize: 6);

            code = totp.ComputeTotp();
            remainingSeconds = totp.RemainingSeconds();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidSecret(string? base32Secret)
    {
        return TryGetCode(base32Secret, out _, out _);
    }
}
