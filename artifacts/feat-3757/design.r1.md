# Design: Photobank nightly index job — close the DateTime Kind=Unspecified regression after PR #3743

## Component Design

### `PhotobankSchemaHealthCheck` (new)

`backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs`

Responsibility: on every `/health/ready` probe, confirm the physical PostgreSQL type of every
Photobank `DateTime` column matches what the EF model declares (`timestamp` without time zone).
Read-only — never writes.

```csharp
public sealed class PhotobankSchemaHealthCheck : IHealthCheck
{
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

    public PhotobankSchemaHealthCheck(ApplicationDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
            return HealthCheckResult.Healthy("Non-relational provider — schema drift check skipped");

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
                .ToListAsync(ct);

            var drifted = TrackedColumns
                .Select(expected =>
                {
                    var actual = rows.FirstOrDefault(r => r.TableName == expected.Table && r.ColumnName == expected.Column);
                    return (expected.Table, expected.Column, ActualType: actual?.DataType);
                })
                .Where(r => r.ActualType != "timestamp without time zone")
                .ToList();

            if (drifted.Count == 0)
                return HealthCheckResult.Healthy("Photobank schema is aligned");

            return HealthCheckResult.Unhealthy(
                description: "Photobank schema drift detected",
                data: new Dictionary<string, object>
                {
                    ["driftedColumns"] = drifted
                        .Select(d => new { table = d.Table, column = d.Column, expectedType = "timestamp without time zone", actualType = d.ActualType ?? "MISSING" })
                        .ToList(),
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
```

Note for implementation: if `Database.SqlQuery<T>` against a private nested keyless type does not
compile cleanly under EF Core 8.0.8 (it may require the row type to be a `public` top-level class, or
`FromSqlRaw` may be needed instead for a fully unmapped ad-hoc type), adjust the row type's visibility
or switch to `FromSqlRaw` accordingly — the query text and comparison logic above are the contract;
the exact EF API surface used to execute it is an implementation detail to confirm at build time (see
arch-review's Risk 1).

**Registration** (`ServiceCollectionExtensions.cs`, `AddHealthCheckServices`, alongside the existing
`data-quality-schema` check):

```csharp
.AddCheck<PhotobankSchemaHealthCheck>(
    name: "photobank-schema",
    failureStatus: HealthStatus.Unhealthy,
    tags: new[] { "ready", "db", "schema" })
```

### `PhotobankIndexJob.UpsertPhotoBatchAsync` (modified, one line)

`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:181`

Before:
```csharp
photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;
```

After:
```csharp
photo.ModifiedAt = item.LastModifiedAt.HasValue
    ? DateTime.SpecifyKind(item.LastModifiedAt.Value, DateTimeKind.Utc)
    : DateTime.UtcNow;
```

No other lines in this method change. `DateTime.SpecifyKind` re-labels the existing instant as UTC
without shifting it — Microsoft Graph's `lastModifiedDateTime` is always UTC by API contract, so this
is a correctness fix (removing an ambiguous/incorrect `Kind` label), not a behavior change to the
actual point in time stored.

## Data Schemas

No schema changes. This feature reads (never writes) `information_schema.columns` for the seven
existing columns listed in the spec's Data Model table. No new tables, columns, migrations, or API
request/response shapes — the only externally-visible change is the new `photobank-schema` entry
inside the existing `/health/ready` JSON body's `entries` map, following the same shape
`data-quality-schema` already produces (see arch-review's Interfaces and Contracts section for the
exact `data` payload on failure).
