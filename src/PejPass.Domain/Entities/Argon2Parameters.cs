namespace PejPass.Domain.Entities;

public sealed record Argon2Parameters(
    int MemorySizeKiB,
    int Iterations,
    int DegreeOfParallelism)
{
    public static Argon2Parameters Default { get; } = new(
        MemorySizeKiB: 65536,
        Iterations: 3,
        DegreeOfParallelism: 4);

    public void Validate()
    {
        if (MemorySizeKiB is < 8192 or > 262144)
            throw new ArgumentOutOfRangeException(
                nameof(MemorySizeKiB),
                "Argon2 memory must be between 8192 and 262144 KiB.");

        if (Iterations is < 1 or > 10)
            throw new ArgumentOutOfRangeException(
                nameof(Iterations),
                "Argon2 iterations must be between 1 and 10.");

        if (DegreeOfParallelism is < 1 or > 8)
            throw new ArgumentOutOfRangeException(
                nameof(DegreeOfParallelism),
                "Argon2 parallelism must be between 1 and 8.");

        if (MemorySizeKiB < 8 * DegreeOfParallelism)
            throw new ArgumentOutOfRangeException(
                nameof(MemorySizeKiB),
                "Argon2 memory must be at least eight times the parallelism.");
    }
}
