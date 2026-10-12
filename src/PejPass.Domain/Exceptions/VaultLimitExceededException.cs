namespace PejPass.Domain.Exceptions;

public enum VaultLimit
{
    HistoryItems
}

/// <summary>Indicates that a vault exceeds a supported size or collection limit.</summary>
public sealed class VaultLimitExceededException(VaultLimit limit, string message)
    : Exception(message)
{
    public VaultLimit Limit { get; } = limit;
}
