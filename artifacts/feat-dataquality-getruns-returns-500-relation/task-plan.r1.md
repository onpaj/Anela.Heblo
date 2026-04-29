### task: add-data-quality-schema-health-check-class

**Goal:** Create the `DataQualitySchemaHealthCheck` `IHealthCheck` implementation that probes the `DqtRuns` table via EF Core and returns structured failure data when the relation is missing.

**Context:**

The `Data Quality Runs` feature is returning HTTP 500 because EF Core targets a relation (`dqt_runs`) that no longer matches the current code mapping. The current authoritative mapping is in `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs` and reads `builder.ToTable("DqtRuns", "public")`.

The architectural decision is:

- The check is registered as an ASP.NET Core `IHealthCheck` and exposed under the `ready` tag (NOT a startup self-check, NOT a request middleware).
- The probe query MUST be `await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken)`. Rationale: this exercises the full EF Core mapping → SQL generation → relation resolution path that produced the bug. `Take(1).ToListAsync()` materializes rows; raw SQL or `information_schema` queries bypass the EF mapping and would not catch a future *mapping*-only drift.
- On `Npgsql.PostgresException` with `SqlState == "42P01"`, return `HealthCheckResult.Unhealthy` with structured `data`:
  ```
  entity=DqtRun, expectedTable=DqtRuns, schema=public, sqlState=42P01
  ```
- On any other exception, return `HealthCheckResult.Unhealthy` with the raw exception (no structured `data` beyond the framework default).
- The check MUST NOT throw. It MUST always return a `HealthCheckResult`.
- The check is scoped to `DqtRuns` only for this PR. Broader coverage of all tables touched by `StandardizeTableNamingToPascalCase` is explicitly out of scope and tracked as a follow-up.
- The class lives in the API layer (Health checks are an API-layer concern that expose HTTP endpoints), inside the Data Quality vertical slice.
- The DbContext that owns `DqtRuns` is `ApplicationDbContext` in `Anela.Heblo.Persistence`. Confirm by inspecting the existing `BackgroundServicesReadyHealthCheck` registration to see which context type is injected for DB-backed checks; use the same context type.

NFR budget: probe overhead < 50 ms in healthy path. `AnyAsync` on a small/indexed table compiles to `SELECT EXISTS (SELECT 1 FROM "DqtRuns" LIMIT 1)` and meets this trivially.

**Files to create/modify:**

- `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs` — new file containing the `IHealthCheck` implementation.

**Implementation steps:**

1. Locate the existing health check folder. If `backend/src/Anela.Heblo.API/HealthChecks/` already exists (it does — `BackgroundServicesReadyHealthCheck` lives in this area per the design), create the subfolder `HealthChecks/DataQuality/` next to it. If the existing convention places health checks directly under `HealthChecks/` without subfolders, follow that convention instead and place the file at `HealthChecks/DataQualitySchemaHealthCheck.cs`. Match the existing style.

2. Open `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs` and confirm the EF mapping reads `builder.ToTable("DqtRuns", "public")`. If it does not, STOP and escalate — the assumption underlying this entire task has changed.

3. Find the existing `ApplicationDbContext` (search the `Anela.Heblo.Persistence` project for the class declaration deriving from `DbContext`). Confirm it exposes a `DbSet<DqtRun> DqtRuns` property. Note its full namespace for the `using` directive.

4. Create the new file `DataQualitySchemaHealthCheck.cs` with this exact content (adjust namespace to match the folder location and adjust `using Anela.Heblo.Persistence;` to the actual namespace of `ApplicationDbContext`):

   ```csharp
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
   ```

5. Verify the project's csproj for `Anela.Heblo.API` already has access to `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.EntityFrameworkCore`, and `Npgsql`. These are already used elsewhere in the API/Persistence projects; no package additions should be necessary. If the API project does not already reference `Npgsql`, add the package reference using the same version as in `Anela.Heblo.Persistence` (consult `Directory.Packages.props` if Central Package Management is in use).

6. Run `dotnet build` from `backend/` to confirm the file compiles. Run `dotnet format` to enforce 4-space indentation and Allman braces (project rule).

**Tests to write:**

