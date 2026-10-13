using PejPass.Domain.Security;

namespace PejPass.Domain.Policies;

/// <summary>
/// Requires a minimum length and a zxcvbn score suitable for protecting the entire vault.
/// </summary>
public static class MasterPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = PasswordStrength.MaximumEvaluatedLength;
    public const int MinimumStrengthScore = 4;

    public static PasswordValidationResult Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return PasswordValidationResult.Fail("Master password cannot be empty.");

        if (password.Length < MinimumLength)
        {
            return PasswordValidationResult.Fail(
                $"Master password must be at least {MinimumLength} characters.");
        }

        if (password.Length > MaximumLength)
        {
            return PasswordValidationResult.Fail(
                $"Master password must not exceed {MaximumLength} characters.");
        }

        if (IsCommonOrPredictable(password))
        {
            return PasswordValidationResult.Fail(
                "This master password is too easy to guess. Use a longer password or a passphrase with several uncommon words.");
        }

        return PasswordValidationResult.Success();
    }

    internal static bool IsCommonOrPredictable(string password) =>
        PasswordStrength.GetScore(password) < MinimumStrengthScore;
}

public sealed record PasswordValidationResult(bool IsValid, string? ErrorMessage)
{
    public static PasswordValidationResult Success() => new(true, null);
    public static PasswordValidationResult Fail(string message) => new(false, message);
}
