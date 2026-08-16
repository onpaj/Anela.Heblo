# Photobank DateTime Kind=Unspecified Regression — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the recurring `PhotobankIndexJob` `DateTime Kind=Unspecified` exception (unchanged
since PR #3743) by defensively normalizing the one externally-sourced Photobank timestamp, adding
a read-only health check that makes physical schema drift observable, and closing the test/doc gaps
that let this regression recur silently.

**Architecture:** Backend-only. One `IHealthCheck` addition mirroring the existing
`DataQualitySchemaHealthCheck` pattern; one defensive `DateTime.SpecifyKind` fix in
`PhotobankIndexJob`; two test additions; two documentation updates. No schema changes, no new
migrations, no new endpoints.

**Tech Stack:** .NET 8, EF Core 8.0.8, Npgsql, xUnit, FluentAssertions, Moq.

---

### task: normalize-photo-modifiedat-utc-kind

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:181`
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `PhotobankIndexJobTests.cs` (same file, same mocking pattern as the existing tests
in that class — copy the Arrange block from `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` and
adjust only what's shown):

```csharp
[Fact]
public async Task UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc()
{
    // Arrange — simulate System.Text.Json handing back a DateTime with Kind=Unspecified for
    // the Graph delta item's lastModifiedDateTime (the one Photobank DateTime value sourced
    // from something other than DateTime.UtcNow). Photo.ModifiedAt must never inherit that
    // Kind as-is: PhotobankRootRepository/PhotobankPhotoRepository share one ApplicationDbContext
    // whose global convention strips Kind before every write, so the column-type mapping is
    // what actually determines success/failure — but this test only needs to prove the
    // application-layer contract: the assigned Kind is always Utc, regardless of the source's Kind.
    var unspecifiedInstant = new DateTime(2026, 7, 27, 1, 28, 0, DateTimeKind.Unspecified);

    var root = new PhotobankIndexRoot
    {
        Id = 1,
        SharePointPath = "/sites/test/photos",
        DriveId = "drive-1",
        RootItemId = "root-item-1",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    var photoItem = new GraphPhotoItem
    {
        ItemId = "file-kind-test",
        Name = "photo.jpg",
        FolderPath = "Fotky/Produkty",
        WebUrl = "https://sharepoint.example.com/photo.jpg",
        FileSizeBytes = 1024,
        LastModifiedAt = unspecifiedInstant,
        DriveId = "drive-1",
        IsDeleted = false,
    };

    _rootRepoMock
        .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync([root]);

    _tagRuleRepoMock
        .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TagRule>());

    _photoRepoMock
        .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-kind-test", It.IsAny<CancellationToken>()))
        .ReturnsAsync((Photo?)null);

    Photo? capturedPhoto = null;
    _photoRepoMock
        .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
        .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
        .Returns(Task.CompletedTask);

    _photoTagRepoMock
        .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);

    _photoTagRepoMock
        .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    _photoRepoMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _photoTagRepoMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _rootRepoMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    _graphServiceMock
        .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GraphDeltaResult
        {
            Items = [photoItem],
            NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
        });

    // Act
    await _job.ExecuteAsync();

    // Assert
    capturedPhoto.Should().NotBeNull();
    capturedPhoto!.ModifiedAt.Kind.Should().Be(DateTimeKind.Utc);
    capturedPhoto.ModifiedAt.Should().Be(DateTime.SpecifyKind(unspecifiedInstant, DateTimeKind.Utc));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests.UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc"`

Expected: FAIL — `capturedPhoto.ModifiedAt.Kind` is `DateTimeKind.Unspecified` (the current code
assigns `item.LastModifiedAt` as-is).

- [ ] **Step 3: Write the minimal implementation**

In `PhotobankIndexJob.cs`, replace line 181:

```csharp
photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;
```

with:

```csharp
photo.ModifiedAt = item.LastModifiedAt.HasValue
    ? DateTime.SpecifyKind(item.LastModifiedAt.Value, DateTimeKind.Utc)
    : DateTime.UtcNow;
```

Do not change any other line in `UpsertPhotoBatchAsync` or elsewhere in the file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests.UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc"`

Expected: PASS

- [ ] **Step 5: Run the full PhotobankIndexJobTests fixture to confirm no regression**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests"`

