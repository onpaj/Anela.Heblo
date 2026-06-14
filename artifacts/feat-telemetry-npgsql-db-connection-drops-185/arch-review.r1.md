I have a complete picture. Confirmed: no existing `IExecutionStrategy`, `AnalyticsPersistenceModule` already shares the same pattern (KeepAlive=30, ConnectionLifetime=600), there's already `Meter`-based metrics in `SmartsuppWebhookMetrics`, and Hangfire uses its own MaxPoolSize-capped connection string. Now I have everything to write the review.

# Architecture Review: Npgsql Database Connection Resilience & Telemetry

## Skip Design: true

## Architectural Fit Assessment

The feature is a backend-only, cross-cutting resilience + observability change inside `Anela.Heblo.Persistence`. It aligns cleanly with existing patterns:

- **Polly v8 ResiliencePipeline is already the project's resilience idiom** — see `CatalogResilienceService` (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs:56`) and `DownloadResilienceService`. The proposed `IDbResiliencePipelineProvider` reuses that mental model in the persistence ring.
- **`NpgsqlDataSourceBuilder` is already the canonical wiring point** for connection-string-derived options (`PersistenceModule.cs:40-66` and `AnalyticsPersistenceModule.cs:13-16`). Connection-string defaults from FR-2 land in the same builder block.
- **`Meter`/`IMeterFactory` + Application Insights is already the metrics path** — `SmartsuppWebhookMetrics` (`backend/src/Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppWebhookMetrics.cs:10-41`) demonstrates exactly the schema (`Counter<long>`, `Histogram<double>`, `MeterName` constant, `TagList` tags). FR-3 telemetry follows the same shape.
- **EF Core interceptor for connection-layer logging already exists** — `PostgresExceptionLoggingInterceptor` only handles `SaveChangesFailed`. The "mid-read disconnect" case from telemetry comes from `DbCommandInterceptor`, not `SaveChangesInterceptor`, so a new sibling interceptor is the right extension point (do not bloat the existing class).
- **Hangfire uses an isolated MaxPoolSize-capped clone of the connection string** (`ServiceCollectionExtensions.cs:298-308`), and `AnalyticsPersistenceModule` builds its own `NpgsqlDataSource`. **Each pool is independent** — FR-2's "total connection budget" requires summing three pools, not one.

Integration risks are concentrated in three places: (1) execution-strategy contract compatibility with EF Core 8.0.4, (2) Npgsql 8 EventSource→Meter bridge availability, and (3) any read-path call that opens its own `NpgsqlConnection` outside EF (e.g. `MaterialContainerCodeGenerator`) — which the execution strategy will not protect.

## Proposed Architecture

### Component Overview

```
                            appsettings:Database:Resilience  ──┐
                                                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Persistence                                                │
│                                                                        │
│  ┌──────────────────────────┐    ┌────────────────────────────────┐    │
│  │ PersistenceModule        │    │ Infrastructure/Resilience/     │    │
│  │  AddPersistenceServices  │───►│  IDbResiliencePipelineProvider │    │
│  │   (existing entry point) │    │  DbResiliencePipelineProvider  │    │
│  │                          │    │  PollyExecutionStrategy        │    │
│  │  + AddDbResilience()     │    │  DbResilienceMetrics (Meter)   │    │
│  └─────────┬────────────────┘    │  DbResilienceOptions           │    │
│            │                     └────────────────────────────────┘    │
│            │                                  ▲                        │
│            │                                  │ ExecuteAsync           │
│            │                                  │                        │
│            ▼                                  │                        │
│  options.UseNpgsql(dataSource, x =>           │                        │
│    x.ExecutionStrategy(deps =>                │                        │
│      new PollyExecutionStrategy(deps, p)))────┘                        │
│                                                                        │
│  ┌──────────────────────────────────────┐                              │
│  │ Infrastructure/                      │                              │
│  │  PostgresExceptionLoggingInterceptor │ (existing — SaveChanges)    │
│  │  NpgsqlConnectionInterceptor (NEW)   │ (DbConnection lifecycle)    │
│  └──────────────────────────────────────┘                              │
└────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API                                                        │
│  Program.cs:                                                           │
│    AddPersistenceServices() ─────────────► wires execution strategy   │
│    AddOptimizedApplicationInsights() ────► Meter listener (existing) │
│    + Npgsql Meter ("Npgsql") registered with OpenTelemetry/AI bridge  │
└────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                  Application Insights (metrics + logs + alerts)
                              ▲
                              │
                  Azure Monitor Action Group `ag-heblo-prod-default`
```

