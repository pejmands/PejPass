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
    [InlineData("Password1234!")]
    [InlineData("P@ssw0rd123!")]
    [InlineData("Summer2026!")]
    [InlineData("Qwerty123456!")]
    public void Validate_RejectsInvalidOrPredictablePasswords(string? password)
    {
        var result = MasterPasswordPolicy.Validate(password);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Theory]
    [InlineData("R3dwood!Orbit7")]
    [InlineData("Boulder#Train48")]
    public void Validate_AcceptsPasswordsMeetingPolicy(string password)
    {
        var result = MasterPasswordPolicy.Validate(password);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }
}
