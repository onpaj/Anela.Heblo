# Design: Chronic Npgsql `TaskCanceledException@NpgsqlWriteBuffer.Flush` connectivity failures

No user interface is involved — this is a backend resilience/observability fix (Persistence + Application layers, plus a docs-only telemetry query). The UX/UI section is omitted.

## Grounding for this design

Before laying out components, three plan assumptions were checked directly against the running code and the actual Polly 8.4.1 package this repo depends on (not against Polly's general docs, which don't match this composition). These findings materially shape the design below and resolve the plan's two "Open Questions".

**1. `TransientErrorClassifier` currently has no case for `Polly.Timeout.TimeoutRejectedException`, and this is fixable with a single new switch arm.**
`TimeoutRejectedException` (`Infrastructure/Resilience/TransientErrorClassifier.cs:45-56`) inherits from `Polly.ExecutionRejectedException` → `System.Exception` — **not** from `TimeoutException` — so the existing `TimeoutException => true` arm does not catch it, confirmed via reflection against the installed `Polly.Core 8.4.1` assembly.

**2. The production pipeline (`.AddRetry(retry).AddTimeout(options.TotalTimeBudget)` in `DbResiliencePipelineProvider.BuildPipeline`) makes `AddTimeout` a PER-ATTEMPT timeout, not a shared/total budget across the retry loop — despite the field's name.**
Verified empirically by building the exact same two-strategy composition against `Polly.Core 8.4.1` and driving it with both a permanently-hanging operation and an ambient-cancelled one:
- A hung operation (never respects its own token) retried correctly up to `MaxRetryAttempts` times, each bounded by the `TotalTimeBudget` duration individually (with the classifier fix applied) — confirming `AddTimeout`, added *after* `AddRetry`, is nested *inside* each attempt, not wrapping the whole loop.
- A `SocketException`-throwing operation with `MaxRetryAttempts=50` and a 150 ms `TotalTimeBudget` ran all 51 attempts to exhaustion (6.4 s elapsed) — proof the "budget" does not bound the overall retry loop at all when each attempt itself returns quickly.
- Reversing the order (`.AddTimeout().AddRetry()`) does turn `AddTimeout` into a true shared budget (3 attempts, 281 ms) — but this is *not* what the current code does, and reordering was evaluated and rejected for this change (see Component design §2).

**3. Ambient/caller-token cancellation is — and remains, unchanged — correctly non-transient.**
When the *caller's* token fires (not Polly's own timer), the exception that reaches `ShouldHandle` is a raw, unwrapped `TaskCanceledException` with no `TimeoutRejectedException` wrapper, confirmed in every probe variant including the exact production composition. It never matches any arm of `IsTransientCore` today and falls through to `_ => false`. **No code change is needed to preserve this** — it's already correct, and the classifier fix in this design does not touch it.

**4. `/health/ready`'s Npgsql check does not go through `TransientErrorClassifier`/`PollyExecutionStrategy` at all.**
`ServiceCollectionExtensions.AddHealthCheckServices` wires `AddNpgSql(sp => sp.GetRequiredService<NpgsqlDataSource>(), ...)` — the `AspNetCore.HealthChecks.NpgSql` package issues its own ADO.NET query directly against the shared `NpgsqlDataSource`, entirely outside EF Core's `ExecutionStrategy`. **FR-1's classifier fix therefore has no effect on `/health/ready`'s own cancellations.** This is a deliberate scope boundary, not an oversight: retrying a health probe adds latency to a monitoring signal without changing correctness (the next probe interval tries again), so no retry is added there. What *does* change for `/health/ready` is indirect — see Component design §5 (pool-wait visibility) and the NFR note below on residual exception volume.

