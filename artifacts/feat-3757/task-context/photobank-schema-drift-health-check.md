### task: photobank-schema-drift-health-check

**Files:**
- Create: `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- Test: `backend/test/Anela.Heblo.Tests/API/HealthChecks/Photobank/PhotobankSchemaHealthCheckTests.cs`

- [ ] **Step 1: Write the failing tests first**

Create `backend/test/Anela.Heblo.Tests/API/HealthChecks/Photobank/PhotobankSchemaHealthCheckTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.API.HealthChecks.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Anela.Heblo.Tests.API.HealthChecks.Photobank;

public class PhotobankSchemaHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenProviderNotRelational_ReturnsHealthyAndSkips()
    {
        // Arrange — the in-memory provider used by this test suite is non-relational, so the
        // check must short-circuit to Healthy rather than attempting an information_schema
        // query the in-memory provider cannot serve.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"photobank-schema-{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var healthCheck = new PhotobankSchemaHealthCheck(context);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Non-relational provider — schema drift check skipped");
    }
}
```

(A relational-provider test against real `timestamp`/`timestamptz` columns requires a live Postgres
connection this sandbox does not have — the in-memory-provider-skip path above is the only path
testable without one, and is the same limitation `DataQualitySchemaHealthCheckTests` accepts for its
own real-DB paths, which it tests via a mocked throwing `DbSet` instead. If a local/CI Postgres
instance is available when this task runs, add a second test there that creates the tracked tables
with one column deliberately left `timestamptz` and asserts `Unhealthy` with `driftedColumns`
populated — otherwise this single test is sufficient to close the task.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankSchemaHealthCheckTests"`

Expected: FAIL to compile — `PhotobankSchemaHealthCheck` does not exist yet.

- [ ] **Step 3: Write the minimal implementation**

Create `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs`:

```csharp
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
```

**Implementation note:** if `Database.SqlQuery<PhotobankColumnTypeRow>` does not compile against this
repo's exact EF Core 8.0.8 patch (some 8.x point releases require the row type be `public` and
top-level, not a `private sealed` nested class, to satisfy the keyless-type discovery `SqlQuery<T>`
relies on) — first try making `PhotobankColumnTypeRow` a `public` top-level class in the same file.
If it still does not compile, fall back to `_db.Database.GetDbConnection()` + a raw `NpgsqlCommand`
executing the same SQL text (matching the query text exactly; only the execution mechanism changes).
Either satisfies this task — do not skip the drift-detection logic itself if the first mechanism
doesn't compile.

- [ ] **Step 4: Register the health check**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, inside
`AddHealthCheckServices`, add to the existing `healthChecksBuilder` chain (immediately after the
`.AddCheck<DataQualitySchemaHealthCheck>(...)` call):

```csharp
            .AddCheck<Anela.Heblo.API.HealthChecks.Photobank.PhotobankSchemaHealthCheck>(
                name: "photobank-schema",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db", "schema" })
```

(Use the fully-qualified type name as shown, or add a `using Anela.Heblo.API.HealthChecks.Photobank;`
at the top of the file and reference `PhotobankSchemaHealthCheck` directly — match whichever style
the file already uses for `DataQualitySchemaHealthCheck`'s `using`.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankSchemaHealthCheckTests"`

Expected: PASS

- [ ] **Step 6: Full backend build to confirm the health check registration compiles**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs backend/test/Anela.Heblo.Tests/API/HealthChecks/Photobank/PhotobankSchemaHealthCheckTests.cs
git commit -m "feat(photobank): add read-only schema-drift health check for Photobank DateTime columns"
```

---
