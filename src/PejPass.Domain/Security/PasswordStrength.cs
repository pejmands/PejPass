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

        var score = Core.EvaluatePassword(input).Score;

        // Repeated character runs can be underestimated by zxcvbn for mixed-case inputs.
        if (HasLongRepeatedCharacterRun(input))
        {
            var compactInput = new string(input
                .Where((character, index) =>
                    index == 0 || char.ToUpperInvariant(character) != char.ToUpperInvariant(input[index - 1]))
                .ToArray());
            score = Math.Min(score, Core.EvaluatePassword(compactInput).Score);
        }

        // Score an alphanumeric-only variant when whitespace does not separate an intentional multi-word passphrase.
        // This prevents spaces, tabs, or punctuation around predictable patterns from artificially raising the score.
        if (input.Any(char.IsWhiteSpace))
        {
            var words = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var meaningfulWordCount = words.Count(word => word.Count(char.IsLetter) >= 3);

            if (meaningfulWordCount < 3)
            {
                var compactInput = string.Concat(input.Where(char.IsLetterOrDigit));
                score = Math.Min(score, Core.EvaluatePassword(compactInput).Score);
            }
        }

        return score;
    }

    private static bool HasLongRepeatedCharacterRun(string input)
    {
        var runLength = 1;

        for (var index = 1; index < input.Length; index++)
        {
            if (char.IsLetterOrDigit(input[index]) &&
                char.IsLetterOrDigit(input[index - 1]) &&
                char.ToUpperInvariant(input[index]) == char.ToUpperInvariant(input[index - 1]))
            {
                runLength++;
                if (runLength >= 4)
                    return true;
            }
            else
            {
                runLength = 1;
            }
        }

        return false;
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
            4 => PasswordStrengthLevel.VeryStrong,
            _ => PasswordStrengthLevel.VeryWeak
        };
    }

    /// <summary>
    /// Passwords below zxcvbn's maximum score of 4 are too guessable for the vault health threshold.
    /// </summary>
    public static bool IsWeak(PasswordStrengthLevel level) =>
        level is PasswordStrengthLevel.Empty
            or PasswordStrengthLevel.VeryWeak
            or PasswordStrengthLevel.Weak
            or PasswordStrengthLevel.Fair;

    public static bool IsWeak(string? password) => IsWeak(Evaluate(password));

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
