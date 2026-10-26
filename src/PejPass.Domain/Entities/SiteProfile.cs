namespace PejPass.Domain.Entities;

/// <summary>
/// Explicitly groups website hosts that share a login flow.
/// </summary>
public sealed class SiteProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public List<string> Domains { get; set; } = [];
}
