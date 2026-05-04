```markdown
# Architecture Review: Stabilize `/health/ready` against TaskCanceledException spikes

## Architectural Fit Assessment

This work is a **surgical hardening** of the API composition root and one custom health check. It does not introduce a new feature, new module, new dependency, or new contract — it corrects two specific deviations from patterns that already exist in this codebase:

1. The application already builds a tuned, singleton `NpgsqlDataSource` in `PersistenceModule` (`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:80-81`) with explicit `KeepAlive`, `ConnectionLifetime`, and pruning settings. Every other DB consumer (EF Core via `ApplicationDbContext`) flows through that pool. **The DB health check is the lone outlier**, opening a fresh connection per probe via `AddNpgSql(connectionString, ...)`. Aligning it with the rest of the application is a pattern-conformance fix, not new architecture.
2. `DataQualitySchemaHealthCheck` follows the right shape (implements `IHealthCheck`, has a defensive try/catch) but is missing the `OperationCanceledException` carve-out that is standard practice for any `IHealthCheck` that touches I/O with a forwarded `CancellationToken`. This is a one-line completion of an existing pattern.

**Integration points** are limited to:
- `Program.cs` startup composition (DI registration order — currently `AddHealthCheckServices` runs **before** `AddPersistenceServices`; this matters and is addressed below)
- `ApplicationBuilderExtensions.ConfigureHealthCheckEndpoints` (consumer of the registered checks; no changes needed)
- `appsettings.json` (one new optional key under a new `HealthChecks` section)
- `CostOptimizedTelemetryProcessor` (already filters `/health*`; no changes, but the fix removes the upstream source of the leak that the processor cannot suppress because exceptions tracked from unhandled throws aren't covered by request-trace filters)

The blast radius is small, the fix sits entirely inside one assembly (`Anela.Heblo.API`), and there are no domain, persistence, or contract changes.

## Proposed Architecture

### Component Overview

```
                ┌──────────────────────────────────────────────┐
                │  Azure App Service / Kubernetes              │
                │  probe → GET /health/ready                   │
                └──────────────────────────┬───────────────────┘
                                           │
                                           ▼
                ┌──────────────────────────────────────────────┐
                │  HealthCheckMiddleware                       │
                │  Predicate: Tags ⊇ {"db", "ready"}           │
                │  ResponseWriter: UIResponseWriter            │
                └──────────────────────────┬───────────────────┘
                                           │ runs all matching checks in parallel
                                           │ each wrapped with per-check Timeout (5s)
              ┌────────────────────────────┼────────────────────────────────┐
              │                            │                                │
              ▼                            ▼                                ▼
  ┌────────────────────────┐   ┌────────────────────────┐   ┌──────────────────────────┐
  │ background-services-   │   │ data-quality-schema    │   │ database (AddNpgSql)     │
  │ ready                  │   │ (custom IHealthCheck)  │   │                          │
  │ Tags: ready            │   │ Tags: ready,db,schema  │   │ Tags: db, postgresql     │
  │ no I/O                 │   │ injects ApplicationDb- │   │ resolves NpgsqlDataSource│
  │                        │   │ Context                │   │ from DI (factory)        │
  │                        │   │ catches OCE → Degraded │   │ shares pool w/ EF Core   │
  └────────────────────────┘   └─────────┬──────────────┘   └─────────────┬────────────┘
                                         │                                │
                                         ▼                                ▼
                              ┌──────────────────────┐       ┌─────────────────────────┐
                              │ ApplicationDbContext │       │   NpgsqlDataSource      │
                              │ (scoped)             │──────▶│   (singleton)           │
                              └──────────────────────┘       │   pool, keepalive,      │
                                                             │   lifetime tuned        │
                                                             └─────────────────────────┘