**5. The raw `TaskCanceledException@NpgsqlWriteBuffer.Flush` will very likely keep appearing in Application Insights' auto-collected exception/dependency telemetry even after this fix ships, including for calls that Polly successfully retries.**
The exception is thrown inside Npgsql's own write-buffer flush, one layer *below* where Polly wraps it into `TimeoutRejectedException`. App Insights' ADO.NET/Npgsql auto-instrumentation taps DiagnosticSource events at that lower layer, independent of whether the call above it ultimately succeeds after a retry. This resolves the plan's second Open Question: **raw exception count in App Insights is not the right success signal for this fix.** The design's success signals (below) are instead: `db.retry.success`/`db.retry.failure` counts (already emitted, now correctly populated for this exception family), the new `dbRetryExhausted` log tag (Component design §4), and *request-level* failure rate / retry-exhaustion count — not the `exceptions` table's raw row count for this problemId.

## Component design

### 1. `TransientErrorClassifier` (extend)

`Infrastructure/Resilience/TransientErrorClassifier.cs` — add one arm to `IsTransientCore`'s switch, alongside the existing `PostgresException`/`SocketException`/`TimeoutException`/`IOException` arms (before the generic `InnerException` unwrap fallback, so it's matched directly and still benefits from recursive unwrap if `TimeoutRejectedException` itself is ever wrapped, e.g. inside a `DbUpdateException` from a failed `SaveChangesAsync`):

```csharp
Polly.Timeout.TimeoutRejectedException => true,
```

Nothing else in the file changes. `IsNonTransientLogical` and the SQL-state tables are unaffected. This is the entire code change needed to make Polly's own per-attempt timeout retryable while leaving ambient cancellation alone (§3 above).

### 2. `DbResilienceOptions` / `DbResiliencePipelineProvider` (retune, do not reorder)

Because `TotalTimeBudget` is empirically a **per-attempt** ceiling in the current composition, classifying `TimeoutRejectedException` as transient means a fully-wedged DB path can now legitimately consume up to `(MaxRetryAttempts + 1) × TotalTimeBudget` plus backoff delay before giving up — with production's current values (`MaxRetryAttempts=3`, `TotalTimeBudget=10s`, `BaseDelay=200ms`→`MaxRetryDelay=4s` exponential+jitter) that's roughly **4 × 10 s + ~1.4 s ≈ 41 s worst case for a single logical DB call**. Today this ceiling is rarely reached because none of the currently-transient exception types (`PostgresException`, `SocketException`, `TimeoutException`, `IOException`) reliably consume the *entire* per-attempt window — they tend to fail fast. `TimeoutRejectedException` is different: by construction, it only fires once the full per-attempt window has elapsed, so once it's classified transient, every retried hang *will* burn its full budget on every attempt. This is exactly the latency-doubling (here, quadrupling) risk the plan's NFR called out, and it's worse than "double" — it needs to be fixed as part of FR-1, not deferred.

Decision: **reduce `Database:Resilience:TotalTimeBudget` from `00:00:10` to `00:00:03`** in `appsettings.json` (dev default), `appsettings.Staging.json`, and `appsettings.Production.json`. Worst case becomes `4 × 3s + ~1.4s ≈ 13.4s` — still a visible tail, but short enough to usually resolve before a caller's own patience (browser fetch, typical health-probe timeout, ASP.NET Core's default request timeout) runs out, which is the actual goal: let Polly's retry *win the race* against the caller giving up, rather than the caller cancelling first and the retry becoming pointless work that only adds pool pressure. 3 s per attempt is still generous for a normal query — this bounds pathological hangs, not everyday latency.

Add a doc comment on `DbResilienceOptions.TotalTimeBudget` clarifying it is consumed as a per-attempt ceiling in the current pipeline shape (not a whole-call budget), so the next person reading the name doesn't repeat this investigation.