### Key Design Decisions

#### Decision 1: Polly lives in Persistence, not Application
**Options considered:**
- (A) Put `IDbResiliencePipelineProvider` in `Anela.Heblo.Application` alongside the existing `ICatalogResilienceService` / `IDownloadResilienceService`.
- (B) Put it in `Anela.Heblo.Persistence/Infrastructure/Resilience/`.
- (C) Put it in `Anela.Heblo.Xcc` for full cross-cutting reuse.

**Chosen approach:** B — `Anela.Heblo.Persistence/Infrastructure/Resilience/`. Add a direct `PackageReference` to `Polly` 8.4.1 and `Polly.Extensions` 8.4.1 in `Anela.Heblo.Persistence.csproj` (not a `ProjectReference` to Application, which would invert the dependency direction).

**Rationale:** The pipeline is *only* consumed by `PollyExecutionStrategy`, which is itself a Persistence concern (it implements EF Core's `IExecutionStrategy`). Placing it in Application would force Persistence to depend on Application — that inverts the dependency graph (Application already references Persistence). The existing `CatalogResilienceService` lives in Application because it wraps Application-layer use cases; the DB pipeline wraps Persistence-layer execution and belongs there.

#### Decision 2: Custom `IExecutionStrategy` vs. wrapping repositories with the Polly pipeline
**Options considered:**
- (A) Build `PollyExecutionStrategy : IExecutionStrategy` and register via `optionsBuilder.UseNpgsql(ds, n => n.ExecutionStrategy(deps => new PollyExecutionStrategy(...)))`.
- (B) Inject `IDbResiliencePipelineProvider` into every repository and wrap calls explicitly.
- (C) Use Npgsql's built-in `EnableRetryOnFailure()` (which produces `NpgsqlRetryingExecutionStrategy`).

**Chosen approach:** A.

**Rationale:** (B) is invasive across ~30+ repositories and easy to miss on new code. (C) hard-codes the retry predicate and SQL-state list inside the Npgsql provider with no way to plug in custom logging/metrics; the spec explicitly forbids it (FR-4). (A) is transparent to repository authors, lives at the boundary EF Core itself manages, and aligns with the spec. The single-pipeline contract also means the execution-strategy guards against the "Polly retries inside Polly" anti-pattern that nested approaches risk.

#### Decision 3: Two interceptors, not one
**Options considered:**
- (A) Extend `PostgresExceptionLoggingInterceptor` to also implement `IDbConnectionInterceptor` and log `Connection*Failed` events.
- (B) Create a sibling `NpgsqlConnectionInterceptor : DbConnectionInterceptor` that lives next to it.

**Chosen approach:** B.

**Rationale:** `SaveChangesInterceptor` and `DbConnectionInterceptor` are distinct base classes; combining them forces multi-interface inheritance that obscures intent. Read-path connection failures (the `ReadMessageLong` cluster from telemetry — 120 occurrences) do **not** flow through `SaveChangesFailed`, so the existing interceptor cannot see them. A sibling interceptor (also `Scoped`, registered in `PersistenceModule`) keeps each class single-purpose and gives FR-3 a clean home for pool-busy/pool-idle structured properties.

#### Decision 4: Strongly-typed `DbResilienceOptions`, not raw `IConfiguration`
**Options considered:**
- (A) Read `Database:Resilience:MaxRetryAttempts` etc. directly via `IConfiguration.GetValue<>` inside the provider.
- (B) Bind to a `DbResilienceOptions` POCO via `services.Configure<DbResilienceOptions>(...)` and inject `IOptions<DbResilienceOptions>`.

**Chosen approach:** B.

**Rationale:** Matches the codebase's existing `HangfireOptions` pattern (`backend/src/Anela.Heblo.Xcc/HangfireOptions.cs`) and the global C# patterns rule (Options Pattern). Single source of validation, easier unit-test seam.

#### Decision 5: Npgsql Meter registered via `IMeterFactory`, not raw `Meter` static
**Rationale:** Npgsql 8 exposes its counters via a `Meter` named `"Npgsql"`. Application Insights' built-in `Meter`-to-AI bridge picks it up automatically once the meter is registered with `IMeterFactory`. The custom `DbResilienceMetrics` (retry counters, pool exhaustion histogram) uses the same `IMeterFactory.Create("Anela.Heblo.Database.Resilience")` pattern as `SmartsuppWebhookMetrics`. **No new sink, no OpenTelemetry SDK** — the existing AI configuration in `ApplicationInsightsExtensions.cs` handles it.

#### Decision 6: Connection-string parameters in code, not in raw connection strings in Key Vault
**Options considered:**
- (A) Bake `Keepalive=30;Tcp Keepalive=true;Connection Idle Lifetime=60` into the connection string stored in Key Vault.
- (B) Keep the Key Vault connection string minimal (host/user/password/db) and set the parameters via `NpgsqlDataSourceBuilder.ConnectionStringBuilder` in code.

**Chosen approach:** B for tunables (pool, idle lifetime, pruning, keepalive), with `Database:` JSON config driving them. Connection string in Key Vault stays as host/credentials/db only.

**Rationale:** The existing code already does this (`PersistenceModule.cs:36-63`) and explicitly sets `KeepAlive = 30` and `ConnectionLifetime = 600` in code. FR-2 should extend `appsettings.*.json:Database` and the builder block — not push every tunable into Key Vault, which would require a secret rotation to change a timeout. This narrows FR-2's acceptance criterion: only `MaxPoolSize`, `Connection Idle Lifetime`, and (newly) `Connection Pruning Interval` are configurable; `Keepalive`/`Tcp Keepalive` are code-defaulted (and already are, line 50).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Persistence/
├── Anela.Heblo.Persistence.csproj                 # + Polly 8.4.1, Polly.Extensions 8.4.1
├── PersistenceModule.cs                           # MODIFY: wire execution strategy + metrics
└── Infrastructure/
    ├── PostgresExceptionLoggingInterceptor.cs     # existing — unchanged
    ├── PostgresExceptionTranslator.cs             # existing — unchanged
    └── Resilience/                                # NEW folder
        ├── DbResilienceOptions.cs                 # POCO bound from "Database:Resilience"
        ├── IDbResiliencePipelineProvider.cs       # interface
        ├── DbResiliencePipelineProvider.cs        # singleton, builds ResiliencePipeline once
        ├── PollyExecutionStrategy.cs              # IExecutionStrategy
        ├── DbResilienceMetrics.cs                 # IMeterFactory-backed counters
        ├── NpgsqlConnectionInterceptor.cs         # DbConnectionInterceptor for FR-3
        └── TransientErrorClassifier.cs            # static — owns the SqlState allow/deny lists

backend/src/Anela.Heblo.Persistence.Analytics/
└── AnalyticsPersistenceModule.cs                  # MODIFY: same execution strategy wiring

backend/src/Anela.Heblo.API/
├── appsettings.Production.json                    # MODIFY: MaxPoolSize 15→20, add Database:Resilience block
├── appsettings.Staging.json                       # MODIFY: add Database:Resilience block (leave pool=10)
├── appsettings.json                               # MODIFY: defaults for Database:Resilience
└── Extensions/
    └── ApplicationInsightsExtensions.cs           # MODIFY: register "Npgsql" meter for export

backend/test/Anela.Heblo.Tests/
└── Persistence/Resilience/                        # NEW
    ├── TransientErrorClassifierTests.cs           # unit — SqlState allow/deny matrix
    ├── PollyExecutionStrategyTests.cs             # unit — pipeline invocation, no-retry on non-transient
    └── DbResiliencePipelineProviderTests.cs       # unit — backoff / max attempts / time budget

docs/
├── architecture/infrastructure.md                 # APPEND: Action Group + alert rules
├── architecture/environments.md                   # APPEND: FR-1 audit table + final values
└── integrations/db-connection-health-kql.md       # NEW — KQL snippet for FR-3
```

### Interfaces and Contracts

```csharp
// DbResilienceOptions.cs
public sealed class DbResilienceOptions
{
    public const string SectionName = "Database:Resilience";
    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan TotalTimeBudget { get; init; } = TimeSpan.FromSeconds(10);
}

// IDbResiliencePipelineProvider.cs
public interface IDbResiliencePipelineProvider
{
    ResiliencePipeline Pipeline { get; }
}

// PollyExecutionStrategy.cs
public sealed class PollyExecutionStrategy : IExecutionStrategy
{
    public PollyExecutionStrategy(ExecutionStrategyDependencies deps,
                                  IDbResiliencePipelineProvider pipelineProvider);

    public bool RetriesOnFailure => true;

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded);

    public Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken);
}

