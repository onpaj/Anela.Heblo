# Architecture review — FlexiBee circuit breaker design (design-01.md)

## Verdict

FR-1/FR-2/FR-4 (investigation, doc, conditional design note) are sound and
need no changes — they don't touch production code. **FR-3's resilience
pipeline has two concrete defects that would make the circuit breaker a
no-op for the exact failure mode it exists to guard against.** Both were
found by reading the precedent it claims to mirror
(`CatalogResilienceService`) and its tests line-by-line, not by inspection of
the design doc alone. Fix both before implementation starts; everything else
in the design (placement, DI, error-filter wiring, downstream propagation)
checks out against the actual code.

## What was verified and holds up

- `CatalogResilienceService` (`Features/Catalog/Infrastructure/CatalogResilienceService.cs`)
  exists exactly as described, is registered `AddSingleton` in
  `CatalogModule.cs:100`, and its `AddCircuitBreaker` options
  (`FailureRatio=0.5`, `MinimumThroughput=3`, `SamplingDuration=1min`,
  `BreakDuration=30s`) match what the design copies.
- `IManufactureErrorFilter` + `ManufactureErrorTransformer` + `services.Scan(...)`
  in `ManufactureModule.cs:75-80` is real and auto-discovers any new
  `IManufactureErrorFilter` dropped into `ErrorFilters/Filters/` — no DI
  change needed for `ErpCircuitOpenFilter`, as claimed.
- `SubmitManufactureHandler`'s catch clause
  (`catch (Exception ex) when (ex is not OperationCanceledException)`,
  `SubmitManufactureHandler.cs:70`) does catch a plain `Exception` subtype
  like the proposed `ManufactureErpUnavailableException` and routes it
  through `_errorTransformer.Transform(ex)` into `SubmitManufactureResponse.UserMessage` — confirmed by reading the handler, not assumed.
- `ConfirmProductCompletionWorkflow.TransitionToCompletedAsync`
  (`ConfirmProductCompletionWorkflow.cs:249,278`) reads
  `submitResult.Success`/`UserMessage` and sets `ManualActionRequired`
  exactly as the design states — no workflow changes needed.
- `ManufactureErpOptions.cs` is a plain bound-options class
  (`services.Configure<ManufactureErpOptions>` in `ManufactureModule.cs:31`)
  — the four new circuit-breaker keys are additive and backward compatible
  as claimed.
- Filesystem placement (`Features/Manufacture/Infrastructure/…`) matches
  `docs/architecture/filesystem.md`'s `Infrastructure/` convention.
- Polly `8.4.1` is already referenced in
  `Anela.Heblo.Application.csproj` — no new package needed, as claimed.

## Finding 1 (blocking) — the cancellation predicate won't recognize the actual ERP timeout

The design's `ShouldHandle` predicate is copied verbatim from
`CatalogResilienceService`:

```csharp
.Handle<OperationCanceledException>(ex => ex.CancellationToken.IsCancellationRequested == false)
```

This looks backwards until you check *why* it works for Catalog:
`CatalogResilienceServiceTests.cs:72-93` constructs the timeout case as
`new TaskCanceledException("Operation timed out")` — **no token passed**, so
`ex.CancellationToken` defaults to `CancellationToken.None`
(`IsCancellationRequested == false`) — matched, retried. The pre-cancelled
case (`ExecuteWithResilienceAsync_OperationCancelled_DoesNotRetry`,
line 157) constructs `new OperationCanceledException(cts.Token)` with
`cts.Cancel()` already called — `IsCancellationRequested == true` — **not**
matched, rethrown immediately. In production, Catalog's real timeout is
Polly's *own* `.AddTimeout(30s)` (line 106 of `CatalogResilienceService.cs`),
whose `TimeoutRejectedException` (an `OperationCanceledException` subtype)
is synthesized by Polly without an attached real token — so it also lands
in the "`IsCancellationRequested == false`" bucket. The predicate isn't
distinguishing "timeout vs. cancellation" by meaning; it's distinguishing
"exception carries no real token" (Polly's own timeout) vs. "exception
carries the actual token that was cancelled" (a real caller cancellation).

The design explicitly **omits** `.AddTimeout()` — "no retry, no inner
timeout... the existing `ErpTimeoutSeconds` `CancelAfter` ... remains the
per-call timeout; the breaker sits around it." That's a reasonable choice on
its own (avoid stacking a second timeout on an already-slow dependency), but
it changes which exception shape actually occurs on timeout:
`SubmitManufactureHandler.CreateLinkedCts` (`SubmitManufactureHandler.cs:80-86`)
builds a **linked** `CancellationTokenSource` and calls `cts.CancelAfter(...)`.
When that timer fires mid-call, the resulting `TaskCanceledException`'s
`.CancellationToken` is the linked `cts.Token` itself — which, at the point
it's observed, reports `IsCancellationRequested == true` (it's the token that
was actually cancelled). Under the copied predicate, that means
**`IsCancellationRequested == false` is false → not handled → the circuit
breaker never sees the timeout as a failure, and never opens because of it.**
The one failure mode this dependency actually exhibits at p95/p99 (multi-second
to 60s hangs) is silently invisible to the breaker.

It also means the exception is not converted to `BrokenCircuitException` (that
requires an open circuit that never opens) nor caught by
`ManufactureErpResilienceService`'s generic `catch (Exception ex) { ...; throw; }`
— it propagates as a raw `TaskCanceledException`, which then hits
`SubmitManufactureHandler`'s own `when (ex is not OperationCanceledException)`
guard and is **excluded there too** — so it propagates all the way out of the
MediatR pipeline unhandled, exactly as it does today. FR-3 as designed changes
nothing about the dominant failure mode.

