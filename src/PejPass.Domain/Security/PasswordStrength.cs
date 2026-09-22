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
    public static PasswordStrengthLevel Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordStrengthLevel.Empty;

        var score = 0;

        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        if (password.Length >= 20) score++;

        var hasLower = password.Any(char.IsLower);
        var hasUpper = password.Any(char.IsUpper);
        var hasDigit = password.Any(char.IsDigit);
        var hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));

        var classes = 0;
        if (hasLower) classes++;
        if (hasUpper) classes++;
        if (hasDigit) classes++;
        if (hasSymbol) classes++;

        if (classes >= 2) score++;
        if (classes >= 3) score++;
        if (classes >= 4) score++;

        // Cap for short passwords
        if (password.Length < 8)
            return PasswordStrengthLevel.VeryWeak;

        // Repeated characters penalty
        if (password.Distinct().Count() <= password.Length / 3)
            score = Math.Max(0, score - 1);

        return score switch
        {
            <= 2 => PasswordStrengthLevel.Weak,
            3 => PasswordStrengthLevel.Fair,
            4 or 5 => PasswordStrengthLevel.Strong,
            _ => PasswordStrengthLevel.VeryStrong
        };
    }

    public static bool IsWeak(string? password)
    {
        var level = Evaluate(password);
        return level is PasswordStrengthLevel.Empty
            or PasswordStrengthLevel.VeryWeak
            or PasswordStrengthLevel.Weak;
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
