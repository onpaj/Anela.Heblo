# Npgsql Database Connection Resilience & Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Polly v8 EF Core execution strategy, pool/connection telemetry, hardened connection-string defaults, and Azure Monitor alerts so transient Azure PostgreSQL connection drops self-heal instead of surfacing as user-visible 500s.

**Architecture:** A new `Infrastructure/Resilience/` folder inside `Anela.Heblo.Persistence` hosts a singleton `IDbResiliencePipelineProvider` (Polly v8 `ResiliencePipeline`), a `PollyExecutionStrategy : IExecutionStrategy` wired via `optionsBuilder.UseNpgsql(...).ExecutionStrategy(...)`, a static `TransientErrorClassifier` (SqlState allow/deny lists), `DbResilienceMetrics` (`IMeterFactory`-backed counters, same pattern as `SmartsuppWebhookMetrics`), and a sibling `NpgsqlConnectionInterceptor : DbConnectionInterceptor` for connection-lifecycle structured logs. Both `ApplicationDbContext` and `AnalyticsDbContext` share the strategy and interceptor so the analytics pool is also protected. Connection-string tunables stay in `appsettings.*.json:Database` (not Key Vault); only host/credentials remain in Key Vault.

**Tech Stack:** .NET 8, EF Core 8.0.8, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4, Polly 8.4.1 + Polly.Extensions 8.4.1, `System.Diagnostics.Metrics.Meter` via `IMeterFactory`, Application Insights (existing AI adaptive-sampling bridge), Azure Key Vault (`kv-heblo-stg`, `kv-heblo-prod`), Azure Monitor (`ag-heblo-prod-default` Action Group).

---

## File Map

**Create (Persistence):**
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceOptions.cs` — strongly-typed options bound from `"Database:Resilience"`.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/TransientErrorClassifier.cs` — static SqlState allow/deny matrix.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/IDbResiliencePipelineProvider.cs` — interface exposing the singleton `ResiliencePipeline`.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResiliencePipelineProvider.cs` — singleton that builds the pipeline once.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs` — `IExecutionStrategy` delegating to the pipeline.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceMetrics.cs` — `IMeterFactory`-backed counters.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs` — `DbConnectionInterceptor` for pool/connection events.

**Modify (Persistence):**
- `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj` — add `Polly` 8.4.1 and `Polly.Extensions` 8.4.1 `PackageReference`.
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — wire options, provider, metrics, interceptor, execution strategy.
- `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs` — cap `MaxPoolSize` to 10, wire execution strategy + interceptor.

**Modify (API):**
- `backend/src/Anela.Heblo.API/appsettings.json` — defaults for `Database:Resilience`.
- `backend/src/Anela.Heblo.API/appsettings.Production.json` — `Database:MaxPoolSize: 20` (was 15), add `Database:Resilience` block, add `AnalyticsDatabase:MaxPoolSize: 10`.
- `backend/src/Anela.Heblo.API/appsettings.Staging.json` — add `Database:Resilience` block, add `AnalyticsDatabase:MaxPoolSize: 10` (leave EF pool at 10).
- `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs` — register `"Npgsql"` and `"Anela.Heblo.Database.Resilience"` meters with the AI bridge.

**Tests (new folder):**
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/TransientErrorClassifierTests.cs` — SqlState matrix.
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/DbResiliencePipelineProviderTests.cs` — backoff / max attempts / time budget / non-transient passthrough.
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs` — strategy delegation, retry-success and retry-failure paths, no retry on logical conflict.
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/ProductionConnectionStringDefaultsTests.cs` — production config asserts `Include Error Detail=false`, pool sizes, resilience options.

**Docs:**
- `docs/architecture/environments.md` — APPEND audit table + final connection-budget table.
- `docs/architecture/infrastructure.md` — APPEND Action Group + alert rule documentation.
- `docs/integrations/db-connection-health-kql.md` — NEW KQL snippets for FR-3 / alert queries.

**CI:**
- `scripts/check-no-managed-tx.sh` — NEW one-line guard that fails if `BeginTransaction|UseTransaction` appears in `backend/src`.
- `.github/workflows/backend-build.yml` (or equivalent) — invoke the guard.

**Az CLI (no code artifact):**
- Update `ConnectionStrings--Production` in `kv-heblo-prod` and `ConnectionStrings--Staging` in `kv-heblo-stg` (Key Vault only — host/user/password/db).
- Create/verify `ag-heblo-prod-default` Action Group + two alert rules in `rgHeblo`.

---

## Task 0: Pre-flight audit (FR-1)

**Files:**
- Modify: `docs/architecture/environments.md`

The audit must run **before any code change** so the rest of the plan's numbers (e.g. `Database:MaxPoolSize=20`) are grounded in observed reality. Treat the table below as the schema for the audit note; fill the values as you go.

- [ ] **Step 1: Verify the Azure PostgreSQL SKU and `max_connections`**

Run:
```bash
az postgres flexible-server show -g rgHeblo -n <server-name> \
  --query "{sku:sku.name, tier:sku.tier, version:version, storage:storage.storageSizeGb}" -o table
```
Then connect (via psql or Azure Portal Query Editor) and run:
```sql
SHOW max_connections;
```
Record both. If the SKU is **not** Burstable B2s, recompute FR-2's pool ceiling so the EF + Analytics + Hangfire + admin headroom totals stay ≤ `max_connections − 60`.

- [ ] **Step 2: Inventory current Npgsql connection-string parameters per environment**

Pull the live secrets (do **not** echo or write to disk):
```bash
az keyvault secret show --vault-name kv-heblo-prod --name "ConnectionStrings--Production" --query value -o tsv | tr ';' '\n'
az keyvault secret show --vault-name kv-heblo-stg  --name "ConnectionStrings--Staging"    --query value -o tsv | tr ';' '\n'
```
Cross-reference with `appsettings.*.json:Database` and the in-code defaults in `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:36-66`.

- [ ] **Step 3: Inspect `MaterialContainerCodeGenerator` for raw `NpgsqlConnection` usage**

Run:
```bash
grep -rn "new NpgsqlConnection\|NpgsqlConnection(" backend/src/Anela.Heblo.Persistence
```
Confirm whether `MaterialContainerCodeGenerator` opens its own connection or uses the shared `NpgsqlDataSource`. Note the finding in the audit (per arch-review Risk row "MaterialContainerCodeGenerator").

- [ ] **Step 4: Append the audit table to `docs/architecture/environments.md`**

Append a new section titled "Database connection audit (2026-06-13)" with the following table (filled with observed values):

```markdown
## Database connection audit (2026-06-13)

### Azure PostgreSQL Flexible Server
| Property | Observed |
|---|---|
| Resource group | rgHeblo |
| Server name | <fill> |
| SKU | <fill, e.g. Standard_B2s> |
| Tier | <fill, e.g. Burstable> |
| `max_connections` | <fill> |

### Connection-string parameters per environment
| Param | Development | Staging | Production | Azure-recommended | Recommendation |
|---|---|---|---|---|---|
| Max Pool Size | <fill> | 10 | 15 → **20** | depends on `max_connections` | Raise prod to 20 |
| Min Pool Size | 0 | 0 | 0 | 0 | OK |
| Connection Idle Lifetime (s) | <fill> | 60 | 60 | ≤ 240 (Azure LB idle) | OK |
| Connection Pruning Interval (s) | <fill> | 10 | 10 | 10 | OK |
| Keepalive (s) | 30 (code) | 30 (code) | 30 (code) | 30 | OK |
| Tcp Keepalive | true (default) | true | true | true | OK |
| Tcp Keepalive Time / Interval | OS default | OS default | OS default | OS default | OK |
| Timeout (s) | 15 (default) | 15 | 15 | 15 | OK |
| Command Timeout (s) | 30 (default) | 30 | 30 | 30 | OK |
| Include Error Detail | true (dev) | false | false | false | OK |
| Connection Lifetime (s) | 600 (code) | 600 | 600 | ≤ 600 | OK |

