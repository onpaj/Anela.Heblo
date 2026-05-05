# Architecture Review: Fix `GET /api/data-quality/runs` 500 Errors (Missing `dqt_runs` Relation)

## Architectural Fit Assessment

This is primarily an **operational incident with a small architectural addition** (a deploy-time schema sanity check), not a feature build. The fit is straightforward:

- **Code/DB drift is a known, accepted risk** in this repo — CLAUDE.md explicitly states database migrations are manual. The architecture has no automated migration gate, so the FR-5 smoke check is the *right architectural response*: not to automate migrations (out of scope, large initiative), but to **fail-fast at readiness** when schema and code disagree.
- **Integration points**:
  1. `Program.cs` / `API` startup — where the readiness probe is registered.
  2. `Anela.Heblo.Persistence` — the `ApplicationDbContext` (or equivalent) that the probe queries through.
  3. ASP.NET Core Health Checks (`Microsoft.Extensions.Diagnostics.HealthChecks`) — the framework the probe must plug into. The repo already references this convention indirectly via `/health` patterns common in Azure App Service deployments; the architect should confirm whether `AddHealthChecks()` is already wired.
  4. Azure App Service container — `/health/ready` is the natural endpoint for the platform's readiness probe.
- **No domain model, no API contract, no UI changes** — this stays cleanly in the Persistence/API layers and doesn't touch Domain or Application. That is the correct boundary.
- **Vertical slice respected**: the smoke check belongs to the Data Quality slice (`API/HealthChecks/DataQuality*` or co-located with the DQ feature), not in a cross-cutting "infrastructure" bucket. This is consistent with the repo's vertical-slice rule.

The single architectural decision worth deliberating is **where the smoke check lives and what it covers** (FR-5). Everything else is operational execution.

## Proposed Architecture

### Component Overview

```
                ┌─────────────────────────────────────────────┐
                │ Azure Web App for Containers                │
                │  ┌─────────────────────────────────────┐    │
                │  │ ASP.NET Core Pipeline                │    │
                │  │                                       │    │
   /health/live │  │  ┌──────────────┐                    │    │
   ────────────►│  │  │ Liveness     │ (process up)       │    │
                │  │  └──────────────┘                    │    │
                │  │                                       │    │
   /health/ready│  │  ┌──────────────────────────────┐    │    │
   ────────────►│  │  │ Readiness                     │    │    │
                │  │  │  ├─ DbConnectivity            │    │    │
                │  │  │  └─ DataQualitySchemaCheck ◄──┼────┼────┼─── NEW (FR-5)
                │  │  └──────────────────────────────┘    │    │
                │  │           │                           │    │
                │  │           ▼                           │    │
                │  │  ┌──────────────────────────────┐    │    │
                │  │  │ ApplicationDbContext          │    │    │
                │  │  │   .DqtRuns.Take(1)            │    │    │
                │  │  └──────────────────────────────┘    │    │
                │  └─────────────────┬─────────────────────┘    │
                │                    │                           │
                └────────────────────┼───────────────────────────┘
                                     │
                                     ▼
                  ┌──────────────────────────────┐
                  │ PostgreSQL (Azure)            │
                  │  __EFMigrationsHistory        │
                  │  public."DqtRuns"             │
                  └──────────────────────────────┘

   Operational track (one-time):
   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
   │ FR-1         │    │ FR-2         │    │ FR-3         │    │ FR-4         │
   │ Diagnose     ├───►│ Apply pending├───►│ Restart App  ├───►│ Verify 2h    │
   │ (read-only   │    │ migration    │    │ Service      │    │ window       │
   │  SQL)        │    │ (if state B) │    │ (if state A) │    │              │
   └──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘
```

### Key Design Decisions

#### Decision 1: Smoke check is a Readiness probe, not a startup self-check

**Options considered:**
- **A.** Startup self-check that throws and prevents app from booting if schema mismatch.
- **B.** ASP.NET Core `IHealthCheck` registered under `/health/ready` tag, returning `HealthStatus.Unhealthy` on mismatch.
- **C.** First-request middleware that probes once and caches the result.

**Chosen approach:** **B — `IHealthCheck` tagged `ready`, exposed at `/health/ready`.**

**Rationale:**
- Azure App Service's container health probe is the platform-native gate. A ready-unhealthy instance is automatically removed from rotation; a started-but-failing instance still receives traffic until the platform notices. B uses the platform correctly.
- A (startup-throw) is too aggressive: a transient DB blip during boot would crash the container and trigger restart loops. The DB-migrations-are-manual reality means we want **soft fail** (no traffic) rather than **hard fail** (crash loop).
- C (first-request middleware) couples the check to user traffic, defeating the "detect at deploy time, not via 500s" goal.
- This is consistent with how Azure Web App for Containers and `Microsoft.Extensions.Diagnostics.HealthChecks` are designed to interact.

