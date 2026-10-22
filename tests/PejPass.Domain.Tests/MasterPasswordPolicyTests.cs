using PejPass.Domain.Policies;

namespace PejPass.Domain.Tests;

public sealed class MasterPasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("LongPassword!")]
    [InlineData("longpassword123!")]
    [InlineData("LONGPASSWORD123!")]
    [InlineData("LongPassword123")]
    [InlineData("Long Password123!")]
    [InlineData("Long\tPassword123!")]
    [InlineData("Password1234!")]
    [InlineData("P@ssw0rd123!")]
    [InlineData("Summer2026!")]
    [InlineData("Qwerty123456!")]
    [InlineData("Aaaaaaaaaaaa1!")]
    public void Validate_RejectsInvalidOrPredictablePasswords(string? password)
    {
        var result = MasterPasswordPolicy.Validate(password);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Theory]
    [InlineData("vQ7!nT4#zP9@kR2$wX6")]
    [InlineData("correct horse battery staple")]
    public void Validate_AcceptsStrongPasswordsAndPassphrases(string password)
    {
        var result = MasterPasswordPolicy.Validate(password);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsPasswordLongerThanEstimatorLimit()
    {
        var password = new string('a', MasterPasswordPolicy.MaximumLength + 1);

        var result = MasterPasswordPolicy.Validate(password);

        Assert.False(result.IsValid);
        Assert.Contains("256", result.ErrorMessage);
    }
}