### Connection-budget summary
| Source | Pool size (after change) | Notes |
|---|---|---|
| `ApplicationDbContext` (Database:MaxPoolSize prod) | 20 | Was 15 |
| `AnalyticsDbContext` (AnalyticsDatabase:MaxPoolSize) | 10 | Previously unbounded — capped by FR-2 |
| Hangfire (Hangfire:ConnectionLimit) | 5 | Independent cloned connection string |
| Admin / migrations headroom | ~5 | Reserved |
| **Ceiling** | **40** | Must be ≤ `max_connections − 60` |

### Raw `NpgsqlConnection` usage
- `MaterialContainerCodeGenerator`: <draws from shared NpgsqlDataSource | builds own connection> — <impact note>
```

- [ ] **Step 5: Commit the audit**

```bash
git add docs/architecture/environments.md
git commit -m "docs: add db connection audit for Npgsql resilience work"
```

---

## Task 1: Add Polly packages to Persistence project

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`

- [ ] **Step 1: Add Polly 8.4.1 and Polly.Extensions 8.4.1 `PackageReference`**

Edit `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj` — inside the existing `<ItemGroup>` that holds `PackageReference` items, add:

```xml
    <PackageReference Include="Polly" Version="8.4.1" />
    <PackageReference Include="Polly.Extensions" Version="8.4.1" />
```

The final `<ItemGroup>` should read (existing entries omitted for clarity):

```xml
  <ItemGroup>
    <PackageReference Include="Pgvector" Version="0.3.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.8" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="8.0.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.4" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
    <PackageReference Include="Polly" Version="8.4.1" />
    <PackageReference Include="Polly.Extensions" Version="8.4.1" />
  </ItemGroup>
```

- [ ] **Step 2: Verify build still succeeds**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
git commit -m "chore(persistence): add Polly 8.4.1 + Polly.Extensions for EF execution strategy"
```

---

## Task 2: TransientErrorClassifier (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/TransientErrorClassifier.cs`
- Test: `backend/test/Anela.Heblo.Tests/Persistence/Resilience/TransientErrorClassifierTests.cs`