No tests in this task — they are added in a later task. This task only delivers the class and a successful build.

**Acceptance criteria:**

- File `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs` exists (or matches the existing health-check folder convention).
- The class is `public sealed`, implements `IHealthCheck`, has a single constructor accepting `ApplicationDbContext`.
- The probe query is exactly `await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken)`.
- The catch chain is: (1) `PostgresException` with `SqlState == "42P01"` → `Unhealthy` with `data` containing `entity`, `expectedTable`, `schema`, `sqlState`; (2) `Exception` → `Unhealthy` with raw exception.
- The class never throws from `CheckHealthAsync`.
- `dotnet build` succeeds.
- `dotnet format --verify-no-changes` passes for the new file.

---

### task: register-data-quality-schema-health-check

**Goal:** Register `DataQualitySchemaHealthCheck` in the application's existing health-check pipeline so that `/health/ready` polls it under the `ready` tag and Azure App Service treats schema drift as an unhealthy instance.

**Context:**

The repo already wires ASP.NET Core health checks via the extension method `ServiceCollectionExtensions.AddHealthCheckServices()` (this is where `BackgroundServicesReadyHealthCheck` is registered). The `/health/ready` endpoint already exists with the predicate `check => check.Tags.Contains("db") || check.Tags.Contains("ready")`, and the response writer is `UIResponseWriter.WriteHealthCheckUIResponse`, which serializes structured `data` dictionaries into the JSON 503 body automatically.

Because of these existing facts:

- DO NOT add a new `MapHealthChecks` call. `/health/ready` already exists.
- DO NOT add `.AllowAnonymous()` — the project does not configure a `FallbackPolicy`, so health endpoints are already implicitly anonymous.
- DO NOT create a new `DataQualityModule.cs` for registration — the existing module structure registers health checks centrally in `AddHealthCheckServices()`.

The check must be registered with both `ready` AND `db` tags so it matches the existing predicate exactly. Use `failureStatus: HealthStatus.Unhealthy` (NOT `Degraded`) so Azure removes the instance from rotation rather than serving traffic that will 500.

The registration call shape:

```csharp
services.AddHealthChecks()
    .AddCheck<DataQualitySchemaHealthCheck>(
        name: "data-quality-schema",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "db", "schema" });
```

If `AddHealthChecks()` is already called in `AddHealthCheckServices()` (it is — `BackgroundServicesReadyHealthCheck` is registered there), reuse the same chained builder; do not call `AddHealthChecks()` twice.

`DataQualitySchemaHealthCheck` is registered as transient by default by `AddCheck<T>()`. The injected `ApplicationDbContext` is already DI-scoped per request/probe — no extra registration required.

**Files to create/modify:**

- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (or wherever `AddHealthCheckServices` is defined — locate by searching for the method name) — add the `AddCheck<DataQualitySchemaHealthCheck>` call.

**Implementation steps:**

1. Search the `Anela.Heblo.API` project for the method `AddHealthCheckServices`. It is the existing extension method that already registers `BackgroundServicesReadyHealthCheck`. Open the file.

2. Locate the `services.AddHealthChecks()` chain (or the `IHealthChecksBuilder` variable it returns). Append a new `.AddCheck<DataQualitySchemaHealthCheck>(...)` call to the chain:

   ```csharp
   .AddCheck<DataQualitySchemaHealthCheck>(
       name: "data-quality-schema",
       failureStatus: HealthStatus.Unhealthy,
       tags: new[] { "ready", "db", "schema" })
   ```

   Place it immediately after the existing `AddCheck<BackgroundServicesReadyHealthCheck>` registration to keep similar checks together.

3. Add the `using Anela.Heblo.API.HealthChecks.DataQuality;` directive to the top of the file (adjust to match the actual namespace produced in the previous task). Add `using Microsoft.Extensions.Diagnostics.HealthChecks;` if not already present.

4. Confirm `ApplicationDbContext` is already registered as a scoped service in DI (it must be, since other handlers depend on it). No DI changes are required.

5. Run `dotnet build` from `backend/` to confirm compilation. Run `dotnet format`.

