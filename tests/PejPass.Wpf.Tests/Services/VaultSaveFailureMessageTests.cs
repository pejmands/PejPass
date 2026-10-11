using PejPass.Wpf.Services;
using System.IO;

namespace PejPass.Wpf.Tests.Services;

public sealed class VaultSaveFailureMessageTests
{
    [Fact]
    public void ForHistoryLimit_ProvidesManualCleanupGuidance()
    {
        var message = VaultSaveFailureMessage.For(
            new InvalidDataException("Vault exceeds the limit of 1000000 history items."),
            "Fallback message");

        Assert.Contains("Delete older history snapshots", message);
    }

    [Fact]
    public void ForOtherValidationErrors_ReturnsFallbackMessage()
    {
        const string fallbackMessage = "Fallback message";

        var message = VaultSaveFailureMessage.For(
            new InvalidDataException("Vault contains an invalid entry."),
            fallbackMessage);

        Assert.Equal(fallbackMessage, message);
    }
}
