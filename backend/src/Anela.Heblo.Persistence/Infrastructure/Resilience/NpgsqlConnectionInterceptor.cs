using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Records structured properties for connection-lifecycle events. Read-path disconnects
/// (PostgresException at NpgsqlConnector.ReadMessageLong) never reach SaveChanges and
/// so cannot be captured by PostgresExceptionLoggingInterceptor.
/// Also records pool-exhaustion wait (any connection open > 1 s).
/// </summary>
public sealed class NpgsqlConnectionInterceptor : DbConnectionInterceptor
{
    private const double PoolExhaustionThresholdSeconds = 1.0;

    private readonly DbResilienceMetrics _metrics;
    private readonly ILogger<NpgsqlConnectionInterceptor> _logger;

    private static readonly AsyncLocal<Stopwatch?> OpenStopwatch = new();

    public NpgsqlConnectionInterceptor(
        DbResilienceMetrics metrics,
        ILogger<NpgsqlConnectionInterceptor> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        OpenStopwatch.Value = Stopwatch.StartNew();
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        OpenStopwatch.Value = Stopwatch.StartNew();
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        RecordOpenLatency();
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordOpenLatency();
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        LogConnectionFailure(connection, eventData.Exception);
        base.ConnectionFailed(connection, eventData);
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogConnectionFailure(connection, eventData.Exception);
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    private void RecordOpenLatency()
    {
        var sw = OpenStopwatch.Value;
        if (sw is null) return;

        sw.Stop();
        OpenStopwatch.Value = null;

        var seconds = sw.Elapsed.TotalSeconds;
        if (seconds > PoolExhaustionThresholdSeconds)
        {
            _metrics.RecordPoolExhaustionWait(seconds);
            _logger.LogWarning(
                "DbPoolExhaustionWait wait_seconds={WaitSeconds:F2}",
                seconds);
        }
    }

    private void LogConnectionFailure(DbConnection connection, Exception exception)
    {
        var host = SafeGetProperty(connection, "Host");
        var database = SafeGetProperty(connection, "Database");

        _logger.LogWarning(
            exception,
            "DbConnectionFailed exception.type={ExceptionType} npgsql.host={Host} npgsql.database={Database}",
            exception.GetType().FullName,
            host,
            database);
    }

    private static string? SafeGetProperty(DbConnection connection, string name)
    {
        try
        {
            return connection switch
            {
                NpgsqlConnection npg when name == "Host" => npg.DataSource,
                NpgsqlConnection npg when name == "Database" => npg.Database,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
