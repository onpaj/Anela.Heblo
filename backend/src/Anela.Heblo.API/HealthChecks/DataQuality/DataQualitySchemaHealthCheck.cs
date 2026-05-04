using System.Collections.Generic;
using System.Diagnostics;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.API.HealthChecks.DataQuality;

public sealed class DataQualitySchemaHealthCheck : IHealthCheck
{
    internal const string ProbeName = "data-quality-schema";

    private readonly ApplicationDbContext _db;
    private readonly ILogger<DataQualitySchemaHealthCheck> _logger;

    public DataQualitySchemaHealthCheck(
        ApplicationDbContext db,
        ILogger<DataQualitySchemaHealthCheck> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("DataQuality schema is reachable");
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return HealthCheckResult.Unhealthy(
                description: "DataQuality table not found",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["entity"] = "DqtRun",
                    ["expectedTable"] = "DqtRuns",
                    ["schema"] = "public",
                    ["sqlState"] = "42P01"
                });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "DataQuality probe cancelled. ProbeName={ProbeName}, ElapsedMs={ElapsedMs}",
                ProbeName,
                stopwatch.ElapsedMilliseconds);
            return HealthCheckResult.Degraded("DataQuality probe was cancelled");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "DataQuality probe failed",
                exception: ex);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
