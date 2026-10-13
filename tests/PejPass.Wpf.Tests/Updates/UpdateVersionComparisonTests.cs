using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateVersionComparisonTests
{
    [Theory]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0", "1.2.0.0")]
    [InlineData("v1.2.0", "1.2")]
    public void SameVersion_TreatsOmittedTrailingComponentsAsZero(
        string first,
        string second)
    {
        Assert.True(UpdateService.SameVersion(first, second));
        Assert.True(UpdateService.SameVersion(second, first));
    }

    [Theory]
    [InlineData("1.2.1", "1.2.0")]
    [InlineData("1.3", "1.2.0")]
    public void SameVersion_ReturnsFalseForDifferentVersions(
        string first,
        string second)
    {
        Assert.False(UpdateService.SameVersion(first, second));
    }

    [Fact]
    public void SameVersion_ReturnsFalseForInvalidVersions()
    {
        Assert.False(UpdateService.SameVersion("not-a-version", "1.2.0"));
        Assert.False(UpdateService.SameVersion("1.2.0", ""));
    }

    [Fact]
    public void IsNewerVersion_DoesNotTreatTrailingZeroAsAnUpdate()
    {
        Assert.False(UpdateService.IsNewerVersion("1.2.0", "1.2"));
        Assert.False(UpdateService.IsNewerVersion("1.2.0.0", "1.2.0"));
    }
}
