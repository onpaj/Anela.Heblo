# Chronic Npgsql `TaskCanceledException@NpgsqlWriteBuffer.Flush` — pool-contention confirm/refute query — 2026-07-30

Fix shipped this run (see `.artifacts/tsk_6c0c911765c14ae9/`):
- `TransientErrorClassifier` now classifies `Polly.Timeout.TimeoutRejectedException` as transient, so a
  Polly-internal per-attempt timeout is retried instead of propagating raw (`TransientErrorClassifier.cs`).
- `Database:Resilience:TotalTimeBudget` reduced `00:00:10` → `00:00:03` in `appsettings.json`,
  `appsettings.Staging.json`, `appsettings.Production.json` — bounds the new worst-case latency
  (~13.4s for a fully-wedged call, 4 attempts) after the classifier change makes timeouts retryable.
- `PollyExecutionStrategy` tags rethrown exceptions with `.Data["Anela.DbRetryAttempts"]` so callers can
  distinguish "failed on first try" from "retried and still failed."
- `HebloFeatureProvider.ResolveBooleanValueAsync`'s fallback-path log now includes
  `dbRetryExhausted`/`attempts`, closing the observability gap left by #893 (which only covered the
  health-check registration, not this DB path).
- `NpgsqlConnectionInterceptor.LogConnectionFailure` (the `ConnectionFailed`/`ConnectionFailedAsync` path)
  now also records `npgsql.pool.exhaustion_wait_seconds` and logs `wait_seconds`, mirroring what the
  success path (`ConnectionOpened`) already did — previously a connection cancelled while queued for a
  free pool slot left no wait-time trace, which is exactly the scenario this whole signal is about.

## Why raw exception count is not the success signal for this fix

App Insights' ADO.NET/Npgsql auto-instrumentation taps DiagnosticSource events inside Npgsql's own
write-buffer flush — one layer below where Polly wraps the failure into a `TimeoutRejectedException`.
That means `TaskCanceledException@NpgsqlWriteBuffer.Flush` can keep appearing in the `exceptions` table at
roughly the same volume even for calls Polly now successfully retries. **Do not use this problemId's raw
row count as the pass/fail signal.** Use instead:
- `db.retry.success` count for this exception family (new — previously these never reached the retry
  path at all, since the classifier didn't recognize `TimeoutRejectedException` as transient).
- `dbRetryExhausted=true` frequency in `FeatureFlags/Get` logs, trending down.
- Request-level failure rate / retry-exhaustion count (`db.retry.failure`), not raw exception volume.

## Confirm/refute query — does cancellation correlate with pool contention?

This answers the plan's FR-3/FR-4 gate: don't touch `MaxPoolSize`/`Hangfire:ConnectionLimit` again
without evidence. Joins on `operation_Id` (shared by every trace/exception/dependency row logged within
one request, including `NpgsqlConnectionInterceptor`'s own log lines).

```kusto
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

Run with:
```bash
./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P7D '<query above, single line>'
```

**Read the result as:**
- `pctCoincidingWithPoolContention` high (most failures share an `operation_Id` with a
  `wait_seconds > 1.0` trace) → pool contention is the driver. Supports an FR-4 pool-size follow-up
  **with this evidence attached to the PR**.
- `pctCoincidingWithPoolContention` low → **do not conclude "no pool contention" outright.** A connection
  open cancelled by the ambient token *before* Npgsql raises either `ConnectionOpened` or
  `ConnectionFailed` is a known residual blind spot (see `NpgsqlConnectionInterceptor`'s class doc comment)
  — it produces no `DbPoolExhaustionWait`/`DbConnectionFailed` trace at all, so a low correlation number
  could mean "no contention" or "contention that manifested through this exact silent path." If low,
  the next hypothesis is host-level (thread-pool starvation, GC pauses, Azure-side network jitter) or
  this residual blind spot — not another blind pool-size tuning pass.

## Post-deploy check (run across the next two P7D windows)

```kusto
exceptions
| where problemId has "NpgsqlWriteBuffer" and type == "System.Threading.Tasks.TaskCanceledException"
| summarize count() by bin(timestamp,1d)
| order by timestamp asc
```

Compare against `Polly.Timeout.TimeoutRejectedException` volume in the same window — if
`TimeoutRejectedException` starts appearing in `exceptions` at the volume the raw `TaskCanceledException`
count used to have, the classifier fix masked the problem (still cancelling, just under a different type
name after retries are exhausted) rather than fixing it, and that needs its own follow-up.

| Date | `TaskCanceledException@NpgsqlWriteBuffer.Flush` occurrences | Result |
|---|---|---|
| _(fill in after first post-deploy P7D window)_ | | |
| _(fill in after second post-deploy P7D window)_ | | |
