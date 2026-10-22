using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Services;

public sealed class VaultFileAssociationTests
{
    [Theory]
    [InlineData("OtherApp.Vault", null, true)]
    [InlineData("PejPass.Vault", "OtherApp.Vault", true)]
    [InlineData(null, "OtherApp.Vault", true)]
    [InlineData("OtherApp.Vault", "PejPass.Vault", false)]
    [InlineData("PejPass.Vault", null, false)]
    [InlineData(null, null, false)]
    public void ShouldPreserveExistingAssociation_RespectsUserChoice(
        string? registeredProgId,
        string? userChoiceProgId,
        bool expected)
    {
        Assert.Equal(
            expected,
            VaultFileAssociation.ShouldPreserveExistingAssociation(
                registeredProgId,
                userChoiceProgId));
    }
}
