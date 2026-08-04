# Review — FlexiBee ERP circuit breaker implementation (development-01.md / commit c64c6eca)

## Verdict: done

## What was checked

Read the full chain (`plan-01.md` → `design-01.md` → `architecture-01.md` →
`development-01.md`) and the actual diff (`git show c64c6eca`), then independently
verified the claims rather than trusting the development log:

- **`dotnet build Anela.Heblo.sln`** — 0 errors, 250 pre-existing warnings (none in
  touched files, none new).
- **`dotnet format Anela.Heblo.sln --verify-no-changes --include <all 9 touched/new
  files>`** — exit 0, no changes needed.
- **`dotnet test --filter "...ManufactureErpResilienceService|...SubmitManufactureHandlerTests|...ErpCircuitOpenFilter"`**
  — 23/23 passed, matching the count claimed in `development-01.md`.

## Conformance to plan/architecture

- **Both blocking findings from `architecture-01.md` are actually fixed, not just
  claimed fixed.** Read `ManufactureErpResilienceService.cs` and
  `SubmitManufactureHandler.cs` line-by-line:
  - Finding 1 (cancellation predicate): `SubmitManufactureHandler` now passes the
    *original* `cancellationToken` (not the linked/`CancelAfter`-armed `cts.Token`)
    as `ExecuteAsync`'s context token, while the operation lambda still closes over
    `cts.Token` for the actual HTTP call. `ShouldHandle` checks
    `args.Context.CancellationToken.IsCancellationRequested` (bound from the
    original token), so a `CancelAfter` timeout is counted as a failure and a
    genuine caller cancellation is not. `ManufactureErpResilienceServiceTests`
    reproduces the exact `CreateLinkedCts`/`CancelAfter` shape (not a bare
    `TaskCanceledException`) and asserts both directions — this is the test
    architecture review explicitly required and it exists.
  - Finding 2 (throughput/sampling defaults): `ErpCircuitBreakerMinimumThroughput`
    defaults to 2 (not Catalog's 3) and `ErpCircuitBreakerSamplingDurationSeconds`
    to 900s (not Catalog's 60s), with the reasoning against this call site's
    ~2.4 calls/hour documented inline in `ManufactureErpOptions`.
  - Minor items (exception file placement under `Infrastructure/Exceptions/`,
    `TimeProvider` threaded for `FakeTimeProvider`-based half-open testing) both
    done.
- **DI wiring matches design**: `ErpCircuitOpenFilter` implements
  `IManufactureErrorFilter` and is picked up by the existing
  `services.Scan(...).FromAssemblyOf<IManufactureErrorFilter>()` in
  `ManufactureModule.cs` — confirmed no manual registration was needed or added.
  `IManufactureErpResilienceService` is registered `AddSingleton`, consistent with
  the `CatalogResilienceService` precedent and the stated reason (breaker state
  must persist across requests).
- **FR-4 gating**: correctly marked not-applicable per the plan's own conditional
  language, with the reasoning (confirm endpoints' own p95 well under blended
  dependency p95/p99) recorded in `flexibee-api.md` §3, and a fallback
  recommendation given rather than silently dropped.
- **FR-1's KQL query genuinely could not be run** (`APPINSIGHTS_APP_ID` unset in
  this sandbox) — this is an environment limitation, not a shortcut, and it's
  flagged honestly in `flexibee-api.md` §2.3 as an open gap with the exact command
  to close it, rather than fabricated or silently dropped. The confirm-vs-background
  split conclusion is derived from call-volume math instead, and that's stated
  explicitly as weaker evidence.
- **Existing `SubmitManufactureHandlerTests` behavior preserved**: the new
  `IManufactureErpResilienceService` mock is wired as a pass-through
  (`(operation, _, ct) => operation(ct)`) across all 6 constructor call sites, so
  the handler's own pre-existing assertions (including the
  `ErpTimeoutSeconds`-based timeout propagation test) are exercised unchanged while
  the resilience service itself is tested in isolation with real circuit-breaker
  timing/state via `ManufactureErpResilienceServiceTests`.

## Correctness

No logic errors found. The one subtlety in this design — a Polly `ResilienceContext`
token intentionally different from the token used for the actual HTTP call — is
non-obvious but is exactly what the architecture review specified, is documented in
a code comment explaining *why*, and is covered by a test that reproduces the real
shape rather than a simplified stand-in.

## Non-blocking observations (do not require another round)

- `flexibee-api.md`'s FR-1 gap means the confirm-vs-background attribution (and by
  extension the FR-4 not-applicable call) rests on inference rather than the
  planned direct measurement. This is explicitly flagged as provisional in the doc
  itself, with the exact follow-up command recorded — acceptable given it's an
  external credential/environment gap, not something the implementation step could
  have fixed.
- The full solution-wide test suite wasn't run to completion in this environment
  (no container runtime for Testcontainers-backed integration tests); the two
  flaky tests noted in `development-01.md` are in unrelated files
  (`CatalogMergeSchedulerTests`, `DbResiliencePipelineProviderTests`) and plausibly
  timing-related to sandbox contention, not to this change.

```json
{"outcome": "done", "summary": "Circuit breaker implementation verified against plan/design/architecture chain: both architecture-01.md blocking findings (cancellation predicate, throughput/sampling defaults) are genuinely fixed with tests reproducing the real failure shape, DI/error-filter wiring matches design, FR-4 gating and the FR-1 environment gap are honestly recorded. Independently re-ran build (0 errors), format (--verify-no-changes, exit 0), and the 23 touched tests (23/23 passed) — all claims in development-01.md hold up."}
```
