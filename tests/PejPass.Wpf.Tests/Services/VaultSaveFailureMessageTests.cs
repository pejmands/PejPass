using PejPass.Domain.Exceptions;
using PejPass.Wpf.Services;
using System.IO;

namespace PejPass.Wpf.Tests.Services;

public sealed class VaultSaveFailureMessageTests
{
    [Fact]
    public void ForHistoryLimit_ProvidesManualCleanupGuidance()
    {
        var message = VaultSaveFailureMessage.For(
            new VaultLimitExceededException(VaultLimit.HistoryItems, "any text"),
            "Fallback message");

        Assert.Contains("Delete older history snapshots", message);
    }

    [Fact]
    public void ForEntryLimit_MentionsTrash()
    {
        var message = VaultSaveFailureMessage.For(
            new VaultLimitExceededException(VaultLimit.Entries, "x"),
            "Fallback message");

        Assert.Contains("Trash", message);
        Assert.Contains(VaultLimits.MaxEntries.ToString("N0"), message);
    }

    [Fact]
    public void ForTextLength_NamesTheFieldAndTheEntry()
    {
        var message = VaultSaveFailureMessage.For(
            new VaultLimitExceededException(VaultLimit.TextLength, "x")
            {
                Subject = "entry notes",
                EntryTitle = "Router"
            },
            "Fallback message");

        Assert.Contains("entry notes", message);
        Assert.Contains("Router", message);
    }

    [Fact]
    public void ForCollectionItems_NamesTheCollection()
    {
        var message = VaultSaveFailureMessage.For(
            new VaultLimitExceededException(VaultLimit.CollectionItems, "x")
            {
                Subject = "tags",
                EntryTitle = "Router"
            },
            "Fallback message");

        Assert.Contains("too many tags", message);
        Assert.Contains("Router", message);
    }

    [Fact]
    public void ForTagLength_NamesTheEntry()
    {
        var message = VaultSaveFailureMessage.For(
            new VaultLimitExceededException(VaultLimit.TagLength, "x")
            {
                EntryTitle = "Router"
            },
            "Fallback message");

        Assert.Contains("tag", message);
        Assert.Contains("Router", message);
        Assert.Contains(VaultLimits.MaxTagCharacters.ToString("N0"), message);
    }

    [Fact]
    public void ForEveryLimit_ThereIsADedicatedMessage()
    {
        foreach (var limit in Enum.GetValues<VaultLimit>())
        {
            var message = VaultSaveFailureMessage.For(
                new VaultLimitExceededException(limit, "x"),
                "Fallback message");

            Assert.NotEqual("Fallback message", message);
        }
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
