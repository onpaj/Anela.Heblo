# Review: inject-timeprovider-and-test (r1)

## Review Result: PASS

### task: inject-timeprovider-and-test
**Status:** PASS

## Spec compliance

**FR-1 — Inject TimeProvider (PASS).** `TimeProvider timeProvider` is appended as
the last constructor parameter and assigned to `private readonly TimeProvider
_timeProvider`. The existing four parameters keep their order and their
assignments are untouched. No DI registration was added or changed — verified
that `services.AddSingleton(TimeProvider.System)` already exists at
`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:135`, so
the new dependency resolves at the composition root.

**FR-2 — Replace DateTime.UtcNow (PASS).** Both fallbacks now derive from a
single `var now = _timeProvider.GetUtcNow().UtcDateTime;`. This is strictly
better than two separate provider calls: the previous code could in principle
read the clock twice across a tick boundary, so `toDate` and `fromDate` are now
guaranteed consistent. `grep -n "DateTime.UtcNow\|DateTime.Now"` on the handler
returns nothing (exit 1). The diff confirms nothing else in `Handle` changed —
validation, filtering, search, sorting, pagination, and `CalculateSummary` are
byte-identical.

**FR-3 — Test coverage (PASS).** The shared construction site in
`GetPurchaseStockAnalysisHandlerTests` now passes a `Mock<TimeProvider>` with
`Setup(x => x.GetUtcNow()).Returns(FixedNow)`, `FixedNow` being a fixed
`DateTimeOffset(2024, 8, 2, 14, 30, 22, TimeSpan.Zero)` — the exact pattern
`CreatePurchaseOrderHandlerTests` uses (same type, same setup call, same field
naming). The new
`Handle_NullDates_DefaultsToOneYearWindowFromTimeProvider` sends a request with
both dates null and asserts `Summary.AnalysisPeriodStart ==
FixedNow.UtcDateTime.AddYears(-1)` and `Summary.AnalysisPeriodEnd ==
FixedNow.UtcDateTime`. No assertion touches real wall-clock time, so it cannot
flake — including at a year or DST boundary. Existing tests were changed only at
the construction call site; no test body was edited.

**NFR-1 / NFR-2 (PASS).** Trivial call substitution; no I/O, auth, or data
sensitivity change.

## Architecture adherence

Follows the module's established pattern rather than inventing one: the handler
mirrors `CreatePurchaseOrderHandler`, and the tests mirror
`CreatePurchaseOrderHandlerTests`. No DTO was touched, so the project's
"DTOs are classes, never records" rule is not implicated. Nothing outside the
Purchase use-case folder and its two test classes was modified — the out-of-scope
list in the spec is respected.

## Completeness

All ten plan steps are accounted for, with build, format, and test evidence
recorded in the implementation artifact. One item in the plan was wrong and the
developer corrected it rather than following it blindly — see below.

## Correctness

- **The plan's claim that only one construction site exists was false.**
  `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs:25` also constructs the
  handler. Had it been left alone the solution would not compile, so updating it
  was required, not optional scope creep. The developer used the same
  `Mock<TimeProvider>` + `FixedNow` wiring rather than `TimeProvider.System`.
  That is the right call: those theory cases send a request with no
  `FromDate`/`ToDate`, so they exercise the fallback path too, and a real clock
  there would have re-introduced exactly the nondeterminism this issue exists to
  remove. Accepted as a justified, minimal deviation.
- No nullability or argument-validation regression: the handler never
  null-checked its other injected dependencies either, so adding a guard only for
  `timeProvider` would have been inconsistent with the surrounding code.
- `GetUtcNow().UtcDateTime` (not `.DateTime`) is correct — it yields a
  `DateTimeKind.Utc` value, matching the semantics of the `DateTime.UtcNow` it
  replaces.

## Verification evidence

- `dotnet build Anela.Heblo.sln` — `0 Error(s)`; 259 warnings, all pre-existing,
  none referencing the touched files.
- `dotnet format Anela.Heblo.sln --verify-no-changes` — exit 0, no violations.
- Filtered run — `Failed: 0, Passed: 27, Total: 27`.
- New test in isolation — `Failed: 0, Passed: 1`.
- Full test project — `Failed: 105, Passed: 6859, Skipped: 4, Total: 6968`. Spot-
  checked: every failure is `System.ArgumentException : Docker is either not
  running or misconfigured` from `PostgresSharedContainerFixture..ctor()`. No
  failure references `StockAnalysis`; the lone `Features.Purchase` failure
  (`PurchaseOrderRepositoryHistorySqlShapeTests`) has the same Docker cause.
  Sandbox limitation, not a regression from this change.

## Documentation check

No documentation update needed. There is no public behaviour change, no new
concept, no new environment variable, CLI command, or operational step — the
handler is resolved exclusively through DI and its request/response contracts are
untouched. `README.md` and `CLAUDE.md` are unaffected.

**Status:** PASS