#### Decision 2: Smoke check scope — `DqtRuns` only for this PR

**Options considered:**
- **A.** Probe only `DqtRuns` (minimal, incident-scoped).
- **B.** Probe every table touched by `StandardizeTableNamingToPascalCase`.
- **C.** Generic "check every `DbSet<T>` resolves to an existing relation" probe.

**Chosen approach:** **A for this PR**, with an explicit follow-up issue for B.

**Rationale:**
- YAGNI: we have one observed failure mode. Building C now is speculative; B is broader than the bug demands.
- A readiness probe that touches every table on every readiness poll is itself a risk — slow probes cause flapping. NFR-1 caps probe overhead at 50 ms; minimal scope keeps headroom.
- The runbook update (FR-6) is the durable safeguard for *next time*, not the probe.
- Follow-up to broaden coverage should be filed as a separate ticket and is referenced in the runbook.

#### Decision 3: Probe query shape — `AnyAsync()` on `DqtRuns`

**Options considered:**
- **A.** `_db.DqtRuns.Take(1).ToListAsync()`.
- **B.** `_db.DqtRuns.AnyAsync()`.
- **C.** Raw SQL: `SELECT 1 FROM "DqtRuns" LIMIT 1`.
- **D.** Schema introspection: `SELECT 1 FROM information_schema.tables WHERE table_name = 'DqtRuns'`.

**Chosen approach:** **B — `await _db.DqtRuns.AsNoTracking().AnyAsync(cancellationToken)`**.

**Rationale:**
- B exercises the **full code path that was broken**: EF Core mapping → SQL generation → relation resolution. That is exactly the failure mode we are guarding against. D detects the table but not the mapping; if a future bug renames the *EF mapping* without renaming the DB table, D would falsely report healthy.
- B compiles to `SELECT EXISTS (SELECT 1 FROM "DqtRuns" LIMIT 1)` — cheap, indexed-table-friendly, no row materialization.
- A materializes a row and risks pulling large payloads if the entity grows; B does not.
- C bypasses EF Core mapping entirely, which defeats the purpose.
- Cancellation token is required to honor the readiness probe timeout.

#### Decision 4: Health-check failure is logged with structured fields, not swallowed

**Chosen approach:** Catch `PostgresException` with SQLState `42P01` specifically and return `HealthCheckResult.Unhealthy(...)` with structured `data`:
```
entity=DqtRun, expectedTable=DqtRuns, schema=public, sqlState=42P01, message=...
```
Other exceptions return `Unhealthy` with the raw exception. The probe never throws; it always returns a `HealthCheckResult`.

**Rationale:** A health check that throws is treated as unhealthy by the framework, but the diagnostic signal is poorer (no structured `data`). Differentiating `42P01` (schema drift, our target signal) from generic DB errors (connectivity, auth) helps on-call distinguish drift from infra incidents.

#### Decision 5: Operational track (FR-1 through FR-4) is *not* code

**Chosen approach:** The diagnosis, migration apply, restart, and verification window are an **incident runbook**, not a deliverable code change. They are documented in `memory/gotchas/` and `docs/development/setup.md` (FR-6) once executed. The PR contains only the FR-5 health check + FR-6 docs.

**Rationale:** Conflating one-time operational actions with shippable code muddles the PR scope and invites half-finished SQL scripts in the repo. The repo's convention (`memory/`, manual migrations) already supports this separation.

## Implementation Guidance

### Directory / Module Structure

New files:

```
backend/src/Anela.Heblo.API/
└── HealthChecks/
    └── DataQuality/
        └── DataQualitySchemaHealthCheck.cs        ◄── NEW: IHealthCheck

backend/src/Anela.Heblo.API/
├── DataQualityModule.cs                            ◄── EDIT: register health check
└── Program.cs (or HealthCheckModule.cs)            ◄── EDIT: map /health/ready

backend/test/Anela.Heblo.Tests/
└── API/HealthChecks/DataQuality/
    └── DataQualitySchemaHealthCheckTests.cs        ◄── NEW: 2 tests min (healthy + 42P01 unhealthy)

docs/development/
└── setup.md                                        ◄── EDIT (FR-6): pre/post-deploy checklist

memory/gotchas/
└── ef-migration-codebase-drift.md                  ◄── NEW: incident postmortem
```

