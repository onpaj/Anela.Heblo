# Plan: Chronic Npgsql `TaskCanceledException@NpgsqlWriteBuffer.Flush` connectivity failures

## Summary

A connectivity-cancellation signal (`TaskCanceledException` at `NpgsqlWriteBuffer.Flush`, plus sibling `PostgresException`/`SocketException`/`OperationCanceledException` at other Npgsql frames) has recurred at meaningful volume every week since April, and has now spread from `/health/ready` to `FeatureFlags/Get`, `StockUpOperations/GetSummary`, and `Configuration`. Five prior issues were closed against this family; investigation shows four of them (#591, #592, #893, #3193, plus an unlisted #680) landed real, still-present code fixes (pool caps, idle-lifetime pruning, keepalive/connection-lifetime, shared health-check data source, Hangfire retry/activity fixes) — the "never applied" framing in the telemetry report is not quite right. The actual gap is narrower and more specific: the resilience pipeline added in between (`PollyExecutionStrategy` + `TransientErrorClassifier`) never learned to classify cancellation/timeout exceptions as transient, so exactly this exception family bypasses retry and propagates raw, and the one FeatureFlags-specific code path was never covered by the health-check-scoped #893 fix.

## Context

This is the sixth report against the same telemetry signal. The prior four "fix and close" cycles addressed real but adjacent problems (pool exhaustion, idle-socket pruning after server restarts, health-check-specific connection reuse, Hangfire retry storms) without resolving the core issue, and a fifth (#3256) was explicitly declined at grooming because its recommendations turned out to already be implemented and its root cause was unconfirmed. Continuing to guess-and-tune (as the last five attempts did) risks a sixth non-fix. This plan prioritizes narrowing the actual code gap (found below) and adding the diagnostic signal needed to confirm it, over another blind config tweak.

## Investigation findings (grounds this plan)

- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` builds one singleton `NpgsqlDataSource` shared by the app `DbContext` (scoped, not pooled — `AddDbContext`, not `AddDbContextPool`) **and** the `/health/ready` Npgsql health check (`ServiceCollectionExtensions.cs:118-121`), confirming PR #920 (fixing #893) is in place. `KeepAlive=30`/`ConnectionLifetime=600` (PR #687/#680) and configurable `MaxPoolSize`/`ConnectionIdleLifetime`/`ConnectionPruningInterval` (PRs #662/#671, fixing #591/#592) are also in place. Current values: Production `MaxPoolSize=20`, Staging `MaxPoolSize=10`; Hangfire uses a **separate** pool capped at `ConnectionLimit=5` via `Hangfire.PostgreSql`, independent of the app's `NpgsqlDataSource`.
- `#3256`'s recommendations (Connection Lifetime tuning) were already implemented by #680/#687 before #3256 was even filed; grooming correctly declined it as a non-actionable, ops-only, root-cause-unconfirmed item (confirmed via the closing comment) rather than silently ignoring it.
- All DB calls run through a custom `PollyExecutionStrategy` (`Infrastructure/Resilience/PollyExecutionStrategy.cs`) delegating to a singleton Polly pipeline: `AddRetry(...).AddTimeout(TotalTimeBudget = 10s)` (`DbResilienceOptions`, default 10s). **`TransientErrorClassifier.IsTransientCore` (`Infrastructure/Resilience/TransientErrorClassifier.cs:45-56`) has no case for `OperationCanceledException`/`TaskCanceledException`, nor for Polly's own `TimeoutRejectedException`.** Any cancellation — whether from Polly's internal 10s budget firing under connection-pool contention, or from the ambient/caller token (`HttpContext.RequestAborted` for a web request, or a health-probe client disconnecting) — is therefore never retried and propagates as an "unhandled" failure straight into telemetry, matching the reported signature exactly (no accompanying `PostgresException`, tight clusters, multiple endpoints simultaneously).
- `HebloFeatureProvider.GetOverridesAsync` (`Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:90-101`) caches DB results for 30s and, on cache miss, calls `IFeatureFlagOverrideRepository.GetAllAsDictionaryAsync` — a plain `ApplicationDbContext` query going through the same shared pool/Polly pipeline as everything else. Its only cancellation handling is a no-op `ct.ThrowIfCancellationRequested()`; a catch-all in `ResolveBooleanValueAsync` swallows any exception into a config/default fallback, which is why this path degrades gracefully for callers but still emits raw exceptions into App Insights. #893's fix only touched the health-check *registration*, never this path — exactly matching the report's observation that `FeatureFlags/Get` is "a code path not covered by #893's original scope."
- No current telemetry/metric distinguishes "cancelled because Polly's own 10s budget elapsed while waiting on a saturated pool" from "cancelled because the caller (browser/health-probe) already gave up" from "cancelled because Postgres itself restarted." `DbResilienceMetrics` records retry attempts/success/failure by exception type name, but not pool-saturation state at time of failure.

## Functional requirements

**FR-1 — Classify Polly-internal timeout cancellations as transient, leave caller-cancelled ones alone**
`TransientErrorClassifier.IsTransient` must return `true` for `Polly.Timeout.TimeoutRejectedException` (and any `OperationCanceledException`/`TaskCanceledException` nested beneath it) so `PollyExecutionStrategy`'s retry actually fires when the *pipeline's own* timeout — not the caller — triggered the cancellation. It must continue to return `false` for a cancellation driven by the ambient/original `CancellationToken` passed into `ExecuteAsync` (e.g. `HttpContext.RequestAborted`), since retrying after the caller has already given up wastes a pool slot and prolongs contention rather than fixing it.
- Acceptance: new `TransientErrorClassifierTests` cases — (a) `TimeoutRejectedException` → transient; (b) `TaskCanceledException` constructed from an already-cancelled ambient token (not wrapped in `TimeoutRejectedException`) → not transient; (c) all existing classified cases (`PostgresException` codes, `SocketException`, `TimeoutException`, `IOException`, non-transient `23505`/`23503`/`23502`) unchanged — no regression.
- Must be verified against a small integration/unit test that exercises the actual `ResiliencePipelineBuilder.AddRetry().AddTimeout()` composition used in `DbResiliencePipelineProvider`, to confirm which concrete exception type reaches the classifier when the *inner* timeout fires vs when the *ambient* token fires (see Open Questions — this determines whether FR-1's premise about `TimeoutRejectedException` holds for this specific Polly version/composition).

**FR-2 — Cover the FeatureFlags/Get DB path explicitly**
`HebloFeatureProvider.GetOverridesAsync`'s DB round-trip must benefit from the same classify-and-retry behavior as any other `ApplicationDbContext` call (it already goes through `PollyExecutionStrategy`, so FR-1 covers it automatically — this FR is about verifying that and closing the observability gap, not adding a second retry layer). On retry-exhaustion it must keep today's graceful fallback (serve cached/default flag value, never throw to the OpenFeature caller) but must emit a structured, distinctly-taggable log/telemetry event (e.g. `dbRetryExhausted=true` on the existing error log) so retry-exhausted failures are distinguishable from successful first-attempt reads in dashboards — today both look identical ("Reason.Error" swallowed silently).
- Acceptance: unit test simulating a single transient cancellation on `GetAllAsDictionaryAsync` that succeeds on retry — asserts flags are served correctly, no exception surfaces to the caller, no distinct "exhausted" tag emitted. Second test simulating exhausted retries — asserts fallback behavior is unchanged from today and the new tag is emitted exactly once.

**FR-3 — Make cancellation source observable (diagnosis, not another blind tuning pass)**
Extend `DbResilienceMetrics` (or add a companion counter) to record, per DB-call failure, which concrete exception type surfaced (`PostgresException`+SqlState / `SocketException` / `TimeoutRejectedException` / ambient-cancellation) and whether the connection pool was at `MaxPoolSize` capacity at that moment (Npgsql exposes pool statistics via its `EventCounters` / `NpgsqlDataSource` — use whichever is already available in the installed Npgsql version without adding a new package).
- Acceptance: a documented App Insights query (added under `docs/routines/telemetry-anomaly/` alongside the existing query scripts) can answer "of the `TaskCanceledException@NpgsqlWriteBuffer.Flush` occurrences in a window, how many coincided with pool-at-capacity." This is the confirm/refute gate for FR-4 — don't change pool sizing again without this evidence, since the last five attempts tuned blind.

**FR-4 — Rebalance pool capacity (conditional on FR-3 evidence)**
If FR-3 shows cancellations correlating with pool-at-capacity, raise `Database:MaxPoolSize` for Production/Staging and/or reassess `Hangfire:ConnectionLimit`'s isolation from the app pool, sized against the actual Azure PostgreSQL Flexible Server tier's `max_connections` (an Azure Portal/ops lookup, not derivable from the repo — flag as a required manual pre-merge step, consistent with how #3256's grooming treated this class of change).
- Acceptance: config change plus updated `PersistenceModuleTests` assertions for the new value(s); `dotnet build` + full test suite pass; no `MaxPoolSize` change lands without the FR-3 evidence attached to the PR description.

## Non-functional requirements

- **Reliability target**: `TaskCanceledException@NpgsqlWriteBuffer.Flush` daily count should trend toward the observed healthy-day baseline (3/day, per 07-26) across the two P7D windows following deployment — not merely shift to a different exception type (e.g. `TimeoutRejectedException` appearing at the same volume would mean FR-1 masked rather than fixed the problem; this must be checked explicitly before declaring success).
- **Latency**: retrying a Polly-internal-timeout cancellation must stay within the existing `TotalTimeBudget` (10s) — confirm whether retry is per-attempt or shared-budget in the current `AddRetry().AddTimeout()` composition (see Open Questions) so a retry doesn't silently double caller-visible latency.
- **No behavior change for genuine client/probe cancellation** — FR-1 must not cause retries to fire (and hold a pool connection longer) when the caller has already disconnected; verify this explicitly, since it's the one way this fix could make pool contention worse instead of better.
- **Secrets/config**: any pool-size or connection-string changes go through Key Vault (`kv-heblo-stg`) / existing `Database:*` config keys per `CLAUDE.md` — never hardcoded, never via Azure Portal App Settings.

## Data model

No persisted entities change. Relevant existing config/telemetry surface:
- `DbResilienceOptions` (`Database:Resilience:*` config section) — `MaxRetryAttempts`, `BaseDelay`, `MaxRetryDelay`, `TotalTimeBudget`.
- `Database:MaxPoolSize` / `ConnectionIdleLifetime` / `ConnectionPruningInterval` (per-environment `appsettings.{Environment}.json`), plus hardcoded `KeepAlive=30`/`ConnectionLifetime=600` in `PersistenceModule.cs`.
- `Hangfire:ConnectionLimit` — separate pool, not shared with the app `NpgsqlDataSource`.
- `DbResilienceMetrics` — existing counters (`RecordRetryAttempt`, `RecordRetrySuccess`, `RecordRetryFailure`); FR-3 extends this with an exception-classification + pool-saturation dimension.

## Interfaces

No public/REST API contract changes. Internal surfaces touched:
- `TransientErrorClassifier.IsTransient(Exception)` — pure classification function, behavior extended per FR-1.
- `DbResilienceMetrics` — new dimension/counter per FR-3, consumed by the existing App Insights instance the `telemetry-anomaly` routine already queries (`docs/routines/telemetry-anomaly/appinsights-query.sh`).
- `HebloFeatureProvider.GetOverridesAsync` — added structured log tag per FR-2, no signature change.
- No new HTTP endpoints; `/health/ready`, `/health/live`, `FeatureFlags/Get` behavior is unchanged from the caller's perspective (fewer unhandled-exception telemetry events, same responses).

## Dependencies and scope

**Rests on**: the existing `Infrastructure/Resilience/*` subsystem (Polly pipeline, `TransientErrorClassifier`, `DbResilienceMetrics`) introduced by the earlier `#3028` work; the shared singleton `NpgsqlDataSource` from PR #920; existing App Insights instance and the `telemetry-anomaly` routine's query tooling.

**Explicitly out of scope**:
- Changing the Azure PostgreSQL Flexible Server tier/plan or its `max_connections` — an Azure-side ops decision requiring live portal access, same reasoning grooming used to decline #3256.
- The `DateTimeConverterResolver.Get` type-conversion bugs (#3592, #3757) — unrelated exception family, explicitly called out as distinct in the source report.
- A broader persistence-layer redesign (e.g. switching to `AddDbContextPool`, moving Hangfire onto the shared data source) — worth noting as a possible future simplification but not required to close this signal; do not do it opportunistically per the "surgical changes" rule.
- Re-doing the already-landed pool-sizing/keepalive/health-check work from #591/#592/#893/#3193/#680 — verify it's intact, don't re-implement it.

## Rough plan

1. Confirm exactly which exception type reaches `TransientErrorClassifier` when Polly's internal `AddTimeout` fires vs when the ambient token fires, using a small targeted test against the real `ResiliencePipelineBuilder.AddRetry().AddTimeout()` composition (resolves the FR-1 premise and the retry-budget Open Question below) — do this before writing the classifier fix, not after.
2. Implement FR-1 (`TransientErrorClassifier` + tests) and FR-2 (`HebloFeatureProvider` telemetry tag + tests).
3. Implement FR-3's pool-saturation/exception-classification metric and the accompanying documented App Insights query.
4. Ship 1–3 together; explicitly do **not** touch `MaxPoolSize`/`ConnectionLimit` in the same change (that's FR-4, gated on evidence).
5. After deployment, re-run the P7D query from the source report across the next two windows; if cancellations persist and FR-3's data shows pool-at-capacity correlation, open a tightly-scoped FR-4 follow-up with that evidence attached — if it shows no pool correlation, the next hypothesis becomes host-level (thread-pool starvation, GC pauses, or Azure-side transient network jitter) rather than another Npgsql config tweak.
6. Run `dotnet build`, `dotnet format`, and the full backend test suite before considering this done, per repo validation rules.

## Open questions

- **Does this Polly composition apply the 10s timeout per-attempt or as a shared budget across retries?** `AddRetry(...).AddTimeout(...)` order determines whether `AddTimeout` wraps each individual attempt (generous — 10s per try) or the whole retry loop (tight — 10s total). The field is named `TotalTimeBudget` implying intent, but Polly v8 strategy order may not deliver that. Default assumption for planning: treat this as unconfirmed and resolve it in step 1 of the rough plan before finalizing FR-1's fix, since it affects whether retrying a Polly-timeout cancellation is even safe within the existing latency budget.
- **Does Application Insights capture this exception via automatic low-level ADO.NET/Npgsql dependency tracking, independent of how Polly ultimately wraps/retries it higher up the call stack?** If so, some exception volume may persist in telemetry even after a successful retry (attempted-then-recovered noise, not a real failure) — the real success metric to watch post-fix should be request-level failure rate and retry-exhaustion count (already partially available via `DbResilienceMetrics`), not raw exception-count in App Insights. Default: track both, but treat retry-exhaustion count as the primary signal for "is this actually fixed."
- **Current Azure PostgreSQL Flexible Server tier and live `max_connections`/utilization** — unknown from the repo; needed to judge whether `MaxPoolSize=20`/`10` plus Hangfire's separate `ConnectionLimit=5` are conservative-but-fine or the actual bottleneck. Default: don't guess: gate any pool-size change on FR-3's evidence and an explicit Azure Portal metrics check, not on rerunning this loop a sixth time.
- **Request volume/polling frequency for `FeatureFlags/Get` and `/health/ready`** — not derivable from the backend alone (frontend polling interval / Azure health-probe interval). If either turns out to be far more frequent than expected, lengthening `HebloFeatureProvider`'s 30s cache or reusing its cached DB round-trip for `/health/ready` instead of a dedicated ping could reduce contention further — treat as an optional follow-up, not part of this plan's default scope.