```

The architectural shift is **removing one box** (the per-probe Npgsql connection) and **collapsing two arrows** (`AddNpgSql → fresh NpgsqlConnection` and `ApplicationDbContext → NpgsqlDataSource`) into a single shared path through the singleton pool.

### Key Design Decisions

#### Decision 1: Reorder DI registration so `AddPersistenceServices` precedes `AddHealthCheckServices`

**Options considered:**
- A. Keep current order (`AddHealthCheckServices` first), use a connection-string check as a proxy for "data source will exist", and pass the factory `sp => sp.GetRequiredService<NpgsqlDataSource>()` (lazy resolution at probe time means actual presence is only required when the probe runs).
- B. Reorder calls in `Program.cs` so `AddPersistenceServices` registers `NpgsqlDataSource` before `AddHealthCheckServices` queries the `IServiceCollection` for it.
- C. Probe `services.Any(d => d.ServiceType == typeof(NpgsqlDataSource))` regardless of order.

**Chosen approach:** **B — reorder.** Move `builder.Services.AddPersistenceServices(...)` above `builder.Services.AddHealthCheckServices(...)` in `Program.cs:55-58`.

**Rationale:** Composition order should reflect dependency direction — health checks depend on persistence, so persistence should register first. This makes both the DI service-collection probe (option C) and the lazy factory (option A) safe and explicit. It also future-proofs any further health-check additions that may need to inspect persistence-related registrations at composition time. The alternative (relying purely on lazy `sp.GetRequiredService<NpgsqlDataSource>()`) works but is fragile: the registration silently succeeds even when the data source will never exist (e.g., `UseInMemoryDatabase=true`), only failing at first probe. Reordering keeps the contract observable at startup.

#### Decision 2: Gate the `AddNpgSql` registration on `UseInMemoryDatabase` / `connectionString == "InMemory"`, not on raw connection-string presence

**Options considered:**
- A. Keep the current `if (!string.IsNullOrEmpty(dbConnectionString))` gate, which checks `ConnectionStrings:DefaultConnection`.
- B. Mirror `PersistenceModule`'s exact gating logic: skip when `UseInMemoryDatabase=true` or `connectionString == "InMemory"`.
- C. Probe DI directly: `services.Any(d => d.ServiceType == typeof(NpgsqlDataSource))`.

**Chosen approach:** **C, with B as the documented invariant.** After Decision 1's reorder, `services.Any(d => d.ServiceType == typeof(NpgsqlDataSource))` becomes the correct, single source of truth: it answers exactly "is there a real Postgres pool to probe?" without duplicating `PersistenceModule`'s decision logic.

**Rationale:** The current connection-string check is a leaky proxy — `PersistenceModule` reads `configuration.GetConnectionString(environment.EnvironmentName)` (not `DefaultConnection`), so the two paths can disagree. Probing DI directly ties the health check to the actual outcome of `PersistenceModule`'s decision, eliminating the proxy.

#### Decision 3: Use `HealthCheckRegistration.Timeout` per check, not a wrapping `CancellationTokenSource`

**Options considered:**
- A. Apply `Timeout` on each `HealthCheckRegistration` (5 s), letting the framework drive cancellation.
- B. Wrap each check with `CancellationTokenSource.CreateLinkedTokenSource(token).CancelAfter(5s)`.

**Chosen approach:** **A — `HealthCheckRegistration.Timeout`.**

**Rationale:** The framework already implements precisely this — wrapping per-check execution with a linked token that fires after the configured timeout, then mapping the resulting cancellation into a `HealthCheckResult.Unhealthy` (or `Degraded` if so configured) with a stable `description`. Using the framework primitive keeps the timeout behavior consistent across both checks and avoids reinventing the cancellation wiring inside our code. For `AddNpgSql`, this is the only ergonomic option (the registration is internal). For consistency, apply the same primitive to `data-quality-schema`.

#### Decision 4: `Degraded` (not `Unhealthy`) for cancelled `DataQualitySchemaHealthCheck`

**Options considered:**
- A. Return `Degraded("DataQuality probe was cancelled")` — orchestrator continues routing traffic.
- B. Return `Unhealthy("DataQuality probe was cancelled")` — orchestrator marks instance not-ready, may restart.
- C. Re-throw and let the framework's timeout-mapping decide.

**Chosen approach:** **A — `Degraded`** (matching the spec's FR-2 assumption).

**Rationale:** A cancelled probe is a transient timing artifact, not evidence of a broken DB schema. Treating it as `Unhealthy` would defeat the purpose of the fix: every cancellation would trigger Azure App Service's not-ready response and a probable restart, amplifying the very problem this work addresses. **Caveat: this decision depends on `UIResponseWriter.WriteHealthCheckUIResponse` mapping `Degraded` to HTTP 200, not 503.** That assumption is verified below in Specification Amendments.

#### Decision 5: Configuration shape — `HealthChecks:ProbeTimeoutSeconds` as a strongly-typed options object

**Options considered:**
- A. Read `configuration.GetValue<int>("HealthChecks:ProbeTimeoutSeconds", 5)` inline at registration time.
- B. Introduce a typed `HealthChecksOptions` record bound via `services.Configure<HealthChecksOptions>(...)` and consumed in registration.

**Chosen approach:** **A — inline `GetValue<int>`** at this scale.

**Rationale:** A single integer setting with one consumer does not warrant the ceremony of an options class. If future health-check tunables emerge (per-check timeouts, retry counts, alternative writers), the inline read is trivially refactorable into an options class then. YAGNI applies.

## Implementation Guidance

### Directory / Module Structure

No new files. All changes are confined to existing locations:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.API/Program.cs` | Move `AddPersistenceServices(...)` to run **before** `AddHealthCheckServices(...)` (lines ~55–58). |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | Rewrite the body of `AddHealthCheckServices` (lines 80–99): replace raw-connection-string `AddNpgSql` with the `NpgsqlDataSource` factory overload; gate on DI registration of `NpgsqlDataSource`; apply `Timeout` to both DB-touching checks. |
| `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs` | Inject `ILogger<DataQualitySchemaHealthCheck>`. Insert `catch (OperationCanceledException)` between the existing `PostgresException` and generic `Exception` catches. Emit one Information-level log entry on the cancellation path with `{ ProbeName, ElapsedMs }`. |
| `backend/src/Anela.Heblo.API/appsettings.json` | Add `"HealthChecks": { "ProbeTimeoutSeconds": 5 }` at the top level. |
| `backend/test/Anela.Heblo.Tests/HealthChecks/DataQualitySchemaHealthCheckTests.cs` (new — file may already exist; create or extend) | Three test paths: healthy, cancelled token → `Degraded`, generic exception → `Unhealthy`. |
| `backend/test/Anela.Heblo.Tests/Startup/HealthCheckRegistrationTests.cs` (new) | Assert `database` health check is registered, resolves the same `NpgsqlDataSource` singleton, and is absent under `UseInMemoryDatabase=true`. |

