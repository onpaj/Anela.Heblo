namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantSettings
{
    public static string ConfigurationKey => "HomeAssistant";

    public string BaseUrl { get; init; } = null!;
    public string AccessToken { get; init; } = null!;
    public string InnerTemperatureEntityId { get; init; } = null!;
    public string InnerHumidityEntityId { get; init; } = null!;
    public string OuterTemperatureEntityId { get; init; } = null!;
    public string OuterHumidityEntityId { get; init; } = null!;
    public int RequestTimeoutSeconds { get; init; } = 3;
    public int ConditionsCacheDurationMinutes { get; init; } = 5;

    /// <summary>Polly retry attempts on transient HTTP failures. 0 disables retry.</summary>
    public int RetryCount { get; init; } = 2;

    /// <summary>Base delay (ms) for exponential backoff between retry attempts.</summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 200;

    /// <summary>Upper bound (seconds) on the jittered backoff between retry attempts.</summary>
    public int RetryMaxDelaySeconds { get; init; } = 2;

    /// <summary>Last-known-good snapshot is reused for up to this many minutes. 0 disables stale fallback.</summary>
    public int StaleSnapshotMaxAgeMinutes { get; init; } = 60;

    /// <summary>Health check reports Healthy only if the last Live snapshot is younger than this.</summary>
    public int LiveSnapshotMaxAgeMinutes { get; init; } = 15;

    /// <summary>Enables the circuit breaker around Home Assistant HTTP calls.</summary>
    public bool CircuitBreakerEnabled { get; init; } = true;

    /// <summary>Failure ratio (0-1) within the sampling window that opens the circuit.</summary>
    public double CircuitBreakerFailureRatio { get; init; } = 0.5;

    /// <summary>Minimum number of calls within the sampling window before the circuit can open.</summary>
    public int CircuitBreakerMinimumThroughput { get; init; } = 8;

    /// <summary>Length (seconds) of the sampling window used to evaluate the failure ratio.</summary>
    public int CircuitBreakerSamplingDurationSeconds { get; init; } = 60;

    /// <summary>How long (seconds) the circuit stays open before a trial call is allowed.</summary>
    public int CircuitBreakerBreakDurationSeconds { get; init; } = 60;
}