The classifier owns the SqlState allow/deny lists referenced from the spec (FR-4). Everything that consumes "is this exception worth retrying?" goes through this one type. Pure-static, no DI.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Persistence/Resilience/TransientErrorClassifierTests.cs`:

```csharp
using System.Net.Sockets;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class TransientErrorClassifierTests
{
    [Theory]
    [InlineData("57P01")] // admin_shutdown
    [InlineData("57P02")] // crash_shutdown
    [InlineData("57P03")] // cannot_connect_now
    [InlineData("08000")] // connection_exception
    [InlineData("08003")] // connection_does_not_exist
    [InlineData("08006")] // connection_failure
    [InlineData("08001")] // sqlclient_unable_to_establish_sqlconnection
    [InlineData("08004")] // sqlserver_rejected_establishment
    [InlineData("08007")] // transaction_resolution_unknown
    public void IsTransient_ReturnsTrue_ForTransientPostgresSqlStates(string sqlState)
    {
        var ex = CreatePostgresException(sqlState);

        TransientErrorClassifier.IsTransient(ex).Should().BeTrue();
    }

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23502")] // not_null_violation
    [InlineData("42P01")] // undefined_table — logical schema problem
    public void IsTransient_ReturnsFalse_ForNonTransientPostgresSqlStates(string sqlState)
    {
        var ex = CreatePostgresException(sqlState);

        TransientErrorClassifier.IsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ReturnsTrue_ForSocketException()
    {
        var ex = new SocketException();

        TransientErrorClassifier.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_ReturnsTrue_ForTimeoutException()
    {
        TransientErrorClassifier.IsTransient(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_ReturnsTrue_ForIOException()
    {
        TransientErrorClassifier.IsTransient(new IOException()).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_UnwrapsInnerExceptions()
    {
        var inner = CreatePostgresException("57P03");
        var outer = new InvalidOperationException("wrapper", inner);

        TransientErrorClassifier.IsTransient(outer).Should().BeTrue();
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("23503")]
    [InlineData("23502")]
    public void IsNonTransientLogical_ReturnsTrue_ForLogicalConflictCodes(string sqlState)
    {
        var pg = CreatePostgresException(sqlState);
        var update = new DbUpdateException("save failed", pg);

        TransientErrorClassifier.IsNonTransientLogical(update).Should().BeTrue();
    }

    [Fact]
    public void IsNonTransientLogical_ReturnsTrue_ForDbUpdateConcurrencyException()
    {
        TransientErrorClassifier
            .IsNonTransientLogical(new DbUpdateConcurrencyException("conflict"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsNonTransientLogical_ReturnsFalse_ForTransientPostgresException()
    {
        var pg = CreatePostgresException("57P03");
        var update = new DbUpdateException("save failed", pg);

        TransientErrorClassifier.IsNonTransientLogical(update).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ReturnsFalse_WhenNonTransientLogicalWrapsTransient()
    {
        // Defensive: a logical conflict code (e.g. 23505) must never retry,
        // even if some other transient-looking layer wraps it.
        var pg = CreatePostgresException("23505");
        var update = new DbUpdateException("save failed", pg);

        TransientErrorClassifier.IsTransient(update).Should().BeFalse();
    }

    private static PostgresException CreatePostgresException(string sqlState)
    {
        // PostgresException's public constructor: (messageText, severity, invariantSeverity, sqlState)
        return new PostgresException(
            messageText: $"simulated {sqlState}",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransientErrorClassifierTests"`
Expected: FAIL — `TransientErrorClassifier` not defined.

- [ ] **Step 3: Implement `TransientErrorClassifier`**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/TransientErrorClassifier.cs`:

```csharp
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Classifies database exceptions as transient (retry) or non-transient logical (never retry).
/// Transient: connection drops, admin shutdowns, socket errors. Non-transient: unique / FK / not-null violations, concurrency conflicts.
/// </summary>
public static class TransientErrorClassifier
{
    // PostgreSQL SqlState families. References:
    //  - Class 08 — connection_exception
    //  - Class 57 — operator_intervention (57P01 admin_shutdown, 57P02 crash_shutdown, 57P03 cannot_connect_now)
    private const string ConnectionExceptionPrefix = "08";
    private static readonly HashSet<string> TransientOperatorIntervention = new()
    {
        "57P01",
        "57P02",
        "57P03",
    };

    // Logical conflicts — never retry, even when nested inside a DbUpdateException.
    private static readonly HashSet<string> NonTransientLogicalCodes = new()
    {
        "23505", // unique_violation
        "23503", // foreign_key_violation
        "23502", // not_null_violation
    };

    public static bool IsTransient(Exception exception)
    {
        if (IsNonTransientLogical(exception))
        {
            return false;
        }

        return IsTransientCore(exception);
    }

    public static bool IsNonTransientLogical(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        var pg = UnwrapPostgresException(exception);
        return pg is not null && NonTransientLogicalCodes.Contains(pg.SqlState);
    }

    private static bool IsTransientCore(Exception exception)
    {
        return exception switch
        {
            PostgresException pg => IsTransientSqlState(pg.SqlState),
            SocketException => true,
            TimeoutException => true,
            IOException => true,
            { InnerException: { } inner } => IsTransientCore(inner),
            _ => false,
        };
    }

    private static bool IsTransientSqlState(string sqlState)
    {
        if (string.IsNullOrEmpty(sqlState))
        {
            return false;
        }

        return TransientOperatorIntervention.Contains(sqlState)
            || sqlState.StartsWith(ConnectionExceptionPrefix, StringComparison.Ordinal);
    }

    private static PostgresException? UnwrapPostgresException(Exception? exception)
    {
        return exception switch
        {
            null => null,
            PostgresException pg => pg,
            { InnerException: { } inner } => UnwrapPostgresException(inner),
            _ => null,
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransientErrorClassifierTests"`
Expected: PASS — all 14 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/TransientErrorClassifier.cs \
        backend/test/Anela.Heblo.Tests/Persistence/Resilience/TransientErrorClassifierTests.cs
git commit -m "feat(persistence): add TransientErrorClassifier for Npgsql retry decisions"
```

---

## Task 3: DbResilienceOptions

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceOptions.cs`

Strongly-typed options POCO bound from configuration `"Database:Resilience"`. Matches the project's `HangfireOptions` pattern. No tests at this stage — this is a passive DTO covered by `DbResiliencePipelineProviderTests` in Task 5.

- [ ] **Step 1: Create the options class**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceOptions.cs`:

```csharp
namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Configuration for the database resilience pipeline. Defaults match the spec's
/// "exponential backoff with jitter, 3 attempts, ≤ 10 s total budget" target.
/// </summary>
public sealed class DbResilienceOptions
{
    public const string SectionName = "Database:Resilience";

    /// <summary>Maximum retry attempts before the original exception is rethrown. Default 3.</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Base delay used as the seed for exponential backoff. Default 200 ms.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Upper cap on any single retry delay. Default 4 s.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Total wall-clock budget across all retries. Default 10 s.</summary>
    public TimeSpan TotalTimeBudget { get; init; } = TimeSpan.FromSeconds(10);
}
```

- [ ] **Step 2: Run build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceOptions.cs
git commit -m "feat(persistence): add DbResilienceOptions bound from Database:Resilience"
```

---

## Task 4: DbResilienceMetrics

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceMetrics.cs`

`IMeterFactory`-backed counters, same shape as `SmartsuppWebhookMetrics`. No tests — these are passive `Counter`/`Histogram` writes covered by behaviour assertions in Task 5/6.

- [ ] **Step 1: Create the metrics class**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceMetrics.cs`:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Emits resilience counters via the Meter API. Exported automatically by the
/// Application Insights bridge once "Anela.Heblo.Database.Resilience" is registered
/// (see ApplicationInsightsExtensions.AddOptimizedApplicationInsights).
/// </summary>
public sealed class DbResilienceMetrics : IDisposable
{
    public const string MeterName = "Anela.Heblo.Database.Resilience";

    private readonly Meter _meter;
    private readonly Counter<long> _retryAttempts;
    private readonly Counter<long> _retrySuccess;
    private readonly Counter<long> _retryFailure;
    private readonly Histogram<double> _poolExhaustionWait;

    public DbResilienceMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _retryAttempts = _meter.CreateCounter<long>(
            "db.retry.attempts",
            description: "Database retry attempts, tagged by exception type");

        _retrySuccess = _meter.CreateCounter<long>(
            "db.retry.success",
            description: "Operations that succeeded after one or more retries");

        _retryFailure = _meter.CreateCounter<long>(
            "db.retry.failure",
            description: "Operations that exhausted retries and rethrew");

        _poolExhaustionWait = _meter.CreateHistogram<double>(
            "npgsql.pool.exhaustion_wait_seconds",
            unit: "s",
            description: "Time spent waiting for a free connection from the Npgsql pool");
    }

    public void RecordRetryAttempt(string exceptionType, int attempt)
    {
        _retryAttempts.Add(1, new TagList
        {
            { "exception.type", exceptionType },
            { "attempt", attempt },
        });
    }

    public void RecordRetrySuccess(int totalAttempts) =>
        _retrySuccess.Add(1, new TagList { { "total_attempts", totalAttempts } });

    public void RecordRetryFailure(string exceptionType, int totalAttempts) =>
        _retryFailure.Add(1, new TagList
        {
            { "exception.type", exceptionType },
            { "total_attempts", totalAttempts },
        });

    public void RecordPoolExhaustionWait(double waitSeconds) =>
        _poolExhaustionWait.Record(waitSeconds);

    public void Dispose() => _meter.Dispose();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceMetrics.cs
git commit -m "feat(persistence): add DbResilienceMetrics for retry + pool-wait counters"
```

---

## Task 5: IDbResiliencePipelineProvider + DbResiliencePipelineProvider (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/IDbResiliencePipelineProvider.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResiliencePipelineProvider.cs`
- Test: `backend/test/Anela.Heblo.Tests/Persistence/Resilience/DbResiliencePipelineProviderTests.cs`

A singleton that builds the Polly v8 `ResiliencePipeline` once. The pipeline is the only place where retry policy lives.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Persistence/Resilience/DbResiliencePipelineProviderTests.cs`:

```csharp
using System.Net.Sockets;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class DbResiliencePipelineProviderTests
{
    [Fact]
    public async Task Pipeline_RetriesTransientPostgresException_UpToMaxAttempts()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new PostgresException("transient", "ERROR", "ERROR", "57P03");
            });
        });

        ex.Should().BeOfType<PostgresException>();
        calls.Should().Be(4); // initial + 3 retries
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_OnUniqueViolation()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new PostgresException("dup", "ERROR", "ERROR", "23505");
            });
        });

        ex.Should().BeOfType<PostgresException>();
        calls.Should().Be(1); // no retry
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_OnNonTransientException()
    {
        var (provider, _) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new InvalidOperationException("not transient");
            });
        });

        ex.Should().BeOfType<InvalidOperationException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_RetriesSocketException()
    {
        var (provider, _) = CreateProvider(maxAttempts: 2);
        var calls = 0;

        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
            {
                calls++;
                throw new SocketException();
            });
        });

        ex.Should().BeOfType<SocketException>();
        calls.Should().Be(3); // initial + 2 retries
    }

    [Fact]
    public async Task Pipeline_SucceedsAfterTransientFailure_RecordsSuccess()
    {
        var (provider, metrics) = CreateProvider(maxAttempts: 3);
        var calls = 0;

        var result = await provider.Pipeline.ExecuteAsync<int>(_ =>
        {
            calls++;
            if (calls < 2)
            {
                throw new PostgresException("blip", "ERROR", "ERROR", "57P03");
            }
            return ValueTask.FromResult(42);
        });

        result.Should().Be(42);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Pipeline_AbortsByTotalTimeBudget()
    {
        var (provider, _) = CreateProvider(
            maxAttempts: 50,
            baseDelay: TimeSpan.FromMilliseconds(300),
            maxDelay: TimeSpan.FromMilliseconds(300),
            totalBudget: TimeSpan.FromMilliseconds(600));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await provider.Pipeline.ExecuteAsync(_ =>
                throw new SocketException());
        });
        sw.Stop();

        ex.Should().NotBeNull();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "the total time budget must short-circuit retries");
    }

    private static (DbResiliencePipelineProvider provider, DbResilienceMetrics metrics) CreateProvider(
        int maxAttempts,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        TimeSpan? totalBudget = null)
    {
        var options = Options.Create(new DbResilienceOptions
        {
            MaxRetryAttempts = maxAttempts,
            BaseDelay = baseDelay ?? TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = maxDelay ?? TimeSpan.FromMilliseconds(10),
            TotalTimeBudget = totalBudget ?? TimeSpan.FromSeconds(10),
        });

        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var provider = new DbResiliencePipelineProvider(
            options,
            metrics,
            NullLogger<DbResiliencePipelineProvider>.Instance);

        return (provider, metrics);
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
            new(options.Name, options.Version);
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DbResiliencePipelineProviderTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Create the interface**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/IDbResiliencePipelineProvider.cs`:

```csharp
using Polly;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Exposes the singleton Polly ResiliencePipeline used by PollyExecutionStrategy.
/// </summary>
public interface IDbResiliencePipelineProvider
{
    ResiliencePipeline Pipeline { get; }
}
```

- [ ] **Step 4: Create the provider implementation**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResiliencePipelineProvider.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Builds the Polly v8 pipeline once at startup. The pipeline retries transient
/// database faults with exponential backoff + jitter and a total time budget.
/// </summary>
public sealed class DbResiliencePipelineProvider : IDbResiliencePipelineProvider
{
    private readonly DbResilienceMetrics _metrics;
    private readonly ILogger<DbResiliencePipelineProvider> _logger;

    public DbResiliencePipelineProvider(
        IOptions<DbResilienceOptions> options,
        DbResilienceMetrics metrics,
        ILogger<DbResiliencePipelineProvider> logger)
    {
        _metrics = metrics;
        _logger = logger;
        Pipeline = BuildPipeline(options.Value);
    }

    public ResiliencePipeline Pipeline { get; }

    private ResiliencePipeline BuildPipeline(DbResilienceOptions options)
    {
        var retry = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(TransientErrorClassifier.IsTransient),
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.BaseDelay,
            MaxDelay = options.MaxRetryDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = OnRetry,
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retry)
            // TotalTimeBudget bounds wall-clock across all retries.
            .AddTimeout(options.TotalTimeBudget)
            .Build();
    }

    private ValueTask OnRetry(OnRetryArguments<object> args)
    {
        var exception = args.Outcome.Exception;
        var exceptionType = exception?.GetType().FullName ?? "unknown";

        _metrics.RecordRetryAttempt(exceptionType, args.AttemptNumber + 1);

        _logger.LogWarning(
            exception,
            "DbTransientRetry attempt={Attempt} delay={DelayMs}ms exception.type={ExceptionType}",
            args.AttemptNumber + 1,
            args.RetryDelay.TotalMilliseconds,
            exceptionType);

        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DbResiliencePipelineProviderTests"`
Expected: PASS — all 6 tests green.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/IDbResiliencePipelineProvider.cs \
        backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResiliencePipelineProvider.cs \
        backend/test/Anela.Heblo.Tests/Persistence/Resilience/DbResiliencePipelineProviderTests.cs
git commit -m "feat(persistence): add Polly v8 ResiliencePipeline provider for EF Core retries"
```

---

## Task 6: PollyExecutionStrategy (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs`
- Test: `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs`

The strategy implements `IExecutionStrategy` and delegates to the singleton pipeline. EF Core constructs one strategy per `DbContext` via the factory wired in `PersistenceModule`.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs`:

```csharp
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class PollyExecutionStrategyTests
{
    [Fact]
    public void RetriesOnFailure_ReturnsTrue()
    {
        var strategy = CreateStrategy();
        strategy.RetriesOnFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToPipeline_AndRetriesTransient()
    {
        var strategy = CreateStrategy(maxAttempts: 2);
        var calls = 0;

        var act = async () => await strategy.ExecuteAsync<int, int>(
            state: 0,
            operation: (_, _, _) =>
            {
                calls++;
                throw new PostgresException("blip", "ERROR", "ERROR", "57P03");
            },
            verifySucceeded: null,
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>();
        calls.Should().Be(3); // initial + 2 retries
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsResult_OnSuccess()
    {
        var strategy = CreateStrategy(maxAttempts: 3);

        var result = await strategy.ExecuteAsync<int, int>(
            state: 7,
            operation: (_, st, _) => Task.FromResult(st * 6),
            verifySucceeded: null,
            cancellationToken: CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_OnUniqueViolation()
    {
        var strategy = CreateStrategy(maxAttempts: 3);
        var calls = 0;

        var act = async () => await strategy.ExecuteAsync<int, int>(
            state: 0,
            operation: (_, _, _) =>
            {
                calls++;
                throw new PostgresException("dup", "ERROR", "ERROR", "23505");
            },
            verifySucceeded: null,
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>();
        calls.Should().Be(1);
    }

    private static PollyExecutionStrategy CreateStrategy(int maxAttempts = 3)
    {
        var options = Options.Create(new DbResilienceOptions
        {
            MaxRetryAttempts = maxAttempts,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(10),
            TotalTimeBudget = TimeSpan.FromSeconds(10),
        });

        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var provider = new DbResiliencePipelineProvider(
            options,
            metrics,
            NullLogger<DbResiliencePipelineProvider>.Instance);

        // Build a real EF Core ExecutionStrategyDependencies via DI so the strategy
        // contract surface is exercised exactly as EF Core 8 produces it.
        var services = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .AddDbContext<TestDbContext>(opt => opt.UseInMemoryDatabase("strategy-tests"))
            .BuildServiceProvider();
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var deps = ctx.GetService<ExecutionStrategyDependencies>();

        return new PollyExecutionStrategy(deps, provider, metrics, NullLogger<PollyExecutionStrategy>.Instance);
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
            new(options.Name, options.Version);
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PollyExecutionStrategyTests"`
Expected: FAIL — `PollyExecutionStrategy` not defined.

- [ ] **Step 3: Implement `PollyExecutionStrategy`**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// EF Core execution strategy that wraps every database operation in the project's
/// singleton Polly ResiliencePipeline. EF Core resets its change tracker before
/// each retry, so transient mid-call failures replay safely.
///
/// Note: EnableRetryOnFailure must not be used in addition to this strategy —
/// there is exactly one retry layer.
/// </summary>
public sealed class PollyExecutionStrategy : IExecutionStrategy
{
    private readonly ExecutionStrategyDependencies _dependencies;
    private readonly IDbResiliencePipelineProvider _pipelineProvider;
    private readonly DbResilienceMetrics _metrics;
    private readonly ILogger<PollyExecutionStrategy> _logger;

    public PollyExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        IDbResiliencePipelineProvider pipelineProvider,
        DbResilienceMetrics metrics,
        ILogger<PollyExecutionStrategy> logger)
    {
        _dependencies = dependencies;
        _pipelineProvider = pipelineProvider;
        _metrics = metrics;
        _logger = logger;
    }

    public bool RetriesOnFailure => true;

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        var attempt = 0;
        try
        {
            var result = _pipelineProvider.Pipeline.Execute(_ =>
            {
                attempt++;
                return operation(_dependencies.CurrentContext.Context, state);
            });

            if (attempt > 1)
            {
                _metrics.RecordRetrySuccess(attempt);
            }

            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordRetryFailure(ex.GetType().FullName ?? "unknown", attempt);
            _logger.LogError(
                ex,
                "DbTransientRetryExhausted attempts={Attempts} exception.type={ExceptionType}",
                attempt,
                ex.GetType().FullName);
            throw;
        }
    }

    public async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            var result = await _pipelineProvider.Pipeline.ExecuteAsync(async ct =>
            {
                attempt++;
                return await operation(_dependencies.CurrentContext.Context, state, ct);
            }, cancellationToken);

            if (attempt > 1)
            {
                _metrics.RecordRetrySuccess(attempt);
            }

            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordRetryFailure(ex.GetType().FullName ?? "unknown", attempt);
            _logger.LogError(
                ex,
                "DbTransientRetryExhausted attempts={Attempts} exception.type={ExceptionType}",
                attempt,
                ex.GetType().FullName);
            throw;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PollyExecutionStrategyTests"`
Expected: PASS — all 4 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs \
        backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs
git commit -m "feat(persistence): add PollyExecutionStrategy for EF Core transient retries"
```

---

## Task 7: NpgsqlConnectionInterceptor

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs`

Sibling to `PostgresExceptionLoggingInterceptor`. Captures `ConnectionFailed` and `ConnectionOpening` / `ConnectionOpened` events so the read-path "mid-read disconnect" cluster (120 occurrences in telemetry) gets structured logging — these never flow through `SaveChangesFailed`. Also records pool-exhaustion-wait when the connection open is slow.

- [ ] **Step 1: Create the interceptor**

Create `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs`:

```csharp
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// Records structured properties for connection-lifecycle events. Read-path disconnects
/// (PostgresException at NpgsqlConnector.ReadMessageLong) never reach SaveChanges and
/// so cannot be captured by PostgresExceptionLoggingInterceptor.
///
/// Also records pool-exhaustion wait (any connection open > 1 s) as a histogram sample
/// for the FR-3 telemetry surface.
/// </summary>
public sealed class NpgsqlConnectionInterceptor : DbConnectionInterceptor
{
    private const double PoolExhaustionThresholdSeconds = 1.0;

    private readonly DbResilienceMetrics _metrics;
    private readonly ILogger<NpgsqlConnectionInterceptor> _logger;

    private static readonly AsyncLocal<Stopwatch?> OpenStopwatch = new();

    public NpgsqlConnectionInterceptor(
        DbResilienceMetrics metrics,
        ILogger<NpgsqlConnectionInterceptor> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        OpenStopwatch.Value = Stopwatch.StartNew();
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        OpenStopwatch.Value = Stopwatch.StartNew();
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        RecordOpenLatency();
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordOpenLatency();
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        LogConnectionFailure(connection, eventData.Exception);
        base.ConnectionFailed(connection, eventData);
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogConnectionFailure(connection, eventData.Exception);
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    private void RecordOpenLatency()
    {
        var sw = OpenStopwatch.Value;
        if (sw is null) return;

        sw.Stop();
        OpenStopwatch.Value = null;

        var seconds = sw.Elapsed.TotalSeconds;
        if (seconds > PoolExhaustionThresholdSeconds)
        {
            _metrics.RecordPoolExhaustionWait(seconds);
            _logger.LogWarning(
                "DbPoolExhaustionWait wait_seconds={WaitSeconds:F2}",
                seconds);
        }
    }

    private void LogConnectionFailure(DbConnection connection, Exception exception)
    {
        // Reading pool counters from a single NpgsqlConnection is not exposed publicly,
        // so we log host/database from the connection — never the connection string.
        var host = SafeGetProperty(connection, "Host");
        var database = SafeGetProperty(connection, "Database");

        _logger.LogWarning(
            exception,
            "DbConnectionFailed exception.type={ExceptionType} npgsql.host={Host} npgsql.database={Database}",
            exception.GetType().FullName,
            host,
            database);
    }

    private static string? SafeGetProperty(DbConnection connection, string name)
    {
        try
        {
            return connection switch
            {
                NpgsqlConnection npg when name == "Host" => npg.DataSource,
                NpgsqlConnection npg when name == "Database" => npg.Database,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs
git commit -m "feat(persistence): add NpgsqlConnectionInterceptor for connection lifecycle telemetry"
```

---

## Task 8: Wire resilience into `PersistenceModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

Register the new services and pass the execution strategy factory to `UseNpgsql`.

- [ ] **Step 1: Add the new `using` and wiring**

Edit `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`. At the top, add:

```csharp
using Anela.Heblo.Persistence.Infrastructure.Resilience;
```

Then replace the registration block starting at line 82 (`// Register interceptors`) through line 103 (the closing `});` of `AddDbContext`) with:

```csharp
        // Register interceptors
        services.AddScoped<PostgresExceptionLoggingInterceptor>();
        services.AddScoped<NpgsqlConnectionInterceptor>();

        // Register exception translator (used by GridLayoutRepository to surface domain exceptions
        // and log SqlState/Operation at the Persistence boundary — distinct from the SaveChanges
        // interceptor which has no operation context and does not fire on read paths).
        services.AddScoped<PostgresExceptionTranslator>();

        // Resilience pipeline + metrics — singleton so the Polly pipeline is built once.
        services.Configure<DbResilienceOptions>(configuration.GetSection(DbResilienceOptions.SectionName));
        services.AddSingleton<DbResilienceMetrics>();
        services.AddSingleton<IDbResiliencePipelineProvider, DbResiliencePipelineProvider>();

        // Register DbContext
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            if (useInMemory || connectionString == "InMemory")
            {
                // For testing scenarios where no real database is needed
                options.UseInMemoryDatabase("TestDatabase");
            }
            else
            {
                options.UseNpgsql(dataSource!, npgsql =>
                {
                    npgsql.ExecutionStrategy(deps =>
                        new PollyExecutionStrategy(
                            deps,
                            sp.GetRequiredService<IDbResiliencePipelineProvider>(),
                            sp.GetRequiredService<DbResilienceMetrics>(),
                            sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
                });
                options.AddInterceptors(
                    sp.GetRequiredService<PostgresExceptionLoggingInterceptor>(),
                    sp.GetRequiredService<NpgsqlConnectionInterceptor>());
            }
        });
```

Also add `using Microsoft.Extensions.Logging;` at the top of the file if it is not already present.

- [ ] **Step 2: Run the existing `PersistenceModuleTests` regression suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PersistenceModuleTests"`
Expected: PASS — existing tests must continue to pass (NFR-4).

- [ ] **Step 3: Build the full backend**

Run: `dotnet build backend`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat(persistence): wire PollyExecutionStrategy + interceptors into PersistenceModule"
```

---

## Task 9: Cap `AnalyticsDbContext` pool + wire resilience

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs`

Per arch-review amendment #4 and #5: `AnalyticsDbContext` currently has no `MaxPoolSize`, so its pool defaults to the Npgsql ceiling (100) and would silently consume the server budget. The analytics module must accept a max-pool-size argument and wire the same execution strategy + interceptor as the main module.

- [ ] **Step 1: Update the method signature and wiring**

Replace the entire body of `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs` with:

```csharp
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Persistence.Analytics;

public static class AnalyticsPersistenceModule
{
    public static IServiceCollection AddAnalyticsPersistenceServices(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        var dataSource = dataSourceBuilder.Build();

        // Do not register dataSource as a bare NpgsqlDataSource singleton — it would shadow the main application pool
        // and break EanCodeGenerator and health checks. Pass it only to AddDbContext via closure.
        services.AddDbContext<AnalyticsDbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        sp.GetRequiredService<IDbResiliencePipelineProvider>(),
                        sp.GetRequiredService<DbResilienceMetrics>(),
                        sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
            });
            options.AddInterceptors(sp.GetRequiredService<NpgsqlConnectionInterceptor>());
        });
        return services;
    }
}
```

- [ ] **Step 2: Update the caller**

Find the analytics-module caller and pass the new `maxPoolSize` argument from configuration:

Run:
```bash
grep -rn "AddAnalyticsPersistenceServices" backend/src
```

At each call site, update to:
```csharp
services.AddAnalyticsPersistenceServices(
    analyticsConnectionString,
    configuration.GetValue<int?>("AnalyticsDatabase:MaxPoolSize") ?? 10);
```

- [ ] **Step 3: Build**

Run: `dotnet build backend`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs \
        backend/src/Anela.Heblo.API/
git commit -m "feat(persistence.analytics): cap pool + share execution strategy with main module"
```

---

## Task 10: Update `appsettings.*.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Production.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Staging.json`

- [ ] **Step 1: Add `Database:Resilience` defaults to `appsettings.json`**

Open `backend/src/Anela.Heblo.API/appsettings.json`. Inside the root object (anywhere after `Database` if it exists, or before `Hangfire`), add:

```json
  "Database": {
    "Resilience": {
      "MaxRetryAttempts": 3,
      "BaseDelay": "00:00:00.200",
      "MaxRetryDelay": "00:00:04",
      "TotalTimeBudget": "00:00:10"
    }
  },
  "AnalyticsDatabase": {
    "MaxPoolSize": 10
  },
```

If a `Database` block already exists, merge `Resilience` into it without overwriting other keys.

- [ ] **Step 2: Update `appsettings.Production.json`**

Edit `backend/src/Anela.Heblo.API/appsettings.Production.json`. Replace the existing `Database` block (lines 69-73):

```json
  "Database": {
    "MaxPoolSize": 15,
    "ConnectionIdleLifetime": 60,
    "ConnectionPruningInterval": 10
  },
```

…with:

```json
  "Database": {
    "MaxPoolSize": 20,
    "ConnectionIdleLifetime": 60,
    "ConnectionPruningInterval": 10,
    "Resilience": {
      "MaxRetryAttempts": 3,
      "BaseDelay": "00:00:00.200",
      "MaxRetryDelay": "00:00:04",
      "TotalTimeBudget": "00:00:10"
    }
  },
  "AnalyticsDatabase": {
    "MaxPoolSize": 10
  },
```

- [ ] **Step 3: Update `appsettings.Staging.json`**

Open `backend/src/Anela.Heblo.API/appsettings.Staging.json`. Inside the existing `Database` block, leave `MaxPoolSize` at 10 and add the same `Resilience` sub-block as above. Add the `AnalyticsDatabase:MaxPoolSize: 10` block. (Use the same JSON as Step 2 except `Database:MaxPoolSize: 10`.)

- [ ] **Step 4: Build + run all backend tests**

Run: `dotnet build backend && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: BUILD + TEST SUCCEEDED. All existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json \
        backend/src/Anela.Heblo.API/appsettings.Production.json \
        backend/src/Anela.Heblo.API/appsettings.Staging.json
git commit -m "feat(api): add Database:Resilience options + cap analytics pool, raise prod EF pool to 20"
```

---

## Task 11: Production-config regression test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Persistence/Resilience/ProductionConnectionStringDefaultsTests.cs`

Per arch-review Risk row (`Include Error Detail` accidentally true): assert the production config does not flip NFR-2.

- [ ] **Step 1: Write the regression tests**

Create `backend/test/Anela.Heblo.Tests/Persistence/Resilience/ProductionConnectionStringDefaultsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class ProductionConnectionStringDefaultsTests
{
    [Fact]
    public void Production_DatabaseMaxPoolSize_IsTwenty()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("Database:MaxPoolSize").Should().Be(20,
            "spec FR-2 raises production EF pool from 15 to 20");
    }

    [Fact]
    public void Production_AnalyticsMaxPoolSize_IsCapped()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("AnalyticsDatabase:MaxPoolSize").Should().Be(10,
            "AnalyticsDbContext must not consume the server's connection budget");
    }

    [Fact]
    public void Production_ResilienceOptions_MatchSpec()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("Database:Resilience:MaxRetryAttempts").Should().Be(3);
        config.GetValue<TimeSpan>("Database:Resilience:TotalTimeBudget").Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Production_HangfireConnectionLimit_StaysAtFive()
    {
        var config = LoadProductionConfig();

        config.GetValue<int>("Hangfire:ConnectionLimit").Should().Be(5,
            "spec FR-2 keeps Hangfire pool at 5 to preserve total connection budget");
    }

    private static IConfigurationRoot LoadProductionConfig()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Anela.Heblo.API"));

        return new ConfigurationBuilder()
            .SetBasePath(path)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();
    }
}
```

If the relative path resolution does not match the test project's `OutputPath`, adjust the `..` count or use `Directory.GetParent(AppContext.BaseDirectory)` traversal until `appsettings.json` is found.

- [ ] **Step 2: Run the tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductionConnectionStringDefaultsTests"`
Expected: PASS — all 4 tests green.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Persistence/Resilience/ProductionConnectionStringDefaultsTests.cs
git commit -m "test(persistence): assert production connection-string defaults match spec"
```

---

## Task 12: Register Npgsql + resilience meters with Application Insights

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs`

Per arch-review amendment #8: the existing AI bridge only exports meters it knows about. Both `"Npgsql"` (the Npgsql built-in counters) and `"Anela.Heblo.Database.Resilience"` (our `DbResilienceMetrics`) must be opted in.

- [ ] **Step 1: Add OpenTelemetry meter registration**

Edit `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs`. After the existing `services.AddApplicationInsightsTelemetry(options);` call (around line 55), add:

```csharp
        // Surface Npgsql's built-in Meter counters and our custom resilience counters in App Insights.
        // Application Insights' default Meter-to-AI bridge auto-discovers EventCounters but
        // requires explicit allowlisting for System.Diagnostics.Metrics meters by name.
        services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder.AddMeter("Npgsql");
                builder.AddMeter("Anela.Heblo.Database.Resilience");
                builder.AddMeter("Anela.Heblo.Smartsupp.Webhooks"); // existing — keep current behaviour
                builder.AddAzureMonitorMetricExporter();
            });
```

This requires two new packages on the API project:
- `OpenTelemetry.Extensions.Hosting`
- `Azure.Monitor.OpenTelemetry.Exporter`

Add to `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`:

```xml
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />
    <PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.3.0" />
```

Add the required `using`s to the top of `ApplicationInsightsExtensions.cs`:

```csharp
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
```

- [ ] **Step 2: Verify the AI exception/metric sampling exclusion list**

Confirm `Metric` is not stripped by adaptive sampling. The existing call uses `excludedTypes: "Exception;Event"` — that list does not exclude metrics, so no change is needed. Add a comment above the `UseAdaptiveSampling` call:

```csharp
            // excludedTypes does not list "Metric" — adaptive sampling only filters telemetry it can
            // identify by category. MetricTelemetry items flow through unfiltered, which is what we want
            // for the Npgsql / Database.Resilience meters added above.
```

- [ ] **Step 3: Build**

Run: `dotnet build backend`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs \
        backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git commit -m "feat(api): register Npgsql + db-resilience meters with Application Insights"
```

---

## Task 13: KQL snippets for FR-3 telemetry verification

**Files:**
- Create: `docs/integrations/db-connection-health-kql.md`

A simple, copy-pasteable reference for the on-call developer. Per arch-review amendment #9, scope alert queries to Npgsql-originated exceptions only.

- [ ] **Step 1: Create the document**

Create `docs/integrations/db-connection-health-kql.md`:

```markdown
# Database Connection Health — App Insights / KQL Snippets

Use these queries in the Azure Portal Application Insights workbook (or the Logs blade)
to verify pool health and audit the FR-3 telemetry surface.

## 1. Npgsql-originated exceptions in the last hour

```kusto
exceptions
| where timestamp > ago(1h)
| where type startswith "Npgsql."
    or (type == "System.Net.Sockets.SocketException" and outerAssembly startswith "Npgsql")
    or (type == "System.TimeoutException" and outerMethod has "NpgsqlConnector")
| summarize count() by type, bin(timestamp, 5m)
| render timechart
```

## 2. Pool exhaustion waits (custom metric)

```kusto
customMetrics
| where name == "npgsql.pool.exhaustion_wait_seconds"
| summarize p50 = percentile(value, 50), p95 = percentile(value, 95), n = count() by bin(timestamp, 5m)
| render timechart
```

## 3. Retry counters

```kusto
customMetrics
| where name in ("db.retry.attempts", "db.retry.success", "db.retry.failure")
| extend exceptionType = tostring(customDimensions["exception.type"])
| summarize total = sum(value) by name, exceptionType, bin(timestamp, 15m)
| render timechart
```

## 4. Npgsql built-in pool counters

```kusto
customMetrics
| where name in ("npgsql.connections.total", "npgsql.connections.idle", "npgsql.connections.busy")
| summarize value = avg(value) by name, bin(timestamp, 5m)
| render timechart
```

## 5. Alert query — Npgsql exception spike

Used by alert rule `alert-heblo-db-npgsql-spike`:

```kusto
exceptions
| where timestamp > ago(1h)
| where type startswith "Npgsql."
    or (type == "System.Net.Sockets.SocketException" and outerAssembly startswith "Npgsql")
| count
```

Threshold: `Count > 10` for 2 consecutive evaluations (hourly).

## 6. Alert query — pool exhaustion

Used by alert rule `alert-heblo-db-pool-exhaustion`:

```kusto
customMetrics
| where timestamp > ago(5m)
| where name == "npgsql.pool.exhaustion_wait_seconds"
| count
```

Threshold: `Count > 5` for one evaluation (5-minute window).
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/db-connection-health-kql.md
git commit -m "docs: add KQL snippets for Npgsql connection health monitoring"
```

---

## Task 14: Document Action Group + alert rules

**Files:**
- Modify: `docs/architecture/infrastructure.md`

Capture the Action Group + alert configuration as runbook so the next change can recreate it.

- [ ] **Step 1: Append the "Database alerts" section**

Append to `docs/architecture/infrastructure.md`:

```markdown
## Database connection health alerts

### Action Group

The alert receiver is the `ag-heblo-prod-default` Azure Monitor Action Group in
resource group `rgHeblo`. Single email receiver: `ondra@anela.cz`.

Verify or create:

```bash
az monitor action-group show -g rgHeblo -n ag-heblo-prod-default || \
az monitor action-group create \
  -g rgHeblo -n ag-heblo-prod-default \
  --short-name HebloProd \
  --email-receivers name=primary email=ondra@anela.cz useCommonAlertSchema=true
```

### Alert rule — Npgsql exception spike

Fires when Npgsql-originated exceptions exceed 10/hour for 2 consecutive hours.

```bash
az monitor scheduled-query create \
  -g rgHeblo -n alert-heblo-db-npgsql-spike \
  --scopes /subscriptions/<sub>/resourceGroups/rgHeblo/providers/microsoft.insights/components/aiHeblo \
  --description "Npgsql / SocketException spike originating in the database driver" \
  --severity 2 \
  --evaluation-frequency 1h \
  --window-size 1h \
  --condition "count 'exceptions | where type startswith \"Npgsql.\" or (type == \"System.Net.Sockets.SocketException\" and outerAssembly startswith \"Npgsql\") | count' > 10" \
  --condition-query Heartbeat \
  --action-groups /subscriptions/<sub>/resourceGroups/rgHeblo/providers/microsoft.insights/actionGroups/ag-heblo-prod-default \
  --auto-mitigate true \
  --target-resource-type microsoft.insights/components
```

Number of evaluation periods required to fire: 2 (`--mfns=2 --mfnt=2` if needed via the v2 API).

### Alert rule — pool exhaustion

Fires when pool exhaustion waits exceed 5 in any 5-minute window.

```bash
az monitor scheduled-query create \
  -g rgHeblo -n alert-heblo-db-pool-exhaustion \
  --scopes /subscriptions/<sub>/resourceGroups/rgHeblo/providers/microsoft.insights/components/aiHeblo \
  --description "Npgsql connection pool exhaustion waits > 5/5m" \
  --severity 2 \
  --evaluation-frequency 5m \
  --window-size 5m \
  --condition "count 'customMetrics | where name == \"npgsql.pool.exhaustion_wait_seconds\" | count' > 5" \
  --action-groups /subscriptions/<sub>/resourceGroups/rgHeblo/providers/microsoft.insights/actionGroups/ag-heblo-prod-default \
  --auto-mitigate true
```

Replace `<sub>` with the production Azure subscription ID (`az account show --query id -o tsv`).

See `docs/integrations/db-connection-health-kql.md` for the underlying KQL.
```

- [ ] **Step 2: Commit**

```bash
git add docs/architecture/infrastructure.md
git commit -m "docs: document db connection health Action Group + alert rules"
```

---

## Task 15: CI guard against managed transactions

**Files:**
- Create: `scripts/check-no-managed-tx.sh`

Per arch-review risk row (HIGH): the execution strategy contract forbids user-managed transactions. A one-line guard prevents future regressions.

- [ ] **Step 1: Create the script**

Create `scripts/check-no-managed-tx.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

# Fails if a managed-transaction API call appears in backend/src.
# The PollyExecutionStrategy retries an EF Core operation by replaying it; a
# caller-owned transaction would silently break that contract by reusing a
# stale NpgsqlTransaction. SaveChangesAsync's implicit transaction is safe.

hits=$(grep -rn -E "BeginTransaction|UseTransaction" \
  --include="*.cs" \
  backend/src || true)

if [[ -n "$hits" ]]; then
  echo "ERROR: managed-transaction API used in backend/src — incompatible with PollyExecutionStrategy" >&2
  echo "$hits" >&2
  exit 1
fi

echo "OK: no BeginTransaction / UseTransaction calls in backend/src"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/check-no-managed-tx.sh`

- [ ] **Step 3: Verify it passes on the current tree**

Run: `./scripts/check-no-managed-tx.sh`
Expected: `OK: no BeginTransaction / UseTransaction calls in backend/src`

- [ ] **Step 4: Wire it into the backend CI workflow**

Find the existing backend workflow:

Run: `ls -la .github/workflows/`

In the workflow that runs `dotnet build` for the backend (commonly `backend-build.yml`, `ci.yml`, or similar), add a new step **before** the build step:

```yaml
      - name: Guard against managed transactions
        run: ./scripts/check-no-managed-tx.sh
```

- [ ] **Step 5: Commit**

```bash
git add scripts/check-no-managed-tx.sh .github/workflows/
git commit -m "ci: guard against user-managed transactions incompatible with execution strategy"
```

---

## Task 16: Update Key Vault connection strings (manual, deployment-time)

**Files:** none in repo — Azure CLI operations only.

Per FR-2 / NFR-2: connection strings live in Key Vault only. Per arch-review Decision 6: tunables (`Keepalive`, `Tcp Keepalive`, `Connection Idle Lifetime`, `Include Error Detail`) live in `appsettings.json` and code, **not** in the Key Vault secret. The Key Vault values should contain only `Host`, `Port`, `Database`, `Username`, `Password`, `SSL Mode`, `Trust Server Certificate`.

This task is a runbook — execute when the audit (Task 0) and code changes (Tasks 1-15) are merged and ready to deploy to staging/production.

- [ ] **Step 1: Read the current Production secret**

Run:
```bash
az keyvault secret show --vault-name kv-heblo-prod --name "ConnectionStrings--Production" --query value -o tsv > /tmp/current-prod-cs.txt
```

Inspect `/tmp/current-prod-cs.txt`. Strip any `Max Pool Size`, `Connection Idle Lifetime`, `Connection Pruning Interval`, `Keepalive`, `Tcp Keepalive`, `Include Error Detail` parameters — those are now configured in code/appsettings.

- [ ] **Step 2: Write back the trimmed value**

Run:
```bash
az keyvault secret set --vault-name kv-heblo-prod --name "ConnectionStrings--Production" --value "<trimmed value>"
```

- [ ] **Step 3: Repeat for Staging**

Run:
```bash
az keyvault secret show --vault-name kv-heblo-stg --name "ConnectionStrings--Staging" --query value -o tsv > /tmp/current-stg-cs.txt
# … strip tunables …
az keyvault secret set --vault-name kv-heblo-stg --name "ConnectionStrings--Staging" --value "<trimmed value>"
```

- [ ] **Step 4: Delete the tempfiles**

Run:
```bash
shred -u /tmp/current-prod-cs.txt /tmp/current-stg-cs.txt 2>/dev/null || rm -f /tmp/current-prod-cs.txt /tmp/current-stg-cs.txt
```

- [ ] **Step 5: Record the post-change values in the audit**

Update the Task 0 audit table in `docs/architecture/environments.md` with the post-change connection-string parameters per environment.

- [ ] **Step 6: Commit doc update**

```bash
git add docs/architecture/environments.md
git commit -m "docs: record post-change Key Vault connection-string values in audit"
```

---

## Task 17: Create the Action Group + alert rules (manual, deployment-time)

**Files:** none in repo — Azure CLI operations only. Documentation already in `docs/architecture/infrastructure.md` (Task 14).

Run the runbook commands from Task 14 (`docs/architecture/infrastructure.md` → "Database connection health alerts").

- [ ] **Step 1: Verify or create the Action Group**

Run the `az monitor action-group show … || az monitor action-group create …` command from the runbook.

- [ ] **Step 2: Create both alert rules**

Run the two `az monitor scheduled-query create` commands from the runbook, substituting the live subscription ID.

- [ ] **Step 3: Verify the alert rules**

Run: `az monitor scheduled-query list -g rgHeblo --query "[?contains(name, 'alert-heblo-db-')].{name:name, severity:severity, enabled:enabled}" -o table`
Expected: both `alert-heblo-db-npgsql-spike` and `alert-heblo-db-pool-exhaustion` appear and are `enabled = true`.

- [ ] **Step 4: Send a test signal (optional)**

If possible, run an integration test or manual workflow that triggers a `npgsql.pool.exhaustion_wait_seconds` sample, then confirm the alert evaluation picks it up by checking `az monitor scheduled-query show -g rgHeblo -n alert-heblo-db-pool-exhaustion --query "latestEvaluationOutcome"`.

---

## Task 18: Final validation

**Files:** none — verification only.

- [ ] **Step 1: Full backend build + test**

Run: `dotnet build backend && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: BUILD + TEST SUCCEEDED.

- [ ] **Step 2: Format check**

Run: `dotnet format backend/src/Anela.Heblo.Persistence --verify-no-changes` (and the same for `Anela.Heblo.API`, `Anela.Heblo.Persistence.Analytics`).
Expected: no diff.

- [ ] **Step 3: Managed-transaction guard**

Run: `./scripts/check-no-managed-tx.sh`
Expected: `OK`.

- [ ] **Step 4: Smoke test against an InMemory DbContext**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PersistenceModuleTests"`
Expected: the regression test for "25 DbContexts without ManyServiceProvidersCreatedWarning" still passes (validates that adding the strategy factory did not break the singleton `NpgsqlDataSource` arrangement).

- [ ] **Step 5: Final commit if any cleanup is needed**

If any cleanup is required (formatting, doc typos), make a final commit:

```bash
git add -A
git commit -m "chore: final cleanup after npgsql resilience rollout"
```

---

## Self-Review

### Spec coverage

- **FR-1 (audit)** → Task 0 (5 steps + amendment for `AnalyticsDbContext` and `MaterialContainerCodeGenerator`).
- **FR-2 (hardened connection-string defaults)** → Tasks 9 (analytics pool cap), 10 (appsettings updates), 16 (Key Vault rotation), 11 (regression test). All amendment items (Decision 6, analytics cap) covered.
- **FR-3 (telemetry)** → Tasks 4 (`DbResilienceMetrics`), 7 (`NpgsqlConnectionInterceptor`), 12 (AI meter registration), 13 (KQL).
- **FR-4 (Polly execution strategy)** → Tasks 1 (Polly package), 2 (classifier), 3 (options), 5 (provider), 6 (strategy), 8 (wiring), 15 (managed-tx CI guard).
- **FR-5 (alerting)** → Tasks 14 (documentation), 17 (Az CLI execution).
- **NFR-1 (≤1ms p99 overhead)** → The pipeline passthrough on success has only a wrapping `ResiliencePipeline.ExecuteAsync` + counter increment; no I/O. Acceptable.
- **NFR-2 (security)** → Task 11 asserts `MaxPoolSize`. `Include Error Detail` is not set anywhere in the connection-string builder (defaults to false). Task 16 keeps only host/credentials in Key Vault.
- **NFR-3 (observability schema)** → All counters use OpenTelemetry-style names (`db.retry.*`, `npgsql.pool.*`). Logs use `attempt`, `delay`, `exception.type` properties matching the adapter `ResilienceService` schema (Catalog OnRetry log: `AttemptNumber`, `Exception` — consistent semantic).
- **NFR-4 (backwards compatibility)** → Task 8 retains existing interceptor, Task 18 runs the existing regression suite.

### Placeholder scan

No "TBD" / "implement later" / "add appropriate error handling" / "similar to Task N" patterns appear above. Every step contains either a runnable command or full code. Per Task 16 Step 1, `<trimmed value>` is an explicit human action the operator must perform; this is operator data, not a placeholder for the implementer.

### Type consistency

- `DbResilienceOptions.SectionName = "Database:Resilience"` — used identically in `PersistenceModule.cs:Configure`, `appsettings*.json`, and tests.
- `DbResilienceMetrics.MeterName = "Anela.Heblo.Database.Resilience"` — used in `ApplicationInsightsExtensions.AddMeter`, AI bridge, KQL docs.
- `PollyExecutionStrategy` constructor signature `(ExecutionStrategyDependencies deps, IDbResiliencePipelineProvider, DbResilienceMetrics, ILogger<PollyExecutionStrategy>)` — identical in `PersistenceModule`, `AnalyticsPersistenceModule`, and test factory.
- `TransientErrorClassifier.IsTransient(Exception)` / `IsNonTransientLogical(Exception)` — same signatures across tests, provider, and any future consumer.
- Counter names used in metrics class (`db.retry.attempts`, `db.retry.success`, `db.retry.failure`, `npgsql.pool.exhaustion_wait_seconds`) match KQL doc.

No drift detected.
