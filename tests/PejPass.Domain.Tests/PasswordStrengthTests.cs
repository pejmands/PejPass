using PejPass.Domain.Security;

namespace PejPass.Domain.Tests;

public sealed class PasswordStrengthTests
{
    [Theory]
    [InlineData("Password1234!")]
    [InlineData("P@ssw0rd123!")]
    [InlineData("Summer2026!")]
    [InlineData("Qwerty123456!")]
    public void Evaluate_DoesNotRateCommonPasswordVariantsAsStrong(string password)
    {
        Assert.Equal(PasswordStrengthLevel.Weak, PasswordStrength.Evaluate(password));
        Assert.True(PasswordStrength.IsWeak(password));
    }

    [Fact]
    public void Evaluate_RatesLongMixedUniquePasswordAsStrong()
    {
        var level = PasswordStrength.Evaluate("R3dwood!Orbit7");

        Assert.True(level is PasswordStrengthLevel.Strong or PasswordStrengthLevel.VeryStrong);
        Assert.False(PasswordStrength.IsWeak("R3dwood!Orbit7"));
    }

    [Theory]
    [InlineData(null, PasswordStrengthLevel.Empty)]
    [InlineData("", PasswordStrengthLevel.Empty)]
    [InlineData("abc123", PasswordStrengthLevel.VeryWeak)]
    public void Evaluate_HandlesEmptyAndShortPasswords(string? password, PasswordStrengthLevel expected)
    {
        Assert.Equal(expected, PasswordStrength.Evaluate(password));
    }
}