**Rejected alternative — reordering into a 3-layer composition** (`AddTimeout(total, outer) → AddRetry(middle) → AddTimeout(perAttempt, inner)`) would make `TotalTimeBudget` genuinely total, which is more honest to its name. This was prototyped during design and produces the right shape for simple retry-only cases, but nested/reordered `AddTimeout` strategies showed timing behavior in Polly 8.4.1 that didn't cleanly match either the inner or outer configured duration in one prototype run — behavior that needs its own dedicated, carefully-instrumented spike to trust in production. Given the "surgical changes" project rule and that the 2-layer shape with a smaller `TotalTimeBudget` already gets worst-case latency into a reasonable range with a one-line classifier change plus a config tune, reordering is explicitly deferred as future work, not part of this fix.

### 3. `PollyExecutionStrategy` (extend — expose retry-attempt count on the rethrown exception)

`Infrastructure/Resilience/PollyExecutionStrategy.cs` — in both `Execute` and `ExecuteAsync`'s `catch (Exception ex)` block, immediately before the existing `_metrics.RecordRetryFailure(...)` / `_logger.LogError(...)` / `throw`, tag the exception:

```csharp
ex.Data["Anela.DbRetryAttempts"] = attempt;
```

This is a generic signal ("this failure survived N attempts through the resilience pipeline before propagating") usable by *any* caller of `ApplicationDbContext`, not just `HebloFeatureProvider` — deliberately not coupled to FeatureFlags. `attempt` is already tracked locally in both methods; no new state.

### 4. `HebloFeatureProvider.ResolveBooleanValueAsync` (extend — surface retry-exhaustion distinctly)

`Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:58-67` — read the tag set by §3 in the existing `catch (Exception ex)` block and add it to the existing `LogWarning` call:

```csharp
catch (Exception ex)
{
    var attempts = ex.Data["Anela.DbRetryAttempts"] as int? ?? 1;
    var dbRetryExhausted = attempts > 1;
    _logger.LogWarning(
        ex,
        "Feature flag resolution failed for key {FlagKey}; using default {DefaultValue}; dbRetryExhausted={DbRetryExhausted} attempts={Attempts}",
        flagKey, defaultValue, dbRetryExhausted, attempts);
    return new ResolutionDetails<bool>(
        flagKey, defaultValue,
        errorType: ErrorType.General, errorMessage: ex.Message, reason: Reason.Error);
}
```

Returned `ResolutionDetails` and caller-visible behavior are unchanged — this is purely an observability addition. `dbRetryExhausted=true` distinguishes "the pipeline tried, retried, and still failed" from `dbRetryExhausted=false` (attempts==1), which covers both "immediate non-transient failure" (e.g. a schema error) and "no DB call happened at all" (e.g. `OperationCanceledException` thrown by the `ct.ThrowIfCancellationRequested()` guard before ever reaching the repository).

### 5. `NpgsqlConnectionInterceptor` (extend — record pool-wait on the failure path, not just success)

`Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs` already measures connection-open latency and records `DbResilienceMetrics.RecordPoolExhaustionWait` / logs `DbPoolExhaustionWait` — but **only from `ConnectionOpened`/`ConnectionOpenedAsync` (the success path)**. `ConnectionFailed`/`ConnectionFailedAsync` calls `LogConnectionFailure` without ever reading `OpenStopwatch`, so a connection attempt that's *cancelled while queued waiting for a free pool slot* (exactly the "pool exhaustion" scenario FR-3 needs to detect) leaves no wait-time trace today — the one case where this signal matters most is the one case that isn't recorded.

