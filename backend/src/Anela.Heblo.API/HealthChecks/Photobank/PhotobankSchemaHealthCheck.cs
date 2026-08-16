using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anela.Heblo.API.HealthChecks.Photobank;

public sealed class PhotobankSchemaHealthCheck : IHealthCheck
{
    private const string ExpectedType = "timestamp without time zone";

    private static readonly (string Table, string Column)[] TrackedColumns =
    {
        ("Photos", "TakenAt"),
        ("Photos", "IndexedAt"),
        ("Photos", "ModifiedAt"),
        ("Photos", "LastAutoTaggedAt"),
        ("PhotobankIndexRoots", "CreatedAt"),
        ("PhotobankIndexRoots", "LastIndexedAt"),
        ("PhotoTags", "CreatedAt"),
    };

    private readonly ApplicationDbContext _db;

    public PhotobankSchemaHealthCheck(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsRelational())
        {
            return HealthCheckResult.Healthy("Non-relational provider — schema drift check skipped");
        }

        try
        {
            var rows = await _db.Database
                .SqlQuery<PhotobankColumnTypeRow>($"""
                    SELECT table_name AS "TableName", column_name AS "ColumnName", data_type AS "DataType"
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND ((table_name = 'Photos' AND column_name IN ('TakenAt','IndexedAt','ModifiedAt','LastAutoTaggedAt'))
                        OR (table_name = 'PhotobankIndexRoots' AND column_name IN ('CreatedAt','LastIndexedAt'))
                        OR (table_name = 'PhotoTags' AND column_name = 'CreatedAt'))
                    """)
                .ToListAsync(cancellationToken);

            var drifted = TrackedColumns
                .Select(expected =>
                {
                    var actual = rows.FirstOrDefault(r => r.TableName == expected.Table && r.ColumnName == expected.Column);
                    return new
                    {
                        table = expected.Table,
                        column = expected.Column,
                        expectedType = ExpectedType,
                        actualType = actual?.DataType ?? "MISSING",
                    };
                })
                .Where(d => d.actualType != ExpectedType)
                .ToList();

            if (drifted.Count == 0)
            {
                return HealthCheckResult.Healthy("Photobank schema is aligned");
            }

            return HealthCheckResult.Unhealthy(
                description: "Photobank schema drift detected",
                data: new Dictionary<string, object>
                {
                    ["driftedColumns"] = drifted,
                });
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Photobank schema probe was cancelled");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Photobank schema probe failed", ex);
        }
    }

    private sealed class PhotobankColumnTypeRow
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
    }
}
