using System.Text;
using System.Text.RegularExpressions;

namespace PejPass.Domain.Policies;

/// <summary>
/// Strict master-password policy (~75% strictness).
/// </summary>
public static partial class MasterPasswordPolicy
{
    [GeneratedRegex("[A-Z]")]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex("[a-z]")]
    private static partial Regex LowercaseRegex();

    [GeneratedRegex("[0-9]")]
    private static partial Regex DigitRegex();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex SpecialCharacterRegex();

    public const int MinimumLength = 12;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456", "123456789", "12345678", "1234567890", "password", "password1",
        "password123", "qwerty", "qwerty123", "abc123", "letmein", "welcome",
        "admin", "administrator", "iloveyou", "monkey", "dragon", "master",
        "login", "princess", "football", "baseball", "shadow", "sunshine",
        "trustno1", "whatever", "freedom", "hello", "charlie", "donald",
        "ashley", "michael", "jessica", "superman", "pokemon", "starwars",
        "passw0rd", "p@ssword", "p@ssw0rd", "welcome1", "admin123",
        "qazwsx", "1q2w3e4r", "zaq12wsx", "654321", "111111", "000000",
        "123123", "121212", "987654321", "qwertyuiop", "asdfghjkl",
        "zxcvbnm", "whatever1", "secret", "secret123", "changeme",
        "default", "root", "toor", "test", "testing", "guest", "user",
        "summer", "winter", "spring", "autumn", "coffee", "lovely",
        "hockey", "ranger", "buster", "soccer", "killer", "george",
        "harley", "thomas", "jordan", "hunter", "batman", "matrix"
    };

    public static PasswordValidationResult Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return PasswordValidationResult.Fail("Master password cannot be empty.");

        if (password.Length < MinimumLength)
            return PasswordValidationResult.Fail($"Master password must be at least {MinimumLength} characters.");

        if (password.Any(char.IsWhiteSpace))
            return PasswordValidationResult.Fail("Master password must not contain spaces.");

        if (!UppercaseRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one uppercase letter.");

        if (!LowercaseRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one lowercase letter.");

        if (!DigitRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one digit.");

        if (!SpecialCharacterRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one special character.");

        if (IsCommonOrPredictable(password))
            return PasswordValidationResult.Fail("This password is too common or predictable and is not allowed.");

        return PasswordValidationResult.Success();
    }

    internal static bool IsCommonOrPredictable(string password)
    {
        if (CommonPasswords.Contains(password))
            return true;

        var normalized = NormalizeForCommonPasswordCheck(password);
        return normalized.Length > 0 && CommonPasswords.Contains(normalized);
    }

    private static string NormalizeForCommonPasswordCheck(string password)
    {
        var core = password.ToLowerInvariant().ToList();

        // Ignore common numeric and punctuation suffixes such as Password123! or Summer2026.
        while (core.Count > 0 && !char.IsLetterOrDigit(core[^1]))
            core.RemoveAt(core.Count - 1);

        while (core.Count > 0 && char.IsDigit(core[^1]))
            core.RemoveAt(core.Count - 1);

        var builder = new StringBuilder(core.Count);

        foreach (var character in core)
        {
            var normalized = character switch
            {
                '@' or '4' => 'a',
                '8' => 'b',
                '3' => 'e',
                '6' => 'g',
                '1' or '!' => 'i',
                '0' => 'o',
                '
}

public sealed record PasswordValidationResult(bool IsValid, string? ErrorMessage)
{
    public static PasswordValidationResult Success() => new(true, null);
    public static PasswordValidationResult Fail(string message) => new(false, message);
}
 or '5' => 's',
                '7' or '+' => 't',
                _ => character
            };

            if (char.IsLetterOrDigit(normalized))
                builder.Append(normalized);
        }

        return builder.ToString();
    }
}

public sealed record PasswordValidationResult(bool IsValid, string? ErrorMessage)
{
    public static PasswordValidationResult Success() => new(true, null);
    public static PasswordValidationResult Fail(string message) => new(false, message);
}