**Why `API/HealthChecks/DataQuality/` and not `Persistence/`:** Health checks are an API-layer concern (they expose HTTP endpoints and are wired into the ASP.NET pipeline). The Data Quality vertical slice owns its own probe, consistent with the repo's vertical-slice rule. If a `HealthChecks/` folder already exists at the API root with a different convention, follow that — but keep the DataQuality probe in its own subdirectory.

**Do NOT create:**
- A new module project. This is a single class.
- A `Common.HealthChecks` cross-cutting library. YAGNI.
- A SQL migration. The schema is already correct (or will be, after FR-2).

### Interfaces and Contracts

**Health check contract:**

```csharp
public sealed class DataQualitySchemaHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;  // exact name TBD by repo

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
                    ["sqlState"] = ex.SqlState
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("DataQuality probe failed", ex);
        }
    }
}
```

**Registration contract (in `DataQualityModule.cs` or equivalent):**

```csharp
services.AddHealthChecks()
    .AddCheck<DataQualitySchemaHealthCheck>(
        name: "data-quality-schema",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "db", "schema" });
```

**Endpoint mapping (in `Program.cs`):**

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // liveness = process is up, no checks
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

If `/health` already exists with a different layout, **do not break it** — add `/health/ready` alongside and migrate the platform probe configuration as a follow-up.

**Authentication contract:** `/health/live` and `/health/ready` MUST be `[AllowAnonymous]` so the Azure platform can poll them without an Entra ID token. Verify this is consistent with how existing health endpoints (if any) are configured.

### Data Flow

**Healthy readiness probe (steady state):**
```
Azure App Service probe ──► GET /health/ready
  └─► HealthCheckService runs all "ready"-tagged checks in parallel
      └─► DataQualitySchemaHealthCheck.CheckHealthAsync
          └─► EF Core: SELECT EXISTS (SELECT 1 FROM "DqtRuns" LIMIT 1)
              └─► Postgres: returns t/f
          └─► HealthCheckResult.Healthy
      └─► Aggregate status: Healthy → HTTP 200
```

**Drift detected (the failure we want to catch):**
```
Azure App Service probe ──► GET /health/ready
  └─► DataQualitySchemaHealthCheck.CheckHealthAsync
      └─► EF Core: SELECT EXISTS (SELECT 1 FROM "DqtRuns" LIMIT 1)
          └─► Postgres: 42P01 relation "DqtRuns" does not exist
      └─► PostgresException caught, SqlState=42P01
      └─► HealthCheckResult.Unhealthy with structured data
  └─► Aggregate status: Unhealthy → HTTP 503
  └─► Azure App Service removes instance from rotation
  └─► Application Insights surfaces the structured event
  └─► On-call sees "data-quality-schema unhealthy, expectedTable=DqtRuns" not "500 on /api/data-quality/runs"
```

**Operational fix flow (one-time, FR-1 → FR-4):**
```
FR-1 (read-only SQL via psql or Azure portal query)
  └─► Determine state A / B / C
      ├─► A: skip to FR-3
      ├─► B: FR-2 → FR-3 → FR-4
      └─► C: STOP, escalate
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Readiness probe times out under DB load and flaps the pod | High | `AnyAsync` on indexed PK is sub-millisecond on a healthy DB. Set probe timeout ≥ 5s in Azure config. NFR-1 budget (50 ms) is for healthy path; under load, 503 is preferable to false-healthy. |
| Probe is registered but not actually polled by Azure (no platform config) | High | After deploy, manually `curl https://heblo.anela.cz/health/ready` and confirm 200. Document the Azure App Service health-check path setting in FR-6 runbook. |
| `42P01` check is too narrow — production might fail with a different SQLState | Medium | The generic `catch (Exception)` branch returns `Unhealthy` for any other failure. The `42P01` branch only adds *richer diagnostics*; it does not gate the unhealthy decision. |
| FR-2 migration apply locks `DqtRuns` table during rename, causing brief read failures | Medium | `ALTER TABLE ... RENAME` takes an `ACCESS EXCLUSIVE` lock but completes in milliseconds for metadata-only renames. Schedule during low-traffic window; the readiness probe will mark the instance unhealthy during the lock and recover automatically. |
| Restart in FR-3 causes brief downtime if instance count is 1 | Medium | Confirm App Service has ≥ 2 instances before rolling restart. If single-instance, accept brief 503s during the restart window (consistent with existing deploy behavior). |
| Health check runs against the wrong DbContext if the repo has multiple | Medium | Verify the DbContext name during implementation. If multiple contexts exist, inject the one that owns `DqtRuns` (likely `ApplicationDbContext`). |
| Probe leaks connection-pool slots under failure | Low | EF Core + Npgsql release connections on exception. `AsNoTracking` ensures no change-tracker accumulation. Standard pooling applies. |
| Smoke check passes locally but fails in prod due to schema differences | Low | Integration test (see Tests below) uses a real Postgres in Testcontainers or a migration-applied test DB to verify the healthy path. The unhealthy path is unit-tested with a mocked context that throws `PostgresException`. |
| Adding `/health/ready` collides with existing `/health` route | Low | Inspect existing routing during implementation. If conflict, scope the new path to `/health/ready/data-quality` or align with existing convention. |

