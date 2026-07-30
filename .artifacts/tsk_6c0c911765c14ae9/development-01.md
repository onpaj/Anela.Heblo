# Development: Chronic Npgsql `TaskCanceledException@NpgsqlWriteBuffer.Flush` connectivity failures

Implements design-01.md exactly as approved by architecture-01.md, including both required corrections
(latency-framing fix folded directly into code comments/doc, and the new `NpgsqlConnectionInterceptor`
test + residual-blind-spot note).

## Files changed

**Source**
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/TransientErrorClassifier.cs` — added
  `Polly.Timeout.TimeoutRejectedException => true` to `IsTransientCore`'s switch. Verified via a Polly
  8.4.1 probe that a raw ambient `TaskCanceledException` (caller-token cancellation) still falls through
  to `_ => false` unchanged — no special-casing needed, confirming the design's grounding claim.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/DbResilienceOptions.cs` — added an XML
  doc comment on `TotalTimeBudget` clarifying it's a per-attempt ceiling, not a total budget, in the
  current pipeline composition (`AddRetry(...).AddTimeout(...)`). Default value left at 10s (per-file
  fallback, unused in practice since every environment's `appsettings.*.json` sets it explicitly).
- `backend/src/Anela.Heblo.API/appsettings.json`, `appsettings.Staging.json`, `appsettings.Production.json`
  — `Database:Resilience:TotalTimeBudget` `00:00:10` → `00:00:03`. Bounds the new worst case (a fully-
  wedged call that now retries on `TimeoutRejectedException`) to `4 × 3s + ~1.4s backoff ≈ 13.4s` instead
  of `4 × 10s + ~1.4s ≈ 41s`. **This is a latency increase from today's ~10s single-attempt-then-fail
  behavior, not a decrease** — the architecture review flagged this exact framing point; the code and
  test comments describe it correctly as a tradeoff (bounded worst case, better common case where a
  retry succeeds within seconds), not as a pure improvement.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs` — both
  `Execute` and `ExecuteAsync` catch blocks now tag the rethrown exception with
  `ex.Data["Anela.DbRetryAttempts"] = attempt` immediately before the existing metric/log/rethrow.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` —
  `ResolveBooleanValueAsync`'s catch block reads that tag (`attempts`, default `1` if absent) and adds
  `dbRetryExhausted`/`attempts` to the existing warning log. Return contract (`ResolutionDetails<bool>`,
  fail-open to `defaultValue`) is unchanged.
- `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/NpgsqlConnectionInterceptor.cs` —
  `LogConnectionFailure` (the `ConnectionFailed`/`ConnectionFailedAsync` path) now also stops the
  open-in-flight stopwatch and records `RecordPoolExhaustionWait` + logs `wait_seconds`, mirroring what
  `RecordOpenLatency` (the success path) already did. Both now share a `StopAndGetElapsedSeconds` helper.
  Class doc comment extended to document the residual blind spot the architecture review called out: a
  connection-open cancelled between `ConnectionOpening` and either `ConnectionOpened`/`ConnectionFailed`
  firing still produces no wait-time signal — there's no lower-level Npgsql/ADO.NET hook available to
  this interceptor to close that gap.

**Docs**
- `docs/routines/telemetry-anomaly/2026-07-30-npgsql-cancellation-pool-correlation.md` — new dated note
  (matching the existing `2026-06-13-*.md` pattern) with: a summary of what shipped, why raw exception
  count in App Insights is not the success signal (Npgsql's own DiagnosticSource events fire below
  Polly's wrap point), the FR-3/FR-4 confirm/refute KQL query joining `TaskCanceledException` failures
  against `DbPoolExhaustionWait`/`DbConnectionFailed` traces by `operation_Id`, how to read a low
  correlation result given the residual blind spot, and a post-deploy tracking table for the next two
  P7D windows.

**Tests**
- `TransientErrorClassifierTests.cs` — two new cases: `TimeoutRejectedException` → transient;
  ambient-token `TaskCanceledException` → not transient (regression guard for the "must not touch this"
  behavior).
- `DbResiliencePipelineProviderTests.cs` — two new pipeline-level tests:
  - `Pipeline_RetriesTimeoutRejectedException_WhenOperationHangsPastPerAttemptBudget` — a hanging
    operation that observes its per-attempt cancellation token retries 4 times (1 + `MaxRetryAttempts`)
    and the total elapsed time stays within a generous bound derived from `(MaxRetryAttempts+1) ×
    TotalTimeBudget + backoff`.
  - `Pipeline_DoesNotRetry_OnAmbientCancellation` — an ambient/caller token firing mid-operation is
    never retried (`calls == 1`), confirming the NFR "no behavior change for genuine client/probe
    cancellation" holds through the full pipeline, not just the classifier in isolation.
- `ProductionConnectionStringDefaultsTests.cs` — updated `Production_ResilienceOptions_MatchSpec` to
  assert `TotalTimeBudget == 3s` (was `10s`), matching the deliberate config change, with a `because`
  string explaining why.
- `HebloFeatureProviderTests.cs` — two new tests verifying the `dbRetryExhausted`/`attempts` log fields:
  one with `.Data["Anela.DbRetryAttempts"] = 3` (exhausted-retry case), one with no tag set (first-try
  failure case, `attempts` defaults to `1`). Both use the repo's existing `Mock<ILogger<T>>` +
  `It.IsAnyType` verification pattern (matching `CatalogDataRefreshServiceTests.cs`'s established style).
- `NpgsqlConnectionInterceptorTests.cs` — new file (none existed before, per the architecture review's
  required addition). Three tests: `ConnectionFailed` after `ConnectionOpening` records pool-exhaustion
  wait once elapsed exceeds the 1s threshold (asserted via a `MeterListener` on the real
  `DbResilienceMetrics` histogram, plus a log-field assertion); `ConnectionFailed` with no prior
  `ConnectionOpening` records nothing; a regression test confirming the `RecordOpenLatency` /
  `StopAndGetElapsedSeconds` refactor didn't change the pre-existing success-path behavior.

## Notable implementation-time finding (deviates from design-01.md's stated grounding, code behavior unaffected)

Design-01.md's grounding claimed a "permanently-hanging operation (never respects its own token)" retries
correctly bounded per-attempt. I reproduced this empirically against the installed Polly 8.4.1 package
before writing the pipeline-level test and found that claim is only true if the operation observes the
per-attempt `CancellationToken` Polly passes into the delegate (e.g. `Task.Delay(x, ct)`). An operation
that truly ignores all cancellation (`Task.Delay(x, CancellationToken.None)`) cannot be preempted by
Polly's timeout strategy at all — Polly can only race a timer against the delegate's *own* completion,
not forcibly abort it, so the pipeline just waits for the real operation to finish. This doesn't affect
production correctness (every real Npgsql/EF Core async call does observe its `CancellationToken`
parameter — that's how `AddTimeout` is meant to be used), but it did require fixing the pipeline test's
hanging operation to pass `ct` through, rather than `CancellationToken.None`, to get a result matching
the design's documented behavior. No production code needed to change for this.

## Verification performed

- `dotnet build` on `Anela.Heblo.Persistence`, `Anela.Heblo.Application`, and the full
  `Anela.Heblo.Tests` project: **0 errors** (all warnings pre-existing and unrelated to this change).
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <all changed files>`: **clean**, no
  formatting diffs.
- `dotnet test --filter "FullyQualifiedName~Persistence.Resilience|FullyQualifiedName~Features.FeatureFlags.HebloFeatureProviderTests"`:
  **47/48 passed**. The one failure, `DbResiliencePipelineProviderTests.Pipeline_AbortsByTotalTimeBudget`,
  is a **pre-existing environment-timing flake unrelated to this change** — confirmed by `git stash`-ing
  all changes in this task and re-running the same test against the unmodified base commit, where it
  fails identically (asserts `< 5s`, this sandbox's CPU/scheduler makes 50 retry attempts take 6-7s). Not
  touched or introduced by this work.
- Full suite (`dotnet test` on `Anela.Heblo.Tests.csproj`, no filter): 6062 passed, 45 failed, 4 skipped.
  All 45 failures are pre-existing Testcontainers-backed integration tests (`*IntegrationTests` under
  `Persistence/Smartsupp`, `Features/MeetingTasks`, `Features/Leaflet`, `KnowledgeBase/Integration`) that
  spin up a real Postgres container via `NpgsqlDataSourceBuilder(_container.GetConnectionString())` —
  this sandbox has no Docker/container runtime available, so these fail at `InitializeAsync` regardless
  of this change (`ManyServiceProvidersCreatedWarning` cascading from repeated failed `DbContext`
  construction). None touch any file this change modified.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
dotnet build Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Persistence.Resilience|FullyQualifiedName~Features.FeatureFlags.HebloFeatureProviderTests"
```
Expect 47/48 to pass; the one pre-existing timing-flaky test is unrelated (see above).

Post-deploy: run the KQL in `docs/routines/telemetry-anomaly/2026-07-30-npgsql-cancellation-pool-correlation.md`
against the next two P7D windows and fill in the tracking table there, per FR-3/FR-4's evidence-gated
follow-up.