6. Manual verification (no automated test in this task): run the API locally (`dotnet run --project backend/src/Anela.Heblo.API`) and `curl https://localhost:5001/health/ready`. Expect HTTP 200 and a JSON body containing an entry with `name: "data-quality-schema"` and `status: "Healthy"`.

**Tests to write:**

No automated tests in this task. Verification is manual via local `curl`. Automated tests for the health check itself are added in the next task.

**Acceptance criteria:**

- `AddHealthCheckServices` (or the equivalent extension) contains `.AddCheck<DataQualitySchemaHealthCheck>(name: "data-quality-schema", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready", "db", "schema" })`.
- No second `AddHealthChecks()` call is introduced; the registration is appended to the existing builder chain.
- No new `MapHealthChecks` call is introduced; no changes to `Program.cs` routing.
- No `AllowAnonymous` or `FallbackPolicy` change is introduced.
- `dotnet build` succeeds.
- Local `curl https://localhost:5001/health/ready` returns 200 with a `data-quality-schema` entry showing `status: "Healthy"` (assuming the local DB has `DqtRuns`).
- `dotnet format --verify-no-changes` passes.

---

### task: add-data-quality-schema-health-check-tests

**Goal:** Add unit tests proving that `DataQualitySchemaHealthCheck` returns `Healthy` when the `DqtRuns` relation is reachable, returns `Unhealthy` with structured `data` when the relation is missing (`PostgresException` SQLSTATE `42P01`), and returns `Unhealthy` for any other exception.

**Context:**

The behavioral contract under test is:

| Scenario | EF Core behavior | Expected `HealthCheckResult` |
|----------|------------------|------------------------------|
| Healthy steady state | `DqtRuns.AnyAsync(ct)` returns `false` (or `true`) | `Status == Healthy`, description `"DataQuality schema is reachable"` |
| Schema drift (target signal) | `AnyAsync(ct)` throws `PostgresException` with `SqlState == "42P01"` | `Status == Unhealthy`, `Data["entity"] == "DqtRun"`, `Data["expectedTable"] == "DqtRuns"`, `Data["schema"] == "public"`, `Data["sqlState"] == "42P01"`, exception is the thrown `PostgresException` |
| Other DB error | `AnyAsync(ct)` throws e.g. `InvalidOperationException` or a `PostgresException` with another SQLSTATE | `Status == Unhealthy`, exception is the thrown one, no `Data` keys required (raw exception path) |
| Cancellation | `AnyAsync(ct)` is invoked with the cancellation token | The token is passed through to `AnyAsync` |

The class to test:

```csharp
public sealed class DataQualitySchemaHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;
    public DataQualitySchemaHealthCheck(ApplicationDbContext db) => _db = db;
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("DataQuality schema is reachable");
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") { /* Unhealthy with data */ }
        catch (Exception ex) { /* Unhealthy with raw exception */ }
    }
}
```

`ApplicationDbContext` is the EF Core context that owns `DbSet<DqtRun> DqtRuns`. To make `DqtRuns.AsNoTracking().AnyAsync(...)` throw a controlled exception in a unit test, subclass `ApplicationDbContext` and override `DqtRuns` to return a test `DbSet<DqtRun>` whose async query throws.

The cleanest mechanism in this codebase is to use the EF Core in-memory provider for the healthy path (no third-party packages needed) and to use a thin test subclass that overrides the `DqtRuns` property for the throwing paths. EF Core allows overriding a `DbSet<T>` property in a derived `DbContext`.

The test project is `backend/test/Anela.Heblo.Tests`. Tests for API health checks belong under `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/`.

The `DqtRun` entity is defined under the Domain layer (locate the class via the existing `DbSet<DqtRun> DqtRuns` property on the context); use the same entity type in tests with whatever required properties have defaults or can be set freely.

**Files to create/modify:**

- `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/DataQualitySchemaHealthCheckTests.cs` — new file containing three xUnit tests.

**Implementation steps:**

1. Create the folder `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/` if it does not exist.

