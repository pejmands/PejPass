
using PejPass.Wpf.Controls;
using System.Reflection;

namespace PejPass.Wpf.Tests.Controls;

public sealed class PasswordRevealBoxReflectionTests
{
    [Fact]
    public void Reflection_RequiredPasswordBoxMembersAreAvailable()
    {
        var reflectionField = typeof(PasswordRevealBox).GetField(
            "Reflection",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(reflectionField);

        var reflection = reflectionField.GetValue(null);

        Assert.NotNull(reflection);
    }
}