// TransientErrorClassifier.cs
public static class TransientErrorClassifier
{
    // SqlState families (PostgreSQL Error Code reference, §57 & §08)
    private static readonly HashSet<string> TransientCodes = new()
    {
        "57P01", "57P02", "57P03",                    // admin_shutdown / crash_shutdown / cannot_connect_now
    };
    private static readonly HashSet<string> NonTransientLogicalCodes = new()
    {
        "23505", "23503", "23502"                     // unique / FK / not-null
    };

    public static bool IsTransient(Exception ex);   // true → retry
    public static bool IsNonTransientLogical(Exception ex);  // true → never retry, even nested
}

// DbResilienceMetrics.cs   (IMeterFactory pattern, same as SmartsuppWebhookMetrics)
public sealed class DbResilienceMetrics : IDisposable
{
    public const string MeterName = "Anela.Heblo.Database.Resilience";
    public void RecordRetryAttempt(string exceptionType, int attempt);
    public void RecordRetrySuccess(int totalAttempts, double totalDelayMs);
    public void RecordRetryFailure(string exceptionType, int totalAttempts);
    public void RecordPoolExhaustionWait(double waitMs);   // only when wait > 1s
}
```

### Data Flow

**Happy-path (no retry):**
```
Handler ──► Repository ──► DbContext.SaveChangesAsync()
                                  │
                                  ▼
                EF Core: IExecutionStrategy.ExecuteAsync()
                                  │
                                  ▼
                  PollyExecutionStrategy.ExecuteAsync()
                                  │
                                  ▼
                ResiliencePipeline.ExecuteAsync(op)         ← passthrough, ≤1 ms overhead
                                  │
                                  ▼
                       Npgsql ──► Azure PostgreSQL
