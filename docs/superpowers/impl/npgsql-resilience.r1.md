# Implementation: Npgsql Database Connection Resilience & Telemetry

## What was implemented

A Polly v8-backed EF Core execution strategy that retries transient Azure PostgreSQL connection drops (SqlState 57P0x, 08* family, SocketException, TimeoutException, IOException), plus connection-pool telemetry and hardened pool sizing. Both `ApplicationDbContext` and `AnalyticsDbContext` are covered. The analytics pool was previously unbounded; it is now capped at 10. A CI guard prevents future regressions from user-managed transactions that would break the retry contract.

## Files created/modified

### New — Persistence/Infrastructure/Resilience/
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceOptions.cs` — strongly-typed options bound from `Database:Resilience`
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/TransientErrorClassifier.cs` — static SqlState allow/deny lists; the single authority for "is this worth retrying?"
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/IDbResiliencePipelineProvider.cs` — interface exposing the singleton Polly `ResiliencePipeline`
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResiliencePipelineProvider.cs` — singleton that builds exponential-backoff + jitter pipeline with 10 s total timeout
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs` — `IExecutionStrategy` delegating to the pipeline; `RetriesOnFailure = true`
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceMetrics.cs` — `IMeterFactory`-backed counters: `db.retry.attempts`, `db.retry.success`, `db.retry.failure`, `npgsql.pool.exhaustion_wait_seconds`
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs` — `DbConnectionInterceptor` capturing connection-open latency (pool exhaustion) and `ConnectionFailed` events with structured `exception.type`, `npgsql.host`, `npgsql.database`

### Modified — Persistence projects
- `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj` — added Polly 8.4.1 + Polly.Extensions 8.4.1
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — registers singleton pipeline provider + metrics, scoped interceptors, wires `PollyExecutionStrategy` via `npgsql.ExecutionStrategy(...)`
- `backend/src/Anela.Heblo.Persistence.Analytics/Anela.Heblo.Persistence.Analytics.csproj` — added Polly packages + ProjectReference to Persistence
- `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs` — added `maxPoolSize` param, caps pool, wires same execution strategy and interceptor; analytics `NpgsqlDataSource` registered as keyed singleton `"analytics"` for DI-managed disposal
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — passes `AnalyticsDatabase:MaxPoolSize ?? 10` to `AddAnalyticsPersistenceServices`

### Modified — API config
- `backend/src/Anela.Heblo.API/appsettings.json` — added `Database:Resilience` defaults + `AnalyticsDatabase:MaxPoolSize: 10`
- `backend/src/Anela.Heblo.API/appsettings.Production.json` — `Database:MaxPoolSize` 15 → **20**, added `Database:Resilience` block, added `AnalyticsDatabase:MaxPoolSize: 10`
- `backend/src/Anela.Heblo.API/appsettings.Staging.json` — added `Database:Resilience` block, `AnalyticsDatabase:MaxPoolSize: 10`
- `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs` — added comment documenting Npgsql EventCounter auto-collection path and that `DbResilienceMetrics` flows via `ILogger`

### New — Tests
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/TransientErrorClassifierTests.cs` — 16 tests covering the full SqlState allow/deny matrix
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/DbResiliencePipelineProviderTests.cs` — 10 tests: retry count, no-retry on logical conflicts, time-budget abort
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs` — strategy delegation, retry-on-transient, no-retry on unique violation
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/ProductionConnectionStringDefaultsTests.cs` — 4 regression tests asserting production pool sizes and resilience options

### New — Docs / CI
- `docs/integrations/db-connection-health-kql.md` — KQL snippets for App Insights: exception spike, pool exhaustion waits, retry counters, alert queries
- `docs/architecture/infrastructure.md` — appended Action Group + both alert rules with `az monitor scheduled-query create` runbook
- `scripts/check-no-managed-tx.sh` — CI guard: fails build if `BeginTransaction|UseTransaction` appears in `backend/src/*.cs`
- `.github/workflows/ci-feature-branch.yml` — guard step added before build in `backend-tests` job

## Tests

34 new tests, all passing. Run with:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Persistence.Resilience"
```

## How to verify

```bash
# Build
dotnet build backend

# Run all resilience tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Persistence.Resilience"

# Managed-transaction CI guard
./scripts/check-no-managed-tx.sh
```

Manual runbooks (deployment-time only, not automated):
- Task 16: Update Key Vault connection strings per `docs/superpowers/plans/2026-06-13-npgsql-db-connection-resilience.md` Task 16
- Task 17: Create/verify `ag-heblo-prod-default` Action Group + alert rules per `docs/architecture/infrastructure.md`

## Notes

- **FR-1 audit** (Task 0) requires live Azure CLI access (`az postgres flexible-server show`) — this is a manual pre-deployment step documented in the plan. The audit table in `docs/architecture/environments.md` has the schema ready to fill in.
- **Meter visibility**: `DbResilienceMetrics` counters (`db.retry.*`, `npgsql.pool.exhaustion_wait_seconds`) emit via `System.Diagnostics.Metrics`. The existing `Microsoft.ApplicationInsights.AspNetCore` SDK does not natively bridge these to App Insights. The retry events are also logged as structured `ILogger.LogWarning("DbTransientRetry...")` messages which AI *does* capture. For full metric export, a future iteration could add the OpenTelemetry `Azure.Monitor.OpenTelemetry.Exporter` — the spec's NFR-3 ("no new sinks") was the constraint that deferred this.
- **Analytics disposal**: Fixed using .NET 8 keyed singleton (`services.AddKeyedSingleton<NpgsqlDataSource>("analytics", dataSource)`) so DI manages the analytics pool's lifecycle without shadowing the main `NpgsqlDataSource` singleton.

## PR Summary

Added Polly v8 EF Core execution strategy, connection-pool telemetry, and hardened pool sizing to absorb Azure PostgreSQL transient connection drops instead of surfacing them as 500 errors.

The execution strategy wraps every EF Core database operation in a singleton `ResiliencePipeline` (exponential backoff with jitter, max 3 attempts, 10 s total budget). It retries only genuine transient faults — SqlState 57P0x / 08* family, SocketException, TimeoutException, IOException — and never retries logical conflicts (23505/23503/23502 or DbUpdateConcurrencyException). Both `ApplicationDbContext` and `AnalyticsDbContext` are protected; the analytics pool was previously unbounded and is now capped at 10. Production EF pool raised from 15 → 20. Connection-open latency and failure events are logged with structured properties for App Insights queries. A CI guard fails the build if any `BeginTransaction`/`UseTransaction` call appears in `backend/src`, protecting the retry contract going forward.

### Changes
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/` — 7 new files: classifier, options, metrics, pipeline provider + interface, execution strategy, connection interceptor
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — wire execution strategy + interceptors
- `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs` — cap pool, wire strategy + interceptor
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — pass pool size to analytics module
- `backend/src/Anela.Heblo.API/appsettings.*.json` — pool sizes + resilience options
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/` — 4 new test files, 34 tests
- `docs/integrations/db-connection-health-kql.md` — KQL for App Insights queries
- `docs/architecture/infrastructure.md` — Azure Monitor alert runbook
- `scripts/check-no-managed-tx.sh` + CI workflow update

## Status
DONE_WITH_CONCERNS

Concerns:
1. **Meter visibility**: Custom `DbResilienceMetrics` counters flow via ILogger but not as Azure Monitor Metrics (no OTel bridge added per NFR-3). Retry events are queryable as traces in App Insights. A future task can add `Azure.Monitor.OpenTelemetry.Exporter` if metric dashboards are needed.
2. **FR-1 audit** and **FR-5 alert creation** (Tasks 0, 16, 17) are manual deployment-time steps requiring live Azure access — not automated in this branch.