**Do not** create a new `HealthChecksModule` or relocate existing checks. The current placement matches the codebase convention (cross-cutting infrastructure stays in `Anela.Heblo.API/Extensions`).

### Interfaces and Contracts

No public surface changes. Internal contracts:

```csharp
// In ServiceCollectionExtensions.AddHealthCheckServices
public static IServiceCollection AddHealthCheckServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var probeTimeout = TimeSpan.FromSeconds(
        Math.Max(0, configuration.GetValue<int>("HealthChecks:ProbeTimeoutSeconds", 5)));

    var healthChecksBuilder = services.AddHealthChecks()
        .AddCheck<BackgroundServicesReadyHealthCheck>(
            "background-services-ready",
            tags: new[] { "ready" })
        .AddCheck<DataQualitySchemaHealthCheck>(
            name: "data-quality-schema",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready", "db", "schema" },
            timeout: probeTimeout > TimeSpan.Zero ? probeTimeout : default);

    var hasNpgsqlDataSource = services.Any(d => d.ServiceType == typeof(NpgsqlDataSource));
    if (hasNpgsqlDataSource)
    {
        healthChecksBuilder.AddNpgSql(
            sp => sp.GetRequiredService<NpgsqlDataSource>(),
            name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG },
            timeout: probeTimeout > TimeSpan.Zero ? probeTimeout : default);
    }

    return services;
}
```

