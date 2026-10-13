using Zxcvbn;

namespace PejPass.Domain.Security;

public enum PasswordStrengthLevel
{
    Empty = 0,
    VeryWeak = 1,
    Weak = 2,
    Fair = 3,
    Strong = 4,
    VeryStrong = 5
}

public static class PasswordStrength
{
    public const int MaximumEvaluatedLength = 256;

    /// <summary>Returns the zxcvbn guessability score from 0 (guessable) to 4 (very hard to guess).</summary>
    public static int GetScore(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        var input = password.Length > MaximumEvaluatedLength
            ? password[..MaximumEvaluatedLength]
            : password;

        return Core.EvaluatePassword(input).Score;
    }

    public static PasswordStrengthLevel Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordStrengthLevel.Empty;

        return GetScore(password) switch
        {
            0 => PasswordStrengthLevel.VeryWeak,
            1 => PasswordStrengthLevel.Weak,
            2 => PasswordStrengthLevel.Fair,
            3 => PasswordStrengthLevel.Strong,
            _ => PasswordStrengthLevel.VeryStrong
        };
    }

    /// <summary>
    /// Passwords below zxcvbn score 3 are too guessable for the vault health threshold.
    /// </summary>
    public static bool IsWeak(string? password)
    {
        var level = Evaluate(password);
        return level is PasswordStrengthLevel.Empty
            or PasswordStrengthLevel.VeryWeak
            or PasswordStrengthLevel.Weak
            or PasswordStrengthLevel.Fair;
    }

    public static string GetLabel(PasswordStrengthLevel level) => level switch
    {
        PasswordStrengthLevel.Empty => string.Empty,
        PasswordStrengthLevel.VeryWeak => "Very weak",
        PasswordStrengthLevel.Weak => "Weak",
        PasswordStrengthLevel.Fair => "Fair",
        PasswordStrengthLevel.Strong => "Strong",
        PasswordStrengthLevel.VeryStrong => "Very strong",
        _ => string.Empty
    };

    /// <summary>0–100 for progress bars.</summary>
    public static double GetProgress(PasswordStrengthLevel level) => level switch
    {
        PasswordStrengthLevel.Empty => 0,
        PasswordStrengthLevel.VeryWeak => 15,
        PasswordStrengthLevel.Weak => 35,
        PasswordStrengthLevel.Fair => 55,
        PasswordStrengthLevel.Strong => 75,
        PasswordStrengthLevel.VeryStrong => 100,
        _ => 0
    };
}