## Specification Amendments

1. **FR-5 (clarify probe surface):** Resolve Open Question #2 in favor of an `IHealthCheck` registered under `/health/ready` (not a startup self-check). Update FR-5 acceptance criteria to:
   - "A `DataQualitySchemaHealthCheck : IHealthCheck` is registered with the `ready` tag and exposed at `/health/ready`."
   - "The check returns `HealthStatus.Unhealthy` (not `Degraded`) on `42P01`, so Azure removes the instance from rotation."

2. **FR-5 (clarify probe scope):** Resolve Open Question #3 — minimal scope (`DqtRuns` only) for this PR. Add explicit text: *"Broader coverage of all PascalCase-renamed tables is tracked as a follow-up issue and is out of scope for this PR."*

3. **FR-5 (probe query specification):** Specify the query as `await _db.DqtRuns.AsNoTracking().AnyAsync(ct)` — not `Take(1).ToListAsync()`. Reason: avoids row materialization and exercises the same EF mapping path that produces the bug.

4. **FR-5 (test acceptance):** Tighten "Unit/integration tests cover both paths" to:
   - **Unit test:** mocked `DbContext` where `DqtRuns.AnyAsync` throws `PostgresException("42P01")` → assert `HealthCheckResult.Status == Unhealthy` with `data["sqlState"] == "42P01"`.
   - **Unit test:** mocked `DbContext` where `DqtRuns.AnyAsync` returns `false` → assert `Healthy`.
   - **Integration test (optional but recommended):** real test DB with applied migrations → assert `/health/ready` returns 200; drop the table → assert 503.

5. **FR-3 (concrete confirmation step):** Add: *"Before restarting, capture the running image digest via `az webapp config container show ...` and verify it matches the SHA of the commit containing `DqtRunConfiguration.ToTable("DqtRuns", ...)`."* This makes Open Question #4 actionable.

6. **NFR-1 (probe budget):** Clarify "< 50 ms in healthy path" applies to the *check execution time*, not end-to-end HTTP latency to `/health/ready` (which includes pipeline overhead). Use `Stopwatch` in tests if asserting.

7. **Out of Scope — add:** "Changes to existing `/health` endpoint behavior beyond adding the `/health/ready` path. If the existing health-check infrastructure is incompatible, that work is deferred to a separate PR."

8. **New FR-7 (recommended):** *"Document the FR-1 diagnostic SQL in `docs/development/setup.md` as a reusable runbook snippet for future schema-drift incidents, parameterized by table/migration name."* This generalizes the lesson without expanding code scope.

## Prerequisites

Before implementation can start:

1. **Confirm DbContext name and Persistence module structure.** Verify the EF Core context that owns `DqtRuns` (`ApplicationDbContext` is the conventional name; confirm in `Anela.Heblo.Persistence`).
2. **Confirm existing health-check infrastructure.** Determine whether `AddHealthChecks()` is already called in `Program.cs`/startup. If yes, *extend* it; if no, the PR adds it.
3. **Confirm Azure App Service health-check path.** Whatever path Azure currently polls (`/health`, `/`, or unconfigured) must be reconciled with the new `/health/ready`. If unconfigured, FR-6 must include the Azure setting change.
4. **Confirm `[AllowAnonymous]` policy for health endpoints.** The repo enforces Entra ID auth globally; health endpoints must be exempted, and that exemption must be visible in the auth pipeline.
5. **Confirm `Npgsql` package version supports `PostgresException.SqlState`.** All recent versions do; verify in `Directory.Packages.props` or the relevant `.csproj`.
6. **Resolve Open Question #5 (migration apply authority).** Before FR-2 executes, the actor (developer / ops / Hangfire one-shot) and exact command must be agreed and recorded. This is operational, not code.
7. **Operator access to production DB for FR-1 read-only diagnostic SQL.** Read-only credentials must be available; no embedded credentials in repo (NFR-2).
8. **Telemetry access for FR-4 verification window.** Confirm Application Insights or equivalent dashboards are queryable for the 2-hour observation period.