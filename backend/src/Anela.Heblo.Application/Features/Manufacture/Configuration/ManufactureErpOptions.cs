namespace Anela.Heblo.Application.Features.Manufacture.Configuration;

/// <summary>
/// Configuration options for Manufacture ERP integration (FlexiBee).
/// </summary>
public class ManufactureErpOptions
{
    /// <summary>
    /// Maximum number of seconds to wait for a single Flexi ERP call before timing out.
    /// Defaults to 60 seconds. Set to 0 to disable the application-level timeout.
    /// </summary>
    public int ErpTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Entra ID group identifier consumed by GetManufactureSettings to gate
    /// "responsible person" workflows on the frontend. Bound from configuration
    /// key "ManufactureErp:ManufactureGroupId" (env var "ManufactureErp__ManufactureGroupId").
    /// </summary>
    public string? ManufactureGroupId { get; set; }

    /// <summary>
    /// Minimum number of calls that must land inside <see cref="ErpCircuitBreakerSamplingDurationSeconds"/>
    /// before the failure ratio is evaluated. Deliberately lower than the
    /// CatalogResilienceService precedent (3) because this call site's traffic
    /// (~2.4 confirm calls/hour combined) is far lower than Catalog's cache-refresh
    /// background sync.
    /// </summary>
    public int ErpCircuitBreakerMinimumThroughput { get; set; } = 2;

    /// <summary>
    /// Fraction of calls within the sampling window that must fail (timeout or
    /// transport error) for the breaker to open.
    /// </summary>
    public double ErpCircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Rolling window, in seconds, used to evaluate the failure ratio. Widened to
    /// 15 minutes (vs. Catalog's 60s) so that <see cref="ErpCircuitBreakerMinimumThroughput"/>
    /// calls can plausibly land in the same window at this endpoint's real call rate.
    /// </summary>
    public int ErpCircuitBreakerSamplingDurationSeconds { get; set; } = 900;

    /// <summary>
    /// How long the breaker stays open (failing fast, no HTTP call) before a
    /// half-open probe is attempted.
    /// </summary>
    public int ErpCircuitBreakerBreakDurationSeconds { get; set; } = 30;
}