```csharp
// In DataQualitySchemaHealthCheck (new catch block, ordering is load-bearing)
catch (PostgresException ex) when (ex.SqlState == "42P01") { /* unchanged */ }
catch (OperationCanceledException)
{
    _logger.LogInformation(
        "DataQuality probe cancelled after {ElapsedMs}ms",
        stopwatch.ElapsedMilliseconds);
    return HealthCheckResult.Degraded("DataQuality probe was cancelled");
}
catch (Exception ex) { /* unchanged */ }
```

### Data Flow

**Successful probe (steady state, warm pool):**
1. Azure probe → `GET /health/ready`.
2. Middleware fans out to three checks in parallel; each wrapped in a 5 s timeout.
3. `data-quality-schema`: `ApplicationDbContext` → `NpgsqlDataSource.OpenConnectionAsync()` → returns pooled connection (no handshake) → `SELECT 1 FROM "DqtRuns" LIMIT 1` → `Healthy`.
4. `database` (`AddNpgSql`): same `NpgsqlDataSource` instance → pooled connection → library's `SELECT 1` → `Healthy`.
5. `background-services-ready`: in-memory check → `Healthy`.
6. `UIResponseWriter` → HTTP 200 + JSON.

**Cancelled probe (timeout fires or orchestrator cancels):**
1. Azure probe → `GET /health/ready`.
2. Linked CTS hits 5 s; tokens cancel.
3. `data-quality-schema`: EF query observes cancellation → `OperationCanceledException` → caught → `Degraded("DataQuality probe was cancelled")` + Information log.
4. `database`: `AddNpgSql` (with `NpgsqlDataSource`) returns structured `Unhealthy` for the timed-out check; no unhandled exception escapes.
5. Aggregate report: overall `Unhealthy` (because `database` is `Unhealthy`) → HTTP 503 → orchestrator routes traffic away as intended.
6. **Critical:** no `TaskCanceledException` reaches App Insights as an unhandled exception. The previously observed problem ID disappears.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `AspNetCore.HealthChecks.NpgSql` 8.0.1 (currently referenced) does not expose the `Func<IServiceProvider, NpgsqlDataSource>` overload. | Medium | The 8.x line *does* support `NpgsqlDataSource` (added in 7.0.0). If the specific overload signature differs, fall back to the `Func<IServiceProvider, NpgsqlConnection>` overload that calls `dataSource.CreateConnection()` — same pool, same outcome. Confirm at first compile; no package upgrade should be required. |
| `UIResponseWriter.WriteHealthCheckUIResponse` maps `Degraded` to HTTP 503, defeating Decision 4. | High | Verify with a unit test (`WebApplicationFactory` integration test) that asserts a `Degraded`-only response yields HTTP 200. If it returns 503, change FR-2 to return `Unhealthy` *without* `exception` payload (still suppresses the App Insights leak; loses the "stay in rotation" benefit). This is an explicit decision gate before merge. |
| Reordering `AddPersistenceServices` ahead of `AddHealthCheckServices` breaks an unstated dependency (e.g., a service consumed during persistence registration depends on something registered later). | Low | The two methods are independent in current code; `AddPersistenceServices` reads only `IConfiguration`/`IHostEnvironment`. Run `ApplicationStartupTests` after the reorder. |
| Connection-pool starvation: under high probe rate, the health check now competes with application traffic for pooled connections. | Low | Probe interval is on the order of seconds; `MaxPoolSize` is configurable and already tuned. Net effect is a *decrease* in connection count vs. the current "open new connection per probe" behavior. Monitor `Database:MaxPoolSize` saturation in App Insights post-deploy. |
| 5 s timeout fires more aggressively than the underlying Npgsql command/connection timeout, masking real DB-side slowness as a probe-layer failure. | Low | Logged `Degraded` events with `ElapsedMs` provide visibility. If timeouts cluster near 5 s, raise `HealthChecks:ProbeTimeoutSeconds` per environment. The escape hatch (`0` disables) is in FR-3. |
| Health-check concurrency change: `NpgsqlDataSource` is thread-safe, but interleaved probe + heavy app traffic could hit `MaxPoolSize`. | Low | Same mitigation as pool starvation. The library opens, probes, returns immediately — connection lease is sub-millisecond. |
| `DataQualitySchemaHealthCheck`'s `_db` (`ApplicationDbContext`) is scoped; the health-check infrastructure creates a scope per probe, so concurrent probes get distinct contexts. No state risk. | None | No action; documenting for reviewers. |

