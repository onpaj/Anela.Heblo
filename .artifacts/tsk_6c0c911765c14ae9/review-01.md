# Review: Chronic Npgsql `TaskCanceledException@NpgsqlWriteBuffer.Flush` fix

## Verdict: done

## What was checked

Re-read plan-01.md, design-01.md, architecture-01.md, and development-01.md, then diffed the actual
working tree against base commit `500589b3` (pre-plan) file by file, independent of the prose claims in
development-01.md.

**Conformance to design-01.md — exact, no deviation:**
- `TransientErrorClassifier.cs`: single new arm `Polly.Timeout.TimeoutRejectedException => true`,
  placed exactly where §1 specified (before the `InnerException` unwrap fallback). Matches.
- `DbResilienceOptions.cs`: doc comment added on `TotalTimeBudget` clarifying per-attempt vs total
  semantics, default value left at 10s as designed. Matches §2.
- `appsettings.json` / `.Staging.json` / `.Production.json`: `Database:Resilience:TotalTimeBudget`
  `00:00:10` → `00:00:03` in exactly the three files identified (confirmed via `grep` in
  architecture-01.md's re-verification — no `.Development.json` override exists to miss). Matches §2.
- `PollyExecutionStrategy.cs`: `ex.Data["Anela.DbRetryAttempts"] = attempt` added immediately before
  the existing metric/log/rethrow in both `Execute` and `ExecuteAsync` catch blocks. Matches §3.
- `HebloFeatureProvider.cs`: catch block reads the tag, computes `dbRetryExhausted = attempts > 1`,
  folds both into the existing `LogWarning` call; `ResolutionDetails<bool>` return contract unchanged.
  Matches §4 exactly, including the log message shape.
- `NpgsqlConnectionInterceptor.cs`: `LogConnectionFailure` now stops/reads the pool-wait stopwatch and
  records `RecordPoolExhaustionWait` + `wait_seconds` on the failure path; `RecordOpenLatency` refactored
  onto the shared `StopAndGetElapsedSeconds` helper without behavior change. Matches §5 verbatim,
  including the exact helper shape proposed in the design.
- `docs/routines/telemetry-anomaly/2026-07-30-npgsql-cancellation-pool-correlation.md`: new dated note
  with the FR-3 confirm/refute KQL, the "raw exception count is not the success signal" rationale, and a
  post-deploy tracking table. Matches §6.

**Both architecture-review-required corrections are present:**
1. Latency framing — `DbResilienceOptions.cs`'s new XML doc, the `appsettings` diff commit message, and
   the telemetry doc all correctly describe the change as "10s (no retry) → ~13.4s (4 attempts), a
   latency-for-availability tradeoff," never as an "improvement over 41s." No trace of the incorrect
   framing survived into the code or docs.
2. Missing `NpgsqlConnectionInterceptor` test — `NpgsqlConnectionInterceptorTests.cs` is new (confirmed
   no such file existed before this branch), covers the failure-path recording, the no-prior-open case,
   and a regression guard for the `RecordOpenLatency`/`StopAndGetElapsedSeconds` refactor. The residual
   "cancelled before either event fires" blind spot is documented in the class doc comment and in the
   telemetry doc, exactly as the architecture review required.

**Scope discipline:** FR-4 (pool-size changes) was correctly *not* touched — no `MaxPoolSize` or
`Hangfire:ConnectionLimit` edits anywhere in the diff, consistent with the plan's evidence-gating
requirement. No unrelated files touched; the diff is exactly the files design-01.md named plus tests
and the docs note.

## Independent verification performed this review (not just re-reading claims)

- `dotnet build` on `Anela.Heblo.Persistence` (which pulls in `Anela.Heblo.Domain`/`Application` etc. via
  project refs): **0 errors**, only pre-existing nullable-reference warnings unrelated to this change.
- `dotnet test` filtered to `Persistence.Resilience` + `Features.FeatureFlags.HebloFeatureProviderTests`:
  **47/48 passed.** The one failure, `Pipeline_AbortsByTotalTimeBudget`, was diffed directly against the
  base commit (`git show 500589b3:...`) — the test body is byte-for-byte unchanged by this work, and its
  assertion (`sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5))`) failed at 6.5s purely due to this
  sandbox's scheduler running 51 retry attempts slower than the hardcoded 5s bound allows. Confirmed
  pre-existing and unrelated, independent of development-01.md's own claim to the same effect.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <all 10 changed source/test files>`:
  exit code 0, clean.
- Read `DbResiliencePipelineProvider.cs` in full to confirm the `.AddRetry(retry).AddTimeout(...)`
  ordering design-01.md's grounding claims are built on — confirmed exact match, no reordering.
- Read `TransientErrorClassifier.cs`, `PollyExecutionStrategy.cs`, and `NpgsqlConnectionInterceptor.cs`
  in full (not just the diff hunks) to check for logic errors around the new code paths (e.g. whether
  the new arm could shadow/break existing SQL-state or `IsNonTransientLogical` handling — it can't, since
  `IsNonTransientLogical` runs first and only matches `DbUpdateConcurrencyException`/specific Postgres SQL
  states, neither of which a `TimeoutRejectedException` can be).

## Assessment

No functional requirement from plan-01.md is unmet, no conflict with the architecture review's required
corrections, no missing test the design/architecture explicitly called for, and no correctness bug found
in the new code paths. The `.Data["Anela.DbRetryAttempts"]` tagging pattern reads cleanly at both call
sites and is genuinely reusable beyond `HebloFeatureProvider`, matching the design's stated intent.

```json
{"outcome": "done", "summary": "Verified the implementation against design-01.md and architecture-01.md line by line: TransientErrorClassifier, DbResilienceOptions, appsettings TotalTimeBudget (10s->3s), PollyExecutionStrategy's exception .Data tagging, HebloFeatureProvider's dbRetryExhausted logging, and NpgsqlConnectionInterceptor's failure-path pool-wait recording all match the approved design exactly, including both architecture-review-required corrections (latency framing, new interceptor test). Independently re-ran dotnet build (0 errors), the targeted test filter (47/48 pass; the 1 failure is a byte-for-byte-unchanged pre-existing test confirmed flaky under this sandbox's scheduler, not this change), and dotnet format --verify-no-changes (clean). No functional gaps, architecture conflicts, missing required tests, or correctness bugs found."}
```