**Fix before implementation:** decide the predicate based on what actually
distinguishes "our own `CancelAfter` fired" from "the caller's own token was
cancelled" — e.g. check the *original* `cancellationToken` parameter passed
into `ExecuteAsync` (not `ex.CancellationToken`, and not the linked token):
if the original caller token isn't the one that's cancelled, treat any
`OperationCanceledException`/`TaskCanceledException` as a handleable failure.
This can't be copy-pasted from Catalog because Catalog's timeout mechanism
(Polly's own `.AddTimeout`) and this design's timeout mechanism (app-level
linked `CancelAfter`) produce differently-shaped exceptions for the "false"
vs. "true" branch. Add a unit test that reproduces the actual
`CreateLinkedCts` + `CancelAfter` shape (not a bare `new TaskCanceledException(...)`)
to prove the breaker counts it as a failure — the existing
`CatalogResilienceServiceTests` pattern does not cover this and would give a
false sense of parity if copied as-is.

## Finding 2 (blocking) — `MinimumThroughput=3` / `SamplingDuration=60s` will rarely if ever trip at this call site's volume

`plan-01.md`'s own volume math: the three confirm operations combined are
~405 calls/week (232+146+27) — **roughly 2.4 calls/hour**, spread across
three different endpoints, each independently gated by `_erpOptions`
per-call. `CatalogResilienceService`'s defaults
(`MinimumThroughput=3` within a rolling `SamplingDuration=60s`) were tuned
for Catalog's dependency, whose call site (`CatalogDataRefreshService`,
cache-refresh background sync) runs far more frequently than once every ~25
minutes. Polly's `MinimumThroughput` requires that many qualifying calls to
land **inside the same rolling sampling window** before the failure ratio is
even evaluated. At ~2.4 combined confirm calls/hour, the probability that 3
land within any 60-second window during normal operation is close to zero —
meaning even a 100%-failing FlexiBee would need 3 confirm attempts to
happen to fall within the same minute to ever open the breaker. In practice
the breaker built with copied defaults will almost never open for this
specific call site, silently defeating FR-3's purpose for the very
endpoints the issue calls out as user-facing.

**Fix before implementation:** don't copy Catalog's numbers by default.
Either widen `ErpCircuitBreakerSamplingDurationSeconds` to something that
plausibly captures 3 calls at this endpoint's real traffic (e.g. 10–15
minutes, matching the ~2.4 calls/hour rate), or lower
`ErpCircuitBreakerMinimumThroughput` to 2, or both — and say so explicitly in
the design rather than presenting "same as Catalog" as validation. This is a
config-value decision, not a structural one, but it must be made
deliberately (ideally cross-checked against the FR-1 per-resource
attribution numbers once available) rather than inherited from a
differently-shaped dependency.

## Minor items (non-blocking, worth doing while in the file anyway)

- **Exception file placement.** The codebase already has a convention for
  this: `Features/Catalog/Infrastructure/Exceptions/ProductMarginsException.cs`
  and (naming precedent) `Adapters/Anela.Heblo.Adapters.Comgate/PaymentGatewayUnavailableException.cs`.
  Put the new `ManufactureErpUnavailableException` at
  `Features/Manufacture/Infrastructure/Exceptions/ManufactureErpUnavailableException.cs`
  rather than loose in `Infrastructure/`, matching Catalog's existing shape.
- **Testability of the half-open/recovery scenario.** The plan's FR-3
  acceptance criteria require a test that the breaker "closes again after
  recovery (half-open probe)". `CatalogResilienceServiceTests.cs` has no such
  test today — there's no in-repo precedent for testing this without a real
  30s wait. Polly 8.4.1's `ResiliencePipelineBuilder` exposes a `TimeProvider`
  property; thread the same `TimeProvider` the handler already receives
  (`SubmitManufactureHandler._timeProvider`, itself already DI-injected) into
  `ManufactureErpResilienceService`'s pipeline builder so tests can use
  `FakeTimeProvider` (already available via `Microsoft.Extensions.TimeProvider.Testing`
  if referenced, or a hand-rolled fake) to advance past `BreakDurationSeconds`
  deterministically. Without this, that acceptance criterion either gets
  quietly dropped or implemented with a real `Task.Delay`, which is slow and
  flaky.
- **Coarse-grained wrapping.** `IManufactureClient.SubmitManufactureAsync`
  (`FlexiManufactureClient.cs:54`) internally issues multiple sequential
  FlexiBee HTTP calls (consumption + production, per product) under one
  shared `ErpTimeoutSeconds` budget. Wrapping the whole composite call as one
  Polly "attempt" is the right level (matches the existing timeout's scope)
  but means one throughput unit can represent several HTTP round-trips —
  worth a one-line note in the doc/PR so a future reader isn't surprised the
  breaker's throughput count doesn't match FlexiBee's own per-request count
  from the telemetry query in FR-1.

## Prerequisites before implementation

1. Resolve Finding 1: write (and pass) a unit test using the actual
   `CreateLinkedCts`/`CancelAfter` shape, not a bare `TaskCanceledException`,
   proving the breaker counts a `CancelAfter`-triggered timeout as a failure.
2. Resolve Finding 2: pick `MinimumThroughput`/`SamplingDurationSeconds`
   defaults deliberately against this call site's actual traffic (use FR-1's
   attribution numbers if they refine the ~2.4/hour estimate), not copied
   from `CatalogResilienceService`.
3. Decide the exception file path (minor item 1) and the `TimeProvider`
   threading for testability (minor item 2) before writing the breaker
   implementation, so the TDD loop in the plan's rough-plan step 3 doesn't
   have to backtrack.

No other structural concerns — DI, error-filter wiring, workflow
propagation, config schema, and file placement all check out against the
current codebase.