Fix: in `LogConnectionFailure`, also stop `OpenStopwatch` (mirroring `RecordOpenLatency`'s own stop-and-clear) and include the elapsed time:

```csharp
private void LogConnectionFailure(DbConnection connection, Exception exception)
{
    var host = SafeGetProperty(connection, "Host");
    var database = SafeGetProperty(connection, "Database");
    var waitSeconds = StopAndGetElapsedSeconds();

    if (waitSeconds is > PoolExhaustionThresholdSeconds)
    {
        _metrics.RecordPoolExhaustionWait(waitSeconds.Value);
    }

    _logger.LogWarning(
        exception,
        "DbConnectionFailed exception.type={ExceptionType} npgsql.host={Host} npgsql.database={Database} wait_seconds={WaitSeconds}",
        exception.GetType().FullName, host, database, waitSeconds);
}

private static double? StopAndGetElapsedSeconds()
{
    var sw = OpenStopwatch.Value;
    if (sw is null) return null;
    sw.Stop();
    OpenStopwatch.Value = null;
    return sw.Elapsed.TotalSeconds;
}
```

(`RecordOpenLatency` can be refactored onto the same `StopAndGetElapsedSeconds` helper to avoid duplicating the stop/clear logic — a small internal simplification, not a behavior change.)

No new metric or counter is introduced — `npgsql.pool.exhaustion_wait_seconds` already exists and is exactly the right shape for FR-3; it was simply never fed from the one path (a cancelled/failed open) where the signal is most needed.

### 6. `docs/routines/telemetry-anomaly/` (extend — documented correlation query, no code)

Add a dated note (following the existing `2026-06-13-stockupoperations-summary-403.md` pattern) with the KQL needed to answer FR-3's confirm/refute question, joining on `operation_Id` (the ASP.NET Core / App Insights per-request correlation ID, automatically shared by every trace/exception/dependency row logged within one request, including `NpgsqlConnectionInterceptor`'s own log lines):

```kql
let failures = exceptions
| where problemId has "NpgsqlWriteBuffer" and type == "System.Threading.Tasks.TaskCanceledException"
| project operation_Id, timestamp, cloud_RoleName;
let poolWaits = traces
| where message has "DbPoolExhaustionWait" or message has "DbConnectionFailed"
| project operation_Id, wait_seconds = todouble(customDimensions["wait_seconds"]);
failures
| join kind=leftouter poolWaits on operation_Id
| summarize
    total = count(),
    withPoolWait = countif(isnotnull(wait_seconds)),
    poolWaitOverThreshold = countif(wait_seconds > 1.0)
| extend pctCoincidingWithPoolContention = round(100.0 * poolWaitOverThreshold / total, 1)
```

This is the confirm/refute gate the plan's FR-3/FR-4 dependency needs: a result showing most `TaskCanceledException@NpgsqlWriteBuffer.Flush` rows share an `operation_Id` with a `wait_seconds > 1.0` trace means pool contention is the driver (supports an FR-4 pool-size follow-up with evidence attached); a result showing little/no overlap means the cause is elsewhere (ambient client/probe cancellation unrelated to pool state, e.g. a health probe with a short timeout) and FR-4 should not be pursued on this evidence.

## Data model / schemas

No persisted entities change; no REST/API contract changes (internal-only fix). Concrete surface changes:

**Configuration** (`appsettings.json`, `appsettings.Staging.json`, `appsettings.Production.json`):
| Key | Before | After |
|---|---|---|
| `Database:Resilience:TotalTimeBudget` | `00:00:10` | `00:00:03` |

All other `Database:*` and `Database:Resilience:*` keys unchanged.

**Exception `.Data` contract** (new, informal — not a public API, internal signal between `PollyExecutionStrategy` and its callers):
| Key | Type | Meaning |
|---|---|---|
| `"Anela.DbRetryAttempts"` | `int` | Total attempts made by the resilience pipeline before this exception was rethrown. `1` = failed on the first try (no retry occurred, e.g. immediately non-transient, or never reached the DB). `>1` = retried at least once and still failed (exhausted). |

**Structured log field additions**:
| Log event | New field | Type |
|---|---|---|
| `HebloFeatureProvider` flag-resolution warning | `dbRetryExhausted` | `bool` |
| `HebloFeatureProvider` flag-resolution warning | `attempts` | `int` |
| `NpgsqlConnectionInterceptor` `DbConnectionFailed` warning | `wait_seconds` | `double?` (null when no open was in flight, e.g. a non-open-related failure) |

