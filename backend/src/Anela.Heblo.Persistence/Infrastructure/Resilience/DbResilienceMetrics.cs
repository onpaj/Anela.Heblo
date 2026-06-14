using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

public sealed class DbResilienceMetrics : IDisposable
{
    public const string MeterName = "Anela.Heblo.Database.Resilience";

    private readonly Meter _meter;
    private readonly Counter<long> _retryAttempts;
    private readonly Counter<long> _retrySuccess;
    private readonly Counter<long> _retryFailure;
    private readonly Histogram<double> _poolExhaustionWait;

    public DbResilienceMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _retryAttempts = _meter.CreateCounter<long>(
            "db.retry.attempts",
            description: "Database retry attempts, tagged by exception type");

        _retrySuccess = _meter.CreateCounter<long>(
            "db.retry.success",
            description: "Operations that succeeded after one or more retries");

        _retryFailure = _meter.CreateCounter<long>(
            "db.retry.failure",
            description: "Operations that exhausted retries and rethrew");

        _poolExhaustionWait = _meter.CreateHistogram<double>(
            "npgsql.pool.exhaustion_wait_seconds",
            unit: "s",
            description: "Time spent waiting for a free connection from the Npgsql pool");
    }

    public void RecordRetryAttempt(string exceptionType, int attempt)
    {
        _retryAttempts.Add(1, new TagList
        {
            { "exception.type", exceptionType },
            { "attempt", attempt },
        });
    }

    public void RecordRetrySuccess(int totalAttempts) =>
        _retrySuccess.Add(1, new TagList { { "total_attempts", totalAttempts } });

    public void RecordRetryFailure(string exceptionType, int totalAttempts) =>
        _retryFailure.Add(1, new TagList
        {
            { "exception.type", exceptionType },
            { "total_attempts", totalAttempts },
        });

    public void RecordPoolExhaustionWait(double waitSeconds) =>
        _poolExhaustionWait.Record(waitSeconds);

    public void Dispose() => _meter.Dispose();
}
