using OtpNet;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PejPass.Infrastructure.Totp;

public enum TotpAlgorithm
{
    Sha1,
    Sha256,
    Sha512
}

/// <summary>Validated TOTP settings. Secret is normalized Base32.</summary>
public sealed record TotpParameters(
    string Secret,
    TotpAlgorithm Algorithm = TotpAlgorithm.Sha1,
    int Digits = TotpHelper.DefaultDigits,
    int PeriodSeconds = TotpHelper.StepSeconds)
{
    public bool IsDefault =>
        Algorithm == TotpAlgorithm.Sha1 &&
        Digits == TotpHelper.DefaultDigits &&
        PeriodSeconds == TotpHelper.StepSeconds;

    /// <summary>
    /// Store default settings as a bare Base32 secret for backward compatibility.
    /// Store non-default settings as a minimal otpauth URI in the existing field.
    /// </summary>
    public string ToStorageString()
    {
        if (IsDefault)
            return Secret;

        var builder = new StringBuilder("otpauth://totp/PejPass?secret=").Append(Secret);

        if (Algorithm != TotpAlgorithm.Sha1)
            builder.Append("&algorithm=").Append(ToAlgorithmName(Algorithm));

        if (Digits != TotpHelper.DefaultDigits)
            builder.Append("&digits=").Append(Digits.ToString(CultureInfo.InvariantCulture));

        if (PeriodSeconds != TotpHelper.StepSeconds)
            builder.Append("&period=").Append(PeriodSeconds.ToString(CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private static string ToAlgorithmName(TotpAlgorithm algorithm) => algorithm switch
    {
        TotpAlgorithm.Sha1 => "SHA1",
        TotpAlgorithm.Sha256 => "SHA256",
        TotpAlgorithm.Sha512 => "SHA512",
        _ => throw new InvalidOperationException("Unsupported TOTP algorithm.")
    };
}

/// <summary>
/// Wrapper around Otp.NET. Accepts a Base32 secret or an otpauth://totp URI
/// (SHA1/SHA256/SHA512, 6-8 digits, 1-300 second period).
/// </summary>
public static class TotpHelper
{
    public const int StepSeconds = 30;
    public const int DefaultDigits = 6;

    private const int MaxInputLength = 2048;
    private const int MinDigits = 6;
    private const int MaxDigits = 8;
    private const int MinPeriod = 1;
    private const int MaxPeriod = 300;

    public static bool TryParse(string? input, out TotpParameters? parameters, out string? error)
    {
        parameters = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "The TOTP secret is empty.";
            return false;
        }

        var text = input.Trim();
        if (text.Length > MaxInputLength)
        {
            error = "The TOTP secret is too long.";
            return false;
        }

        var algorithm = TotpAlgorithm.Sha1;
        var digits = DefaultDigits;
        var period = StepSeconds;
        string rawSecret;

        if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.Port != -1 ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "Invalid TOTP URI. Only otpauth://totp URIs are supported.";
                return false;
            }

            if (!TryParseQuery(uri.Query, out var query))
            {
                error = "The TOTP URI has an invalid or duplicated query parameter.";
                return false;
            }

            if (!query.TryGetValue("secret", out var secretValue) ||
                string.IsNullOrWhiteSpace(secretValue))
            {
                error = "The otpauth URI has no secret parameter.";
                return false;
            }

            rawSecret = secretValue;

            if (query.TryGetValue("encoder", out var encoder))
            {
                error = $"Unsupported code format '{encoder}' (for example Steam Guard).";
                return false;
            }

            if (query.TryGetValue("algorithm", out var algorithmValue))
            {
                switch (algorithmValue.Trim().ToUpperInvariant())
                {
                    case "SHA1": algorithm = TotpAlgorithm.Sha1; break;
                    case "SHA256": algorithm = TotpAlgorithm.Sha256; break;
                    case "SHA512": algorithm = TotpAlgorithm.Sha512; break;
                    default:
                        error = $"Unsupported TOTP algorithm '{algorithmValue}'.";
                        return false;
                }
            }

            if (query.TryGetValue("digits", out var digitsValue) &&
                !TryParseInRange(digitsValue, MinDigits, MaxDigits, out digits))
            {
                error = $"Unsupported number of digits '{digitsValue}' (use {MinDigits}-{MaxDigits}).";
                return false;
            }

            if (query.TryGetValue("period", out var periodValue) &&
                !TryParseInRange(periodValue, MinPeriod, MaxPeriod, out period))
            {
                error = $"Unsupported period '{periodValue}' (use {MinPeriod}-{MaxPeriod} seconds).";
                return false;
            }
        }
        else
        {
            rawSecret = text;
        }

        var secret = CleanSecret(rawSecret);
        if (!TryDecodeKey(secret, out var key))
        {
            error = "The TOTP secret is not valid Base32.";
            return false;
        }

        CryptographicOperations.ZeroMemory(key);
        parameters = new TotpParameters(secret, algorithm, digits, period);
        return true;
    }

    public static bool TryGetCode(
        string? secretOrUri,
        out string code,
        out int remainingSeconds) =>
        TryGetCode(secretOrUri, DateTime.UtcNow, out code, out remainingSeconds, out _);

    public static bool TryGetCode(
        string? secretOrUri,
        DateTime utcNow,
        out string code,
        out int remainingSeconds,
        out int periodSeconds)
    {
        code = string.Empty;
        remainingSeconds = 0;
        periodSeconds = StepSeconds;

        if (!TryParse(secretOrUri, out var parameters, out _) || parameters is null)
            return false;

        periodSeconds = parameters.PeriodSeconds;
        byte[]? key = null;

        try
        {
            key = Base32Encoding.ToBytes(parameters.Secret);

            var totp = new OtpNet.Totp(
                key,
                step: parameters.PeriodSeconds,
                mode: ToMode(parameters.Algorithm),
                totpSize: parameters.Digits);

            code = totp.ComputeTotp(utcNow);
            remainingSeconds = totp.RemainingSeconds(utcNow);
            return true;
        }
        catch
        {
            code = string.Empty;
            remainingSeconds = 0;
            return false;
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public static bool IsValidSecret(string? secretOrUri) =>
        TryParse(secretOrUri, out _, out _);

    /// <summary>"123456" becomes "123 456"; "12345678" becomes "1234 5678".</summary>
    public static string FormatForDisplay(string code) => code.Length switch
    {
        6 or 7 => $"{code[..3]} {code[3..]}",
        8 => $"{code[..4]} {code[4..]}",
        _ => code
    };

    private static OtpHashMode ToMode(TotpAlgorithm algorithm) => algorithm switch
    {
        TotpAlgorithm.Sha1 => OtpHashMode.Sha1,
        TotpAlgorithm.Sha256 => OtpHashMode.Sha256,
        TotpAlgorithm.Sha512 => OtpHashMode.Sha512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    };

    private static string CleanSecret(string raw) =>
        raw.Replace(" ", "", StringComparison.Ordinal)
           .Replace("-", "", StringComparison.Ordinal)
           .Trim()
           .ToUpperInvariant();

    private static bool TryDecodeKey(string secret, out byte[] key)
    {
        try
        {
            key = Base32Encoding.ToBytes(secret);
            return key.Length > 0;
        }
        catch
        {
            key = [];
            return false;
        }
    }

    private static bool TryParseInRange(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
        value >= min &&
        value <= max;

    private static bool TryParseQuery(string query, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = part.IndexOf('=');
                if (equals <= 0)
                    return false;

                var key = Uri.UnescapeDataString(part[..equals]);
                var value = Uri.UnescapeDataString(part[(equals + 1)..]);

                if (string.IsNullOrWhiteSpace(key) || !values.TryAdd(key, value))
                    return false;
            }

            return true;
        }
        catch (UriFormatException)
        {
            values.Clear();
            return false;
        }
    }
}