Expected: PASS (all existing tests in this file continue to pass — this change only affects the
value assigned to `ModifiedAt.Kind`, not any field value asserted by existing tests, since existing
tests use `DateTime.UtcNow`, which is already `Kind=Utc` and unaffected by `SpecifyKind`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs
git commit -m "fix(photobank): stamp Photo.ModifiedAt as Kind=Utc when sourced from Graph delta items"
```

---

### task: photobank-phototag-schema-regression-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs`

- [ ] **Step 1: Write the failing (currently-nonexistent) test**

Add a new test class to the bottom of `PhotoSchemaTests.cs` (same file, reuse the existing
`NewNpgsqlContext()` private helper already defined in `PhotoSchemaTests`):

```csharp
[Theory]
[InlineData(nameof(PhotoTag.CreatedAt))]
public void PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone(string propertyName)
{
    using var db = NewNpgsqlContext();

    var property = db.Model
        .FindEntityType(typeof(PhotoTag))!
        .FindProperty(propertyName)!;

    property.GetColumnType().Should().Be(
        "timestamp",
        $"{propertyName} stores UTC and must map to 'timestamp without time zone' to match the " +
        "global UTC->Unspecified converter; 'timestamp with time zone' rejects Unspecified writes");
}
```

Add this as a new `[Theory]` method inside the existing `PhotoSchemaTests` class (do not create a
new class — `PhotoTag` is in the same `Anela.Heblo.Domain.Features.Photobank` namespace already
`using`'d at the top of this file).

- [ ] **Step 2: Run test to verify it passes immediately**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotoSchemaTests.PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone"`

Expected: PASS immediately — `PhotoTagConfiguration.cs` already calls `.AsUtcTimestamp()` on
`CreatedAt` (verified during spec/arch-review research). This test is a **regression guard**, not a
fix — it exists so a future change that removes that mapping fails CI immediately, the same
protective role `PhotoSchemaTests`'s existing theories already play for `Photo` and
`PhotobankIndexRoot`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs
git commit -m "test(photobank): add PhotoTag.CreatedAt to the timestamp-without-timezone regression guard"
```

---

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

### task: photobank-drift-runbook-docs

**Files:**
- Modify: `docs/development/setup.md` (the existing "Diagnostic SQL for suspected schema drift" section)
- Modify: `memory/gotchas/ef-migration-codebase-drift.md` (the existing "Known limitation" section)

- [ ] **Step 1: Extend the diagnostic-SQL section in `docs/development/setup.md`**

Find the existing "Diagnostic SQL for suspected schema drift" section (introduced for the `DqtRuns`
rename incident — table-existence drift). Immediately after its closing "These diagnostic queries
are read-only and safe to run against any environment." line, append this new subsection:

```markdown
### Photobank column-type drift (distinct from the table-rename case above)

The `DqtRuns` case above is a *table-existence* drift (a table was renamed). Photobank's regression
(#3757, following #3444/#3330) is a *column-type* drift instead: a `DateTime` column mapped as
`timestamp` (without time zone) in the EF model can still be `timestamp with time zone` physically,
if its converting migration was never applied to a given environment. Use this query pair instead of
the table-existence pair above when investigating a repeat of
`System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'`
from `PhotobankIndexJob`:

Migration history check:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%AlignPhotoTimestampsWithoutTimeZone%'
   OR "MigrationId" LIKE '%AlignPhotobankIndexRootTimestampWithoutTimeZone%'
ORDER BY "MigrationId";
```

Physical column-type check:

```sql
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema = 'public'
  AND ((table_name = 'Photos' AND column_name IN ('TakenAt','IndexedAt','ModifiedAt','LastAutoTaggedAt'))
    OR (table_name = 'PhotobankIndexRoots' AND column_name IN ('CreatedAt','LastIndexedAt'))
    OR (table_name = 'PhotoTags' AND column_name = 'CreatedAt'));
```

Interpret: every listed migration present in history AND every listed column reporting
`timestamp without time zone` → code and DB are consistent (the exception, if still occurring, is
not a schema-drift issue — look at the actual failing parameter in a live trace instead). Any
migration missing from history, or any column still reporting `timestamp with time zone` → drift;
apply the missing migration via the standard manual procedure. This exact check is also automated at
runtime by `PhotobankSchemaHealthCheck` under `GET /health/ready` (tags `ready`, `db`, `schema`) —
prefer checking that endpoint first before running this SQL by hand.
```

- [ ] **Step 2: Update the "Known limitation" note in `memory/gotchas/ef-migration-codebase-drift.md`**

Find the existing "Known limitation of the safeguard" section (currently ends with: "Broader
coverage is tracked as a follow-up; do not assume the probe protects against drift on any other
entity."). Append this sentence to that section (do not remove or rewrite the existing text — only
append):

```markdown

Photobank's `DateTime` columns (`Photos`, `PhotobankIndexRoots`, `PhotoTags` — see #3757) are now
covered by a sibling safeguard, `PhotobankSchemaHealthCheck` (registered as `photobank-schema` under
`/health/ready`), for the column-type-drift variant of this failure class (as opposed to this file's
own table-existence variant). Other tables remain uncovered.
```

- [ ] **Step 3: Commit**

```bash
git add docs/development/setup.md memory/gotchas/ef-migration-codebase-drift.md
git commit -m "docs(photobank): extend schema-drift diagnostic runbook to cover Photobank column-type drift"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (schema-drift health check) → `photobank-schema-drift-health-check` task. Covered.
- FR-2 (defensive `ModifiedAt` Kind normalization) → `normalize-photo-modifiedat-utc-kind` task. Covered.
- FR-3 (regression test gap: `PhotoTag.CreatedAt` schema test + `ModifiedAt` Kind mapping test) →
  split across `photobank-phototag-schema-regression-test` and
  `normalize-photo-modifiedat-utc-kind` (Step 1 of that task). Covered.
- FR-4 (diagnostic runbook extension) → `photobank-drift-runbook-docs` task. Covered.
- NFR-1 (performance: reuse existing DbContext/pool) → satisfied by construction (health check takes
  `ApplicationDbContext` via DI, same as `DataQualitySchemaHealthCheck`).
- NFR-2 (no production writes from new code) → satisfied by construction (health check query is
  `SELECT`-only against `information_schema`; no migration is applied by any task here).

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" placeholders — every step above
either contains complete code or an explicit, concrete fallback instruction (e.g. the `SqlQuery<T>`
compile-fallback note) rather than a vague deferral.

**3. Type consistency:** `PhotobankSchemaHealthCheck` constructor signature
(`ApplicationDbContext db`) is used identically in its implementation (Step 3) and its test (Step 1);
the registration call (Step 4) references the same type name. `Photo.ModifiedAt`'s type (`DateTime`,
non-nullable) is consistent between the implementation fix and the test assertion
(`capturedPhoto.ModifiedAt.Kind`).
