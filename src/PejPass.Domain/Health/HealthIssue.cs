namespace PejPass.Domain.Health;

public enum HealthIssueKind
{
    DuplicatePassword,
    WeakPassword,
    PasswordNeedsImprovement,
    UsernameEqualsPassword,
    MissingTotp,
    StalePassword
}

public sealed record HealthIssue(
    HealthIssueKind Kind,
    Guid EntryId,
    string EntryTitle,
    string Detail);