```

**Transient failure with retry:**
```
Npgsql throws PostgresException(SqlState=57P03) at attempt 1
        │
        ▼
RetryStrategy.ShouldHandle(ex) → TransientErrorClassifier.IsTransient(ex) → true
        │
        ▼
DbResilienceMetrics.RecordRetryAttempt(...)
ILogger.LogWarning("DbTransientRetry attempt={1} delay={…} exception.type={…}")
        │
        ▼
await Task.Delay(baseDelay * 2^0 + jitter)
        │
        ▼
Operation re-invoked by ResiliencePipeline (EF rebuilds command + opens new conn from pool)
        │
        ▼
Success → DbResilienceMetrics.RecordRetrySuccess(...)
Final failure (3 attempts exhausted, or 10s budget hit) → RecordRetryFailure(...)
                                                        → LogError("DbTransientRetryExhausted")
                                                        → original exception rethrown
```

**Logical conflict (non-transient):**
```
DbUpdateException(inner: PostgresException SqlState=23505)
        │
        ▼
ShouldHandle → IsNonTransientLogical → true → return false (do NOT retry)
        │
        ▼
Exception bubbles unchanged → existing PostgresExceptionLoggingInterceptor logs SqlState
```

### Wiring in `PersistenceModule.AddPersistenceServices`

```csharp
services.Configure<DbResilienceOptions>(configuration.GetSection(DbResilienceOptions.SectionName));
services.AddSingleton<DbResilienceMetrics>();
services.AddSingleton<IDbResiliencePipelineProvider, DbResiliencePipelineProvider>();
services.AddScoped<NpgsqlConnectionInterceptor>();