**Metrics** (no new instrument; existing `DbResilienceMetrics` instruments gain new data sources):
| Instrument | Change |
|---|---|
| `db.retry.failure` (`Counter<long>`, tag `exception.type`) | Now includes `Polly.Timeout.TimeoutRejectedException` as a possible tag value once retries on it are exhausted |
| `npgsql.pool.exhaustion_wait_seconds` (`Histogram<double>`) | Now also populated from cancelled/failed connection-open attempts (§5), not only successful ones |

**Telemetry query artifact**: `docs/routines/telemetry-anomaly/<date>-npgsql-cancellation-pool-correlation.md` — the KQL in Component design §6, plus a template for recording the confirm/refute result of the FR-3/FR-4 gate.

## Interfaces

No public/HTTP interface changes. Internal call-graph changes only:
- `TransientErrorClassifier.IsTransient(Exception)` — same signature, extended classification.
- `PollyExecutionStrategy.Execute`/`ExecuteAsync` — same signatures; exceptions rethrown now carry an additional `.Data` entry.
- `HebloFeatureProvider.ResolveBooleanValueAsync` — same signature and return contract; catch block reads the new `.Data` entry.
- `NpgsqlConnectionInterceptor` — same `DbConnectionInterceptor` overrides; `LogConnectionFailure`'s behavior extended, one new private helper.
- `/health/ready`, `/health/live`, `GET FeatureFlags/Get`, `GET StockUpOperations/GetSummary`, `GET /api/Configuration` — no caller-visible response changes.

## Non-functional requirements — updated based on the grounding above

- **Reliability target** (unchanged from plan): `TaskCanceledException@NpgsqlWriteBuffer.Flush` daily count trending toward the 3/day healthy baseline — but per §5, raw App Insights exception *count* for this problemId is expected to remain nonzero even on success (auto-collected below Polly's wrap point). The primary post-deploy signal is `db.retry.success` count for this exception family (new) and `dbRetryExhausted=true` frequency in `FeatureFlags/Get` logs (new) trending down over the two P7D windows — not the raw exceptions-table row count.
- **Latency**: worst case for a single fully-wedged DB call rises from "propagates immediately" (today) to ~13.4 s (§2's calculation, after the `TotalTimeBudget` reduction) — an explicit, bounded tradeoff, not silent. Must be verified with a test asserting the pipeline's total elapsed time under a permanently-hanging operation stays within this bound (extends the existing `Pipeline_AbortsByTotalTimeBudget` test pattern in `DbResiliencePipelineProviderTests.cs`).
- **No behavior change for genuine client/probe cancellation** — confirmed by design (§"Grounding" point 3): ambient-token cancellation was already, and remains, correctly non-transient with zero code change required for that half of the behavior. The risk NFR called out ("retrying after the caller already gave up wastes a pool slot") does not materialize for this reason.
- **Secrets/config**: the one config change (`TotalTimeBudget` 10s→3s) is a plain `appsettings.*.json` value, not a secret — no Key Vault interaction needed.

## Scope boundaries (unchanged from plan, restated for this design)

Out of scope, per the plan and reaffirmed here: Azure Postgres Flexible Server tier/`max_connections` changes; the unrelated `DateTimeConverterResolver.Get` bugs (#3592, #3757); `AddDbContextPool`/Hangfire-onto-shared-datasource redesign; re-implementing already-landed #591/#592/#893/#3193/#680 work (verified intact during grounding, §"Investigation findings" in the plan); `MaxPoolSize`/`Hangfire:ConnectionLimit` changes (FR-4, gated on the §6 query's evidence, not part of this change). Newly identified and explicitly deferred by this design: reordering the Polly pipeline into a 3-layer per-attempt+total-budget composition (§2's "Rejected alternative") and adding retry to the `/health/ready` Npgsql check itself (§"Grounding" point 4).
