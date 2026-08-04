# Development — FlexiBee ERP dependency latency: circuit breaker + evidence-first investigation

Implements the plan/design/architecture-review chain (`plan-01.md`, `design-01.md`,
`architecture-01.md`), including the two blocking fixes architecture review required
before implementation (predicate correctness, throughput/sampling defaults).

## What was implemented

### FR-1 — Attribution query: attempted, blocked by environment, documented as a gap

`./docs/routines/telemetry-anomaly/appinsights-query.sh --test` fails in this
development environment (`APPINSIGHTS_APP_ID`/`APPINSIGHTS_API_KEY` not set) — the
live per-resource KQL drill-down could not be run here. This is recorded honestly in
`docs/integrations/flexibee-api.md` §2.3 rather than fabricated or silently skipped.
The confirm-vs-background split conclusion (§2.2) is instead derived from the
signal's own call-volume math and each named endpoint's own p95, which is weaker
evidence than the planned direct measurement — flagged as such, with the query
ready to run as soon as credentials are available.

### FR-2 — `docs/integrations/flexibee-api.md` (new file)

Written following the `shoptet-api.md` convention: overview, latency findings
(headline numbers from the signal, confirm-path call volume, the FR-1 gap above,
and the still-unexplained 300s→75s `max` drop), and an explicit decision section.

### FR-3 — `ManufactureErpResilienceService` (Polly circuit breaker)

