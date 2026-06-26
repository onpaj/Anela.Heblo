namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

public sealed class DbResilienceOptions
{
    public const string SectionName = "Database:Resilience";

    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(4);

    public TimeSpan TotalTimeBudget { get; init; } = TimeSpan.FromSeconds(10);
}
