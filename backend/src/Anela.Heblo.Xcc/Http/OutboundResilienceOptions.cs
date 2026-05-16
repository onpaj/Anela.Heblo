namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Root options that govern the outbound observability handler and per-dependency
/// resilience pipelines. Bound from the "OutboundResilience" configuration section.
/// Defaults are intentionally safe: logging on, 4-minute connection lifetime, no per-dependency retries.
/// </summary>
public sealed class OutboundResilienceOptions
{
    public const string SectionName = "OutboundResilience";

    /// <summary>
    /// When false, the observability handler short-circuits to pass-through.
    /// Lets an operator disable the handler in production without a redeploy.
    /// </summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>
    /// SocketsHttpHandler.PooledConnectionLifetime applied by WithHebloOutboundDefaults.
    /// Must be shorter than the shortest upstream load-balancer idle timeout (Azure App
    /// Service / Front Door default = 4 minutes) to avoid using a socket the LB has closed.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Per-dependency resilience configuration. Key is a logical dependency name
    /// (e.g., "Shoptet", "Flexi"); resolved by the call site via IResiliencePipelineProvider.
    /// </summary>
    public Dictionary<string, DependencyResilienceOptions> Dependencies { get; set; } = new();
}

public sealed class DependencyResilienceOptions
{
    public bool RetryEnabled { get; set; }
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
