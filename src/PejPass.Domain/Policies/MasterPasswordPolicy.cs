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
        "password", "password123", "123456", "123456789", "qwerty",
        "abc123", "letmein", "welcome", "admin", "iloveyou",
        "monkey", "dragon", "master", "login", "princess"
    };

    public static PasswordValidationResult Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return PasswordValidationResult.Fail("Master password cannot be empty.");

        if (password.Length < MinimumLength)
            return PasswordValidationResult.Fail($"Master password must be at least {MinimumLength} characters.");

        if (password.Contains(' '))
            return PasswordValidationResult.Fail("Master password must not contain spaces.");

        if (!UppercaseRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one uppercase letter.");

        if (!LowercaseRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one lowercase letter.");

        if (!DigitRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one digit.");

        if (!SpecialCharacterRegex().IsMatch(password))
            return PasswordValidationResult.Fail("Master password must contain at least one special character.");

        if (CommonPasswords.Contains(password))
            return PasswordValidationResult.Fail("This password is too common and is not allowed.");

        return PasswordValidationResult.Success();
    }
}

public sealed record PasswordValidationResult(bool IsValid, string? ErrorMessage)
{
    public static PasswordValidationResult Success() => new(true, null);
    public static PasswordValidationResult Fail(string message) => new(false, message);
}
