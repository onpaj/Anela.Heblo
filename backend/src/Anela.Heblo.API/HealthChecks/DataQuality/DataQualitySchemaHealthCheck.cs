using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Anela.Heblo.API.HealthChecks.DataQuality;

public sealed class DataQualitySchemaHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;

    public DataQualitySchemaHealthCheck(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
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
                    ["sqlState"] = ex.SqlState ?? "42P01"
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "DataQuality probe failed",
                exception: ex);
        }
    }
}