2. Confirm the test project already references `Microsoft.EntityFrameworkCore.InMemory`. Inspect `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (or the equivalent Central Package Management entries) — most .NET test setups in this kind of project already include it. If not present, add it using the same EF Core version used elsewhere.

3. Create the file `DataQualitySchemaHealthCheckTests.cs` with this exact content (adjust namespaces as needed to match the actual `ApplicationDbContext` and `DqtRun` namespaces in the codebase):

   ```csharp
   using System;
   using System.Threading;
   using System.Threading.Tasks;
   using Anela.Heblo.API.HealthChecks.DataQuality;
   using Anela.Heblo.Persistence;
   using Microsoft.EntityFrameworkCore;
   using Microsoft.Extensions.Diagnostics.HealthChecks;
   using Npgsql;
   using Xunit;

   namespace Anela.Heblo.Tests.API.HealthChecks.DataQuality;

   public class DataQualitySchemaHealthCheckTests
   {
       private static ApplicationDbContext CreateInMemoryContext()
       {
           var options = new DbContextOptionsBuilder<ApplicationDbContext>()
               .UseInMemoryDatabase(databaseName: $"dq-health-{Guid.NewGuid()}")
               .Options;
           return new ApplicationDbContext(options);
       }

       [Fact]
       public async Task CheckHealthAsync_WhenTableReachable_ReturnsHealthy()
       {
           // Arrange
           await using var db = CreateInMemoryContext();
           var sut = new DataQualitySchemaHealthCheck(db);

           // Act
           var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

           // Assert
           Assert.Equal(HealthStatus.Healthy, result.Status);
           Assert.Equal("DataQuality schema is reachable", result.Description);
       }

       [Fact]
       public async Task CheckHealthAsync_WhenTableMissing_ReturnsUnhealthyWith42P01Data()
       {
           // Arrange
           var thrown = CreatePostgresException("42P01", "relation \"DqtRuns\" does not exist");
           await using var db = new ThrowingDbContext(thrown);
           var sut = new DataQualitySchemaHealthCheck(db);

           // Act
           var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

           // Assert
           Assert.Equal(HealthStatus.Unhealthy, result.Status);
           Assert.Equal("DataQuality table not found", result.Description);
           Assert.Same(thrown, result.Exception);
           Assert.Equal("DqtRun", result.Data["entity"]);
           Assert.Equal("DqtRuns", result.Data["expectedTable"]);
           Assert.Equal("public", result.Data["schema"]);
           Assert.Equal("42P01", result.Data["sqlState"]);
       }

       [Fact]
       public async Task CheckHealthAsync_WhenOtherException_ReturnsUnhealthyWithRawException()
       {
           // Arrange
           var thrown = new InvalidOperationException("connection broken");
           await using var db = new ThrowingDbContext(thrown);
           var sut = new DataQualitySchemaHealthCheck(db);

           // Act
           var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

           // Assert
           Assert.Equal(HealthStatus.Unhealthy, result.Status);
           Assert.Equal("DataQuality probe failed", result.Description);
           Assert.Same(thrown, result.Exception);
       }

       private static PostgresException CreatePostgresException(string sqlState, string message)
       {
           // Construct PostgresException via reflection-free constructor available in Npgsql 6+:
           // public PostgresException(string messageText, string severity, string invariantSeverity, string sqlState)
           return new PostgresException(
               messageText: message,
               severity: "ERROR",
               invariantSeverity: "ERROR",
               sqlState: sqlState);
       }

       private sealed class ThrowingDbContext : ApplicationDbContext
       {
           private readonly Exception _toThrow;

           public ThrowingDbContext(Exception toThrow)
               : base(new DbContextOptionsBuilder<ApplicationDbContext>()
                   .UseInMemoryDatabase(databaseName: $"throw-{Guid.NewGuid()}")
                   .Options)
           {
               _toThrow = toThrow;
           }

           public override DbSet<TEntity> Set<TEntity>() => throw _toThrow;
       }
   }
   ```

   Note on `ThrowingDbContext`: overriding `Set<TEntity>()` to throw causes EF Core's resolution of `DqtRuns` to throw before any provider call. If the project's `ApplicationDbContext` exposes `DqtRuns` as a public property with a setter or via `Set<DqtRun>()` lookup, this approach works. If `DqtRuns` is exposed as a directly assigned `DbSet<DqtRun>` property without indirection through `Set<>`, refactor `ThrowingDbContext` to override `OnConfiguring` or expose a `new DbSet<DqtRun> DqtRuns` that throws on enumeration — verify the actual `ApplicationDbContext` shape and adjust.

   Alternate fallback if the override approach is incompatible with the actual `ApplicationDbContext`: use Testcontainers PostgreSQL to spin up a real DB, apply migrations, run the healthy test against it, then `DROP TABLE "DqtRuns"` and run the unhealthy test against it. This is heavier but unambiguously correct. Choose this fallback if `ThrowingDbContext` cannot intercept the call.

4. Run `dotnet test --filter "FullyQualifiedName~DataQualitySchemaHealthCheckTests"` from `backend/`. All three tests must pass.

5. Run `dotnet format` on the test file.

**Tests to write:**

Three xUnit `[Fact]` tests, exact names and behavior:

1. **`CheckHealthAsync_WhenTableReachable_ReturnsHealthy`**
   - Inputs: `ApplicationDbContext` with EF Core in-memory provider (no rows, no errors). `HealthCheckContext` is a default-constructed instance.
   - Expected: `result.Status == HealthStatus.Healthy`. `result.Description == "DataQuality schema is reachable"`.

2. **`CheckHealthAsync_WhenTableMissing_ReturnsUnhealthyWith42P01Data`**
   - Inputs: A `DbContext` whose `DqtRuns` access throws `PostgresException` with `SqlState == "42P01"`.
   - Expected: `result.Status == HealthStatus.Unhealthy`. `result.Description == "DataQuality table not found"`. `result.Exception` is the same `PostgresException` instance. `result.Data["entity"] == "DqtRun"`, `result.Data["expectedTable"] == "DqtRuns"`, `result.Data["schema"] == "public"`, `result.Data["sqlState"] == "42P01"`.

3. **`CheckHealthAsync_WhenOtherException_ReturnsUnhealthyWithRawException`**
   - Inputs: A `DbContext` whose `DqtRuns` access throws `InvalidOperationException("connection broken")`.
   - Expected: `result.Status == HealthStatus.Unhealthy`. `result.Description == "DataQuality probe failed"`. `result.Exception` is the same `InvalidOperationException` instance.

**Acceptance criteria:**

- File `backend/test/Anela.Heblo.Tests/API/HealthChecks/DataQuality/DataQualitySchemaHealthCheckTests.cs` exists.
- All three tests above are present, with the exact names listed.
- `dotnet test --filter "FullyQualifiedName~DataQualitySchemaHealthCheckTests"` passes (3/3 green).
- `dotnet format --verify-no-changes` passes for the test file.
- No real database is required for the unit tests (in-memory provider + throwing context).

---

### task: update-setup-doc-with-migration-runbook

**Goal:** Update `docs/development/setup.md` with a pre-deploy / post-deploy migration checklist, the post-fix verification step against `/health/ready`, and a reusable diagnostic SQL snippet for future schema-drift incidents.

**Context:**

The repo's CLAUDE.md and `docs/development/setup.md` both record that database migrations are **manual** in this project — they are not run automatically by deployment. This is a known operational risk; the `dqt_runs` / `DqtRuns` incident materialized that risk because `StandardizeTableNamingToPascalCase` was not (or possibly was not) applied to production at the time the new code was deployed.

The runbook update has three concrete additions, each addressed by a specific FR:

- **FR-6 pre-deploy checklist:** Before merging or deploying code that depends on a new migration, the operator must verify the migration has been applied to the target environment.
- **FR-6 post-deploy verification:** After deployment, the operator must hit the new `/health/ready` endpoint and confirm 200 with the `data-quality-schema` check `Healthy`.
- **FR-6 ordering hazard callout:** Explicitly call out the `AddDataQualityTables` → `StandardizeTableNamingToPascalCase` ordering as the canonical example.
- **FR-7 reusable diagnostic SQL:** Document the read-only diagnostic SQL pair from FR-1 in a parameterized form so it can be reused for any future schema-drift suspicion.

The diagnostic SQL pair is:

```sql
-- Migration history check (parameterize the LIKE patterns per incident)
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%DataQuality%'
   OR "MigrationId" LIKE '%StandardizeTable%'