New files:
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureErpResilienceService.cs`
  — `IManufactureErpResilienceService` + implementation, `ResiliencePipelineBuilder`
  with `AddCircuitBreaker` only (no retry, no inner timeout — the existing
  `ErpTimeoutSeconds` `CancelAfter` remains the per-call time budget).
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/Exceptions/ManufactureErpUnavailableException.cs`
  — typed exception thrown when the circuit is open (placed under `Infrastructure/Exceptions/`
  per architecture review's minor item, matching the `ProductMarginsException` precedent).
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ErpCircuitOpenFilter.cs`
  — `IManufactureErrorFilter` translating the new exception into a Czech
  "FlexiBee je aktuálně nedostupný..." message; auto-discovered by the existing
  `services.Scan(...)` in `ManufactureModule.cs`, no DI change needed for it.

Changed files:
- `ManufactureErpOptions.cs` — four new config keys (`ErpCircuitBreakerMinimumThroughput`,
  `ErpCircuitBreakerFailureRatio`, `ErpCircuitBreakerSamplingDurationSeconds`,
  `ErpCircuitBreakerBreakDurationSeconds`), additive/backward-compatible under the
  existing `ManufactureErp` config section.
- `SubmitManufactureHandler.cs` — takes `IManufactureErpResilienceService` and routes
  the `SubmitManufactureAsync` call through `ExecuteAsync`.
- `ManufactureModule.cs` — `AddSingleton<IManufactureErpResilienceService, ManufactureErpResilienceService>()`
  (singleton: breaker state must persist across requests, same lifetime choice as
  `CatalogResilienceService`).

**Architecture review's two blocking findings, addressed:**

1. **Cancellation predicate (Finding 1).** `SubmitManufactureHandler` now passes the
   request's *original*, un-linked `cancellationToken` as `ExecuteAsync`'s third
   argument — not the linked `cts.Token` that has `CancelAfter` armed. The operation
   lambda itself still closes over `cts.Token` directly (ignoring the `ct` Polly
   hands it), so the actual FlexiBee call remains bounded by `ErpTimeoutSeconds`.
   Inside `ManufactureErpResilienceService`, `ShouldHandle` checks
   `args.Context.CancellationToken.IsCancellationRequested` — bound from that
   original token — instead of `ex.CancellationToken`. Result: a `CancelAfter`-fired
   timeout (linked token cancelled, original token not) is counted as a breaker
   failure; a genuine caller cancellation (original token cancelled) is not. Covered
   by `ManufactureErpResilienceServiceTests.ExecuteAsync_CancelAfterTimeout_IsCountedAsFailure_OpensCircuitAfterThreshold`
   (reproduces the exact `CreateLinkedCts`/`CancelAfter` shape, not a bare
   `TaskCanceledException`) and `..._CallerCancelled_PropagatesUnchanged_AndIsNotCountedAsFailure`.
2. **Throughput/sampling defaults (Finding 2).** Not copied from `CatalogResilienceService`.
   `ErpCircuitBreakerMinimumThroughput` defaults to 2 (was 3) and
   `ErpCircuitBreakerSamplingDurationSeconds` to 900s/15min (was 60s), sized against
   this call site's ~2.4 combined confirm-calls/hour rather than Catalog's much
   higher-frequency cache-refresh background sync. Documented with the reasoning
   inline as XML doc comments on `ManufactureErpOptions`.

**Minor items from architecture review, also addressed:**
- Exception file placement under `Infrastructure/Exceptions/` (done, see above).
- `TimeProvider` threaded into `ManufactureErpResilienceService` (constructor param,
  set via `ResiliencePipelineBuilder.TimeProvider`) so the half-open/recovery test
  can use `FakeTimeProvider` instead of a real 30s wait.

### FR-4 — Conditional design note: not applicable, recorded

Per the plan's gating condition and `docs/integrations/flexibee-api.md` §3: the
confirm endpoints' own p95 (1.7–2.4s) is well under the blended dependency p95/p99
(6.7s/23.4s), so FR-4 is marked not-applicable (no design note, no follow-up issue
filed). The doc explicitly flags this as a provisional call pending the FR-1 query
actually being run, and recommends batching/caching/rate-limiting the background
sync clients as the next follow-up once that evidence exists — per the plan's own
fallback instruction, not silently dropped.

## Files created

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureErpResilienceService.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/Exceptions/ManufactureErpUnavailableException.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ErpCircuitOpenFilter.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureErpResilienceServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ErpCircuitOpenFilterTests.cs`
- `docs/integrations/flexibee-api.md`

## Files changed

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs` (added
  `IManufactureErpResilienceService` mock, wired as a pass-through so existing handler
  behavior tests are unaffected by circuit-breaker state/timing)

## Tests added

`ManufactureErpResilienceServiceTests` (9 tests):
- successful operation returns result
- `CancelAfter`-shaped timeout counted as failure, opens circuit after threshold,
  underlying operation not invoked once open
- circuit-open logs a Warning (`OnOpened`) — observability requirement
- genuine caller cancellation is *not* counted, propagates unchanged, circuit stays
  closed
- after `BreakDurationSeconds` (advanced via `FakeTimeProvider`), a successful
  half-open probe closes the circuit again and a subsequent failure is passed
  through fresh (proving the operation is actually re-invoked, not still fast-failed)
- non-`ShouldHandle` exceptions (e.g. `InvalidOperationException`) propagate
  immediately and never count toward the breaker

`ErpCircuitOpenFilterTests` (3 tests): `CanHandle`/`Transform` behavior.

`SubmitManufactureHandlerTests`: unchanged assertions, updated only to inject a
pass-through resilience-service mock (6 constructor call sites); all previously
passing tests — including `Handle_WhenErpTimesOut_PropagatesOperationCanceledException`,
which is a single-failure case that stays below `MinimumThroughput=2` and so still
propagates the raw `OperationCanceledException` exactly as before — continue to pass
unchanged.

## Verification performed

- `dotnet build Anela.Heblo.sln` — succeeds (only pre-existing, unrelated nullable
  warnings).
- `dotnet format Anela.Heblo.sln --include <all touched/new files>` — no changes
  needed (already compliant).
- `dotnet test --filter "FullyQualifiedName~ManufactureErpResilienceService|FullyQualifiedName~SubmitManufactureHandlerTests|FullyQualifiedName~ErpCircuitOpenFilter"`
  — **23/23 passed.**
- `dotnet test --filter "Category!=Integration"` (broader regression pass, excluding
  the Postgres/Testcontainers-backed integration suite that this sandbox can't run —
  no Docker/Podman socket available here): ran ~100+ tests before being stopped for
  time; the only two failures observed were **pre-existing, unrelated, timing-based
  flaky tests** — `CatalogMergeSchedulerTests.ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`
  (asserts against a 1ms/50ms debounce window) and
  `DbResiliencePipelineProviderTests.Pipeline_AbortsByTotalTimeBudget` (asserts a
  wall-clock budget) — both in files untouched by this change, both plausibly
  explained by this sandbox's CPU being slower/more contended than CI. A full,
  uninterrupted solution-wide run was not completed in this environment; this is a
  known sandbox limitation (no container runtime, apparent CPU contention), not a
  gap introduced by this change. The scoped, deterministic validation above (build +
  format + every test touched by this change) is clean.

## How to verify

```bash
cd backend
dotnet build ../Anela.Heblo.sln
dotnet format ../Anela.Heblo.sln --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureErpResilienceService|FullyQualifiedName~SubmitManufactureHandlerTests|FullyQualifiedName~ErpCircuitOpenFilter"
```

Once `APPINSIGHTS_APP_ID`/`APPINSIGHTS_API_KEY` are available, close the FR-1 gap by
running the query in `docs/integrations/flexibee-api.md` §2.3 and updating that
section (and re-checking the FR-4 not-applicable decision against real numbers).
