namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

public sealed class DbResilienceOptions
{
    public const string SectionName = "Database:Resilience";

    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Despite the name, this is consumed as a per-attempt ceiling in the current pipeline shape
    /// (DbResiliencePipelineProvider builds AddRetry(...).AddTimeout(TotalTimeBudget) — AddTimeout
    /// wraps each individual attempt, not the whole retry loop). Worst-case latency for a call that
    /// exhausts all retries is therefore roughly (MaxRetryAttempts + 1) * TotalTimeBudget plus backoff
    /// delay, not TotalTimeBudget alone.
    /// </summary>
    public TimeSpan TotalTimeBudget { get; init; } = TimeSpan.FromSeconds(10);
}