## Specification Amendments

The following clarifications resolve the spec's open questions and tighten requirements:

1. **Open Q1 (package overload availability) → Resolved.** `AspNetCore.HealthChecks.NpgSql` 8.0.1 supports `NpgsqlDataSource`-based registration. If the exact `Func<IServiceProvider, NpgsqlDataSource>` overload is unavailable, use `AddNpgSql(sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection(), ...)` — equivalent behavior. **No package upgrade.**
2. **Open Q2 (`UIResponseWriter` status mapping for `Degraded`) → Required verification before FR-2 lands.** Add an integration test in `Anela.Heblo.Tests` that posts a synthetic `Degraded` health-check report through `UIResponseWriter` and asserts HTTP 200. **This test must pass; otherwise FR-2 reverts to `Unhealthy` without exception payload.** The test is acceptance-blocking.
3. **Open Q3 (Azure probe timeout alignment) → Out of scope; document only.** The 5 s in-app timeout sits well below Azure App Service's default 30 s probe timeout. If a non-default value is configured at the App Service level, the on-call engineer should raise `HealthChecks:ProbeTimeoutSeconds` to match. Note this in `docs/architecture/observability.md`.
4. **Open Q4 (Degraded vs. Unhealthy policy) → Confirmed `Degraded` per Decision 4**, contingent on the integration test in #2 above.
5. **Composition order requirement (new):** `AddPersistenceServices` MUST be registered before `AddHealthCheckServices` in `Program.cs`. This was not stated in the spec but is required by the chosen DI-presence gating in Decision 2.
6. **Test coverage addition (new):** Add `HealthCheckRegistrationTests` (composition-root test) that asserts (a) the `database` check resolves the same `NpgsqlDataSource` instance as the rest of DI, (b) the `database` check is *not* registered when `UseInMemoryDatabase=true`, and (c) `data-quality-schema` and `database` registrations expose `Timeout = 5s`.
7. **Logging contract refinement (NFR-4):** The structured log on cancellation MUST be Information level (not Warning) and MUST omit the exception object — the goal is suppression of App Insights exception telemetry, and Warning-with-exception would re-introduce the noise.

## Prerequisites

Nothing infrastructural is required before implementation can begin. The only "prerequisites" are existing facts already true in the codebase:

- `NpgsqlDataSource` is registered as a singleton in `PersistenceModule.AddPersistenceServices` ✅ (verified at `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:80-81`).
- `AspNetCore.HealthChecks.NpgSql` 8.0.1 is referenced ✅ (verified at `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj:36`).
- `CostOptimizedTelemetryProcessor` already filters `/health*` request telemetry ✅ — no changes needed; the fix attacks the source of *unhandled exceptions* that the request-trace filter cannot reach.

No migration. No new configuration secret. No infrastructure provisioning. No coordinated deployment with another service. Implementation can start immediately on a feature branch and ship as a single PR.
```