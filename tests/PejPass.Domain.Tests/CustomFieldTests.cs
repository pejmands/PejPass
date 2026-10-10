using PejPass.Domain.Entities;

namespace PejPass.Domain.Tests;

public sealed class CustomFieldTests
{
    private static CustomField Field(string name = "A", string value = "1", bool secret = false) =>
        new() { Name = name, Value = value, IsSecret = secret };

    [Fact]
    public void AreSequencesEqual_IdenticalSequences_ReturnsTrue()
    {
        Assert.True(CustomField.AreSequencesEqual(
            [Field(), Field("B", "2", true)],
            [Field(), Field("B", "2", true)]));
    }

    [Fact]
    public void AreSequencesEqual_OnlySecretValueDiffers_ReturnsFalse()
    {
        Assert.False(CustomField.AreSequencesEqual(
            [Field("Recovery", "old", true)],
            [Field("Recovery", "new", true)]));
    }

    [Fact]
    public void AreSequencesEqual_OnlySecretFlagDiffers_ReturnsFalse()
    {
        Assert.False(CustomField.AreSequencesEqual(
            [Field("A", "1", false)],
            [Field("A", "1", true)]));
    }

    [Fact]
    public void AreSequencesEqual_DifferentOrder_ReturnsFalse()
    {
        Assert.False(CustomField.AreSequencesEqual(
            [Field("A"), Field("B")],
            [Field("B"), Field("A")]));
    }

    [Fact]
    public void AreSequencesEqual_DifferentCount_ReturnsFalse()
    {
        Assert.False(CustomField.AreSequencesEqual([Field()], []));
    }

    [Fact]
    public void AreSequencesEqual_IsCaseSensitive()
    {
        Assert.False(CustomField.AreSequencesEqual(
            [Field("a", "x")],
            [Field("A", "x")]));
    }

    [Fact]
    public void AreSequencesEqual_HandlesNulls()
    {
        Assert.True(CustomField.AreSequencesEqual(null, null));
        Assert.False(CustomField.AreSequencesEqual(null, []));
        Assert.False(CustomField.AreSequencesEqual([], null));
    }
}