ORDER BY "MigrationId";

-- Physical table existence check (parameterize the table names)
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND lower(table_name) IN ('dqt_runs', 'dqtruns');
```

The three documented diagnosis states (A/B/C) and their decision tree are already specified in the spec FR-1; reproduce them in the runbook so the runbook is self-contained.

NFR-2 forbids embedding production credentials in the runbook. Use placeholder language: "connect to the production database using your authorized read-only credentials".

The runbook must NOT prescribe automated migration in CI/CD (explicitly out of scope per the spec).

**Files to create/modify:**

- `docs/development/setup.md` — append a new section titled `## Database Migrations Runbook` (or similar; match the existing heading style in the file).

**Implementation steps:**

1. Open `docs/development/setup.md` and read the existing structure. Identify the most appropriate insertion point — either at the end of the file, or after any existing "Migrations" / "Database" section if one exists.

2. Append the following new section verbatim (adjust heading depth to match the file's existing convention — likely `##` if the file uses `#` for the title, or `###` if `##` is the section level):

   ```markdown
   ## Database Migrations Runbook

   Database migrations in this project are **manual**. They are not applied by the deployment pipeline. Code that depends on a new migration MUST NOT be deployed before the migration is applied to the target environment, or the application will return HTTP 500 with `Npgsql.PostgresException: 42P01: relation "<table>" does not exist`.

   ### Pre-deploy checklist

   Before merging or deploying code that introduces or depends on a new EF Core migration:

   1. Identify the migration(s) the new code depends on (look at `backend/src/Anela.Heblo.Persistence/Migrations/` and `dotnet ef migrations list`).
   2. Connect to the target environment's database using your authorized read-only credentials. Do NOT embed credentials in source control or scripts.
   3. Run the diagnostic SQL pair below (substitute the `LIKE` patterns and table names for the migration in question).
   4. Confirm the migration ID is present in `__EFMigrationsHistory` AND the expected post-migration physical schema is in place.
   5. If the migration is missing, apply it via the project's standard manual migration procedure BEFORE rolling out the dependent application code.

   ### Post-deploy verification

   After every deployment that touches schema or schema-mapping code:

   1. `curl https://<environment-host>/health/ready` (replace `<environment-host>` with the deployed environment URL).
   2. Confirm HTTP 200 and that the JSON body contains every health check with `status: "Healthy"`. In particular, confirm `data-quality-schema` is `Healthy`.
   3. If any check is `Unhealthy`, inspect the structured `data` field on that check (e.g., `entity`, `expectedTable`, `schema`, `sqlState`) — these point directly at the drift.
   4. If `data-quality-schema` reports `sqlState: "42P01"` with `expectedTable: "DqtRuns"`, the production DB is missing the `DqtRuns` rename. Apply `StandardizeTableNamingToPascalCase` (or the relevant pending migration) before allowing the instance back into rotation. Note that Azure App Service will already have removed the unhealthy instance from rotation automatically.

   ### Ordering hazard

   The pair `AddDataQualityTables` (creates `dqt_runs`) → `StandardizeTableNamingToPascalCase` (renames to `DqtRuns`) is the canonical example of an ordering hazard: deploying the application code that depends on the rename before applying the rename migration produces user-facing 500s. Always confirm the most recent dependent migration is applied before deploying its consumer code.

   ### Diagnostic SQL for suspected schema drift

   When you suspect a "relation does not exist" error is caused by code/DB migration drift, use this read-only diagnostic SQL pair. Substitute the `LIKE` patterns and table names for the suspect entity.

   Migration history check:

   ```sql
   SELECT "MigrationId", "ProductVersion"
   FROM "__EFMigrationsHistory"
   WHERE "MigrationId" LIKE '%<migration-fragment-1>%'
      OR "MigrationId" LIKE '%<migration-fragment-2>%'
   ORDER BY "MigrationId";
   ```

   Physical table existence check:

   ```sql
   SELECT table_schema, table_name
   FROM information_schema.tables
   WHERE table_schema = 'public'
     AND lower(table_name) IN ('<old-name-lower>', '<new-name-lower>');
   ```

   Interpret the combined output as one of three states:

   - **State A** — both expected migrations present in history AND only the new (post-rename) physical table exists → code and DB are consistent. Investigate stale application instances; perform a rolling restart of Azure App Service.
   - **State B** — only the older migration present in history AND only the old physical table exists → the rename migration is unapplied. Apply it via the standard manual procedure. Inverse rollback (if ever needed) for a metadata-only rename is `ALTER TABLE "<NewName>" RENAME TO <old_name>;` — note this restores the table name but does not undo any other changes a multi-step migration may have included.
   - **State C** — both migrations present in history but the old physical table still exists (or both exist) → manual intervention required. Do not attempt automated remediation. Escalate.

   These diagnostic queries are read-only and safe to run against any environment.
   ```

3. If any existing section in `setup.md` already partially covers migrations, integrate by reference rather than duplicating; the goal is one canonical runbook section.

4. Verify the file still renders (open in a Markdown preview if available) and that no other sections were broken.

**Tests to write:**

No automated tests. Documentation is verified by review.

**Acceptance criteria:**

- `docs/development/setup.md` contains a new section (heading text contains `Migration` and `Runbook`).
- The section contains four subsections: pre-deploy checklist, post-deploy verification, ordering hazard, and diagnostic SQL.
- The pre-deploy checklist explicitly states migrations are manual and must be applied before deploying dependent code.
- The post-deploy verification subsection references `/health/ready` and the `data-quality-schema` check by name.
- The ordering hazard subsection names `AddDataQualityTables` and `StandardizeTableNamingToPascalCase` explicitly.
- The diagnostic SQL subsection contains both queries (history + information_schema), parameterized with placeholder fragments, and documents the A/B/C state interpretation.
- No production credentials, connection strings, or hostnames are embedded.
- No prescription of automated CI/CD migrations is introduced.

---

### task: write-incident-postmortem-in-memory-gotchas

**Goal:** Add a postmortem note in `memory/gotchas/` documenting the `dqt_runs` / `DqtRuns` incident — root cause confirmed, action taken, evidence, and the durable safeguard added (the readiness probe).

**Context:**

The repo's `memory/` directory is the cross-session knowledge base. `memory/gotchas/` is reserved for "bugs, edge cases, and hard-won lessons" per `CLAUDE.md`. The spec's FR-4 requires a summary be appended to `memory/gotchas/` after the verification window passes; FR-1 requires findings to be recorded in `memory/context/state.md` and (if novel) `memory/gotchas/`.

The lesson is novel for this codebase (first observed instance of EF migration / code drift causing user-facing 500s), so it belongs in `memory/gotchas/`.

The note should record:

- The symptom (HTTP 500 spike on `GET /api/data-quality/runs`, `Npgsql.PostgresException: 42P01: relation "dqt_runs" does not exist`).
- The root cause class (code/DB drift between deployed image's EF mapping and the physical schema in production).
- The diagnostic procedure that resolved it (the SQL pair from FR-1, now codified in `docs/development/setup.md`).
- The durable safeguard added (`DataQualitySchemaHealthCheck` registered under `/health/ready`).
- The known limitation of the safeguard (it covers `DqtRuns` only — broader coverage is a tracked follow-up).
- The migration ordering hazard (`AddDataQualityTables` → `StandardizeTableNamingToPascalCase`).

The note must NOT contain credentials, hostnames, or any production secrets.

The actual operational outcomes (which of states A/B/C occurred, the migration apply timestamp, the restart timestamp, the verification window evidence links) are filled in by the operator after FR-1 through FR-4 execute. Provide template placeholders that the operator fills in post-incident.

**Files to create/modify:**

- `memory/gotchas/ef-migration-codebase-drift.md` — new file.

**Implementation steps:**

1. Confirm `memory/gotchas/` exists. If it does not, create it.

2. Create `memory/gotchas/ef-migration-codebase-drift.md` with this exact content:

   ```markdown
   # EF Core migration / codebase drift causes 42P01 in production

   ## Symptom

   `GET /api/data-quality/runs` returns HTTP 500 at multiples of baseline rate. Application logs show:

   ```
   Npgsql.PostgresException: 42P01: relation "dqt_runs" does not exist
   ```

   Slow-request telemetry on the same endpoint shows latencies in the 3,000–4,000 ms range (timeout-adjacent), suggesting the query never reaches the row-fetch phase.

   ## Root cause class

   Drift between the deployed application image's EF Core mapping and the physical schema in the production PostgreSQL database. In this repo, database migrations are **manual** — they are not applied by the deployment pipeline. Two failure modes are possible:

   1. The dependent migration was never applied to production while the dependent application code was deployed.
   2. The migration was applied but one or more Azure App Service container instances continued serving an older image whose mapping referenced the pre-rename table name.

   Either mode produces the same `42P01` symptom.

   ## Concrete instance

   - Migration `20260424060451_AddDataQualityTables` created the table as `dqt_runs` (snake_case).
   - Migration `20260424142720_StandardizeTableNamingToPascalCase` renamed it to `DqtRuns` (PascalCase).
   - The deployed `DqtRunConfiguration` maps to `DqtRuns`. Production was observed to either (a) not have applied the rename, or (b) be serving stale application instances with the pre-rename mapping.

   ## Diagnostic procedure

   Use the read-only SQL pair documented in `docs/development/setup.md` under "Diagnostic SQL for suspected schema drift". The pair queries `__EFMigrationsHistory` and `information_schema.tables` to determine which of three states (A: code/DB consistent, B: migration unapplied, C: anomalous) actually holds.

   ## Durable safeguard

   `DataQualitySchemaHealthCheck` is registered under `/health/ready` with the `ready`, `db`, and `schema` tags. It executes `_db.DqtRuns.AsNoTracking().AnyAsync(ct)` on every readiness poll. If the relation is missing, the check returns `HealthStatus.Unhealthy` with structured `data` (`entity`, `expectedTable`, `schema`, `sqlState`), Azure App Service removes the instance from rotation, and on-call sees a structured drift signal instead of a 500 spike.

   ## Known limitation of the safeguard

   The probe currently covers `DqtRuns` only. It does NOT cover the other tables touched by `StandardizeTableNamingToPascalCase`. Broader coverage is tracked as a follow-up; do not assume the probe protects against drift on any other entity.

   ## Operator-filled incident record

   Fill in below once FR-1 through FR-4 are complete:

   - Diagnosis state (A / B / C):
   - Migration apply timestamp (UTC), if any:
   - Rolling restart timestamp (UTC), if any:
   - Verification window start (UTC):
   - Verification window end (UTC):
   - Final 500 count on `GET /api/data-quality/runs` during verification window: 0 (required) / actual:
   - Final `42P01` count in logs during verification window: 0 (required) / actual:
   - Slow-request p95 on the endpoint during verification window (target < 1,000 ms):
   - Pre-restart container image digest:
   - Image digest verified to match the commit containing `DqtRunConfiguration.ToTable("DqtRuns", "public")`: yes / no
   - Telemetry / dashboard links (no credentials):

   ## Lesson

   Manual migrations + traffic-serving deployments require an explicit deploy-time gate, not a runtime gate. The readiness-probe safeguard is the minimum correct response: it does not automate migrations (out of scope, larger initiative) but it converts a user-facing 500 into a platform-managed instance removal. The next migration with a similar shape (rename, schema move) MUST be paired with the pre-deploy checklist in `docs/development/setup.md`.
   ```

3. Confirm the file is committed with no embedded production credentials, hostnames, or secrets.

**Tests to write:**

None. This is a documentation/memory artifact.

**Acceptance criteria:**

- File `memory/gotchas/ef-migration-codebase-drift.md` exists.
- The file contains all the sections listed above (Symptom, Root cause class, Concrete instance, Diagnostic procedure, Durable safeguard, Known limitation, Operator-filled incident record, Lesson).
- The Concrete instance section names both `20260424060451_AddDataQualityTables` and `20260424142720_StandardizeTableNamingToPascalCase` and `DqtRunConfiguration`.
- The Durable safeguard section names `DataQualitySchemaHealthCheck`, `/health/ready`, and the `42P01` SQLSTATE explicitly.
- The Operator-filled incident record contains placeholder fields for all FR-4 verification metrics (zero 500s, zero `42P01`, p95 < 1,000 ms) so the operator can record actuals.
- No production credentials, hostnames, or secrets are present.