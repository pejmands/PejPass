using PejPass.Domain.Policies;
using PejPass.Domain.Security;

namespace PejPass.Domain.Tests;

public sealed class PasswordStrengthTests
{
    [Theory]
    [InlineData("Password1234!")]
    [InlineData("P@ssw0rd123!")]
    [InlineData("Summer2026!")]
    [InlineData("Qwerty123456!")]
    [InlineData("Aaaaaaaaaaaa1!")]
    [InlineData("Long Password123!")]
    [InlineData("Long\tPassword123!")]
    public void Evaluate_DoesNotRateCommonOrPredictablePasswordsAsStrong(string password)
    {
        var level = PasswordStrength.Evaluate(password);

        Assert.True(
            level is PasswordStrengthLevel.VeryWeak
                or PasswordStrengthLevel.Weak
                or PasswordStrengthLevel.Fair);
        Assert.True(PasswordStrength.GetScore(password) < MasterPasswordPolicy.MinimumStrengthScore);
        Assert.True(PasswordStrength.IsWeak(password));
    }

    [Theory]
    [InlineData("vQ7!nT4#zP9@kR2$wX6")]
    [InlineData("correct horse battery staple")]
    public void Evaluate_RatesStrongRandomPasswordsAndPassphrasesAsStrong(string password)
    {
        var level = PasswordStrength.Evaluate(password);

        Assert.True(level is PasswordStrengthLevel.Strong or PasswordStrengthLevel.VeryStrong);
        Assert.False(PasswordStrength.IsWeak(password));
    }

    [Fact]
    public void Evaluate_UsesPatternMatchingForRepeatedCharacters()
    {
        const string password = "Aaaaaaaaaaaa1!";

        Assert.True(PasswordStrength.GetScore(password) < MasterPasswordPolicy.MinimumStrengthScore);
        Assert.True(PasswordStrength.IsWeak(password));
        Assert.DoesNotContain(
            PasswordStrength.Evaluate(password),
            new[] { PasswordStrengthLevel.Strong, PasswordStrengthLevel.VeryStrong });
    }

    [Theory]
    [InlineData(null, PasswordStrengthLevel.Empty)]
    [InlineData("", PasswordStrengthLevel.Empty)]
    [InlineData("abc123", PasswordStrengthLevel.VeryWeak)]
    public void Evaluate_HandlesEmptyAndShortPasswords(string? password, PasswordStrengthLevel expected)
    {
        Assert.Equal(expected, PasswordStrength.Evaluate(password));
    }

    [Fact]
    public void GetScore_UsesZeroToFourZxcvbnScale()
    {
        Assert.Equal(0, PasswordStrength.GetScore("password"));
        Assert.InRange(PasswordStrength.GetScore("correct horse battery staple"), 3, 4);
    }
}