services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    if (useInMemory || connectionString == "InMemory")
    {
        options.UseInMemoryDatabase("TestDatabase");
    }
    else
    {
        options.UseNpgsql(dataSource!, npgsql =>
        {
            // EF Core 8 contract: factory must construct strategy per-context
            npgsql.ExecutionStrategy(deps =>
                new PollyExecutionStrategy(
                    deps,
                    sp.GetRequiredService<IDbResiliencePipelineProvider>()));
        });
        options.AddInterceptors(
            sp.GetRequiredService<PostgresExceptionLoggingInterceptor>(),
            sp.GetRequiredService<NpgsqlConnectionInterceptor>());
    }
});
```

Apply the same `ExecutionStrategy(...)` wiring to `AnalyticsPersistenceModule` so the analytics pool is also protected. (Both pools draw from the same Azure server's `max_connections`.)

### Connection-budget table (FR-1 / FR-2)

Document this in `docs/architecture/environments.md`:

| Source | Pool size (after change) | Notes |
|---|---|---|
| `ApplicationDbContext` (`Database:MaxPoolSize` prod) | **20** (was 15) | Main EF Core pool |
| `AnalyticsDbContext` | Npgsql default (100) — **set explicit MaxPoolSize=10** | Currently unbounded; FR-2 must cap it |
| Hangfire (`Hangfire:ConnectionLimit`) | 5 | Already isolated via cloned connection string |
| Admin / migrations headroom | ~5 | Reserved |
| **Ceiling** | **40** | Verify ≤ `max_connections − 60` after `SHOW max_connections` |

The spec's FR-1 acceptance criterion must explicitly include `AnalyticsDbContext` — currently uncapped and a hidden contributor to the total.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Hidden write transactions break execution-strategy contract** | HIGH | Spec asserts no `BeginTransaction`/`UseTransaction` callers exist. Add a CI grep gate (`scripts/check-no-managed-tx.sh`) so future regressions fail fast. |
| **Retry replays a partially-applied `SaveChangesAsync`** | MEDIUM | EF Core resets change tracker between strategy retries; safe for transient `Connect`/`ReadMessageLong` failures (no transaction committed). Document explicitly in the strategy's XML doc. |
| **`AnalyticsDbContext` uncapped pool exhausts server budget** | MEDIUM | FR-1 must include analytics pool. FR-2 adds `MaxPoolSize=10` to its `NpgsqlDataSourceBuilder`. |
| **`MaterialContainerCodeGenerator` opens raw `NpgsqlConnection` outside EF** | MEDIUM | Confirm whether it draws from the shared `NpgsqlDataSource` (good — pool keepalive applies) or builds its own (would bypass keepalive). Inspect during FR-1; add to audit table. |
| **Adaptive AI sampling drops new metric points** | LOW | `Meter`-based metrics use `MetricTelemetry`, which is excluded from adaptive sampling. Verify in `ApplicationInsightsExtensions.cs:71` (`excludedTypes: "Exception;Event"` — add `Metric` if not already implicit). |
| **Polly v8 pipeline rebuild on each request** | LOW | `DbResiliencePipelineProvider` is `Singleton`. The `ResiliencePipeline` is thread-safe and built once at startup. |
| **EF Core 8 execution-strategy factory signature drift** | LOW | `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4` is the pinned version. Lock to this in csproj and pin the strategy signature against the EF Core 8 surface (`ExecutionStrategyDependencies`). |
| **`Include Error Detail` left `true` accidentally** | MEDIUM | Add unit test that asserts production connection-string parser sees `Include Error Detail=false`. NFR-2 explicit. |
| **Logging the connection string in retry handler** | HIGH | `OnRetry` callback must scope-down to `pgEx.SqlState`, `exception.Message`, `attempt`, `delay` — never `Context.OperationKey` if it can carry the connection. Code review gate. |

## Specification Amendments

1. **FR-1: add `AnalyticsDbContext` connection pool to the audit scope.** Currently uncapped (no `MaxPoolSize` passed to `NpgsqlDataSourceBuilder` in `AnalyticsPersistenceModule.cs:13-16`). The spec's "total connection budget" cannot be computed without it.

2. **FR-1: explicitly inspect `MaterialContainerCodeGenerator`** for raw `NpgsqlConnection` use. If it opens its own connection it bypasses the execution strategy; the audit must record whether the keepalive/lifetime settings apply.

3. **FR-2: clarify which connection-string params live in Key Vault vs. code.** Recommend: Key Vault holds only host/user/password/db; tunables (`Keepalive`, `Tcp Keepalive`, `Connection Idle Lifetime`, `Connection Pruning Interval`, `Include Error Detail`) live in `appsettings.*.json:Database` and `NpgsqlDataSourceBuilder`. This matches the current code (`PersistenceModule.cs:50-63`) and avoids secret rotation for every timeout tweak.

4. **FR-2: also cap `AnalyticsDbContext` to `MaxPoolSize=10`** in `appsettings.*.json:AnalyticsDatabase:MaxPoolSize` and apply via `AnalyticsPersistenceModule`. Otherwise the total connection budget cannot be enforced.

5. **FR-3: extend the new `NpgsqlConnectionInterceptor` to also wire `AnalyticsDbContext`,** so both DbContexts emit the same structured properties. Otherwise alerting (FR-5) has blind spots.

6. **FR-4: spec says register Polly pipeline as singleton via `IDbResiliencePipelineProvider`.** Add: register `DbResilienceMetrics` as `Singleton` and `NpgsqlConnectionInterceptor` as `Scoped` (matches `PostgresExceptionLoggingInterceptor`).

7. **FR-4: spec requires Polly to be added to the Persistence project.** Add `<PackageReference Include="Polly" Version="8.4.1" />` and `<PackageReference Include="Polly.Extensions" Version="8.4.1" />` to `Anela.Heblo.Persistence.csproj` directly. **Do not** add a `ProjectReference` to `Anela.Heblo.Application` (inverts the dependency graph).

8. **NFR-3 / FR-3: register the `"Npgsql"` Meter name with the Application Insights bridge.** AI's adaptive sampling bridge only forwards meters it knows about. Update `ApplicationInsightsExtensions.cs` (or document the App Service env var `OTEL_METRICS_EXPORTER_INCLUDED_METERS` if the auto-bridge does not pick it up).

9. **FR-5: tighten alert query scope.** "Originating in Npgsql stack frames" is fuzzy in KQL. Recommend: `exceptions | where type startswith "Npgsql." or (type == "System.Net.Sockets.SocketException" and outerAssembly startswith "Npgsql")`. Capture the exact KQL in `docs/integrations/db-connection-health-kql.md`.

## Prerequisites

Before implementation can start:

1. **Verify Npgsql version supports `Meter` counters.** Project pins `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4`, which pulls Npgsql ≥ 8.0.x. Npgsql added `System.Diagnostics.Metrics` support in 8.0; confirm during FR-1.
2. **Confirm Azure PostgreSQL SKU and observed `max_connections`** via `az postgres flexible-server show -g rgHeblo -n <server>` (spec assumes Burstable B2s / 85 connections; if larger SKU, FR-2 numbers re-baseline).
3. **Confirm Action Group `ag-heblo-prod-default` exists** in `rgHeblo`; create with `az monitor action-group create` if absent (FR-5).
4. **Confirm Application Insights resources `aiHeblo` (prod) and `aiHeblo-test` (staging)** are live and wired to App Settings as documented in `docs/architecture/observability.md` (they are).
5. **Add a CI guard** (one-line script) that fails the build if `BeginTransaction|UseTransaction` appears in `backend/src` — prevents future code from silently breaking the execution-strategy contract.
6. **Read `docs/architecture/development_guidelines.md` §Persistence** before adding the new `Infrastructure/Resilience/` folder — confirms folder placement matches the project's filesystem conventions.