# Plan: Inject TimeProvider into TimePeriodResolver

## Summary

`TimePeriodResolver` (`backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs`) reads `DateTime.UtcNow` directly instead of taking time from the injected `TimeProvider`, violating the project's UTC-time-handling convention (`docs/architecture/Dev_Guidelines_time.md`). This is a small, mechanical fix mirroring the already-accepted pattern in `TimeWindowParser` (`Features/Analytics/Services/TimeWindowParser.cs`): inject `TimeProvider`, replace the ambient clock read, and update the one test that constructs the class directly so date-range assertions can be pinned exactly instead of only loosely ordered.

## Context

Three prior fixes in this same time-handling area (`TimeWindowParser`, `GetInvoiceImportStatisticsHandler`, `GetBankStatementImportStatisticsHandler`) already replaced ambient-clock reads with injected `TimeProvider`. `TimePeriodResolver` is the last outlier. It feeds two production Manufacture use cases (`CalculateBatchPlanHandler`, `GetManufacturingStockAnalysisHandler`) whose windows (previous/future quarter, Y2Y, previous season, Q9M) currently cannot be pinned or boundary-tested because the resolver's `now` is not controllable. `TimeProvider` is already registered as a singleton in DI (`Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`), so no new registration is needed beyond passing it into the constructor.

## Functional requirements

**FR-1: `TimePeriodResolver` takes `TimeProvider` via constructor injection.**
- Add a `private readonly TimeProvider _timeProvider` field and a constructor parameter, matching `TimeWindowParser`'s shape.
- Acceptance: class no longer has a parameterless constructor; `TimeProvider` is the sole constructor dependency.

**FR-2: `Resolve` derives `now` from the injected provider, not `DateTime.UtcNow`.**
- Replace `var now = DateTime.UtcNow;` with `var now = _timeProvider.GetUtcNow().DateTime;` (per the `Dev_Guidelines_time.md` "business logic uses UTC" rule — `GetUtcNow()`, not `GetLocalNow()`, unlike `TimeWindowParser` which is local-time by design for its own reasons).
- Acceptance: no reference to `DateTime.UtcNow`/`DateTime.Now` remains in the file; all five private `ResolveForX(DateTime now)` helpers are unchanged (they already take `now` as a parameter).

**FR-3: DI registration continues to resolve correctly.**
- `ApplicationModule.cs:70` (`services.AddScoped<ITimePeriodResolver, TimePeriodResolver>();`) requires no change — `TimeProvider` is already a singleton in the container, so scoped-resolver-depends-on-singleton-provider is valid and DI will inject it automatically.
- Acceptance: `dotnet build` succeeds; app boots without a missing-service resolution error (covered by existing integration/DI tests if any exist, otherwise by manual build verification).

**FR-4: Existing direct-construction test is updated to inject a controllable clock and gains exact-value assertions.**
- `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs:8` currently does `new TimePeriodResolver()`. Change to construct with a `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (the convention already used elsewhere in this test project, e.g. `GetProductMarginSummaryHandlerTests.cs`) set to a fixed instant.
- Once the clock is fixed, tighten at least the loose `result[0].From.Should().BeBefore(DateTime.UtcNow)`-style assertions to exact `DateRange` values for each `TimePeriod` case, derived by hand from the fixed date (mirroring the existing `Resolve_CustomPeriod_WithBothDates_ReturnsOneRange` test's exact-value style).
- Acceptance: all 9 existing test cases pass against the fixed clock; at least the "previous quarter / future quarter / Y2Y / previous season / Q9M" cases assert concrete `From`/`To` values (not just ordering relative to `DateTime.UtcNow`), including one case exercising a month/quarter/season rollover boundary (e.g. fixed date on the 1st of a month, or year-end for `PreviousSeason`).

**FR-5: No other callers break.**
- `CalculateBatchPlanHandlerTests.cs` and `GetManufacturingStockAnalysisHandlerTests.cs` mock `ITimePeriodResolver` directly and don't construct `TimePeriodResolver`, so they're unaffected — verify this remains true after the change (no direct `new TimePeriodResolver()` elsewhere).
- Acceptance: `grep -rn "new TimePeriodResolver(" backend/` shows only the updated test file.

## Non-functional requirements

- No behavior change for production call sites — the resolved windows must be identical for a given wall-clock instant before and after the change (this is a refactor for testability, not a logic change).
- No new packages: `Microsoft.Extensions.TimeProvider.Testing` (source of `FakeTimeProvider`) is already a test-project dependency per other test files using it.

## Data model

No entities affected. `DateRange` (existing type) and `TimePeriod` (existing enum) are unchanged.

## Interfaces

No public API/contract surface change — `ITimePeriodResolver.Resolve(...)` signature is unchanged. Only the concrete `TimePeriodResolver` constructor changes, which is an internal DI concern.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs` (constructor + `now` source)
- `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs` (construction + tightened assertions)

**Out of scope:**
- `ITimePeriodResolver` interface — unchanged.
- `ApplicationModule.cs` DI registration line — unchanged (already compatible).
- `CalculateBatchPlanHandler` / `GetManufacturingStockAnalysisHandler` and their tests — unaffected, no changes needed.
- Any other `DateTime.UtcNow`/`DateTime.Now` usages elsewhere in the codebase — out of scope for this task (this is a single-class, single-issue fix per the harness issue).

## Rough plan

1. Add `TimeProvider` field + constructor to `TimePeriodResolver`; replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime`.
2. Update `TimePeriodResolverTests.cs`: construct `TimePeriodResolver` with a `FakeTimeProvider` set to a fixed `DateTimeOffset`; replace loose `BeBefore(DateTime.UtcNow)` assertions with exact expected `DateRange` values computed by hand for that fixed date, including a boundary case (e.g. pin the fake clock to the 1st of a month or a year boundary to exercise the quarter/season rollover math).
3. Confirm no other direct instantiation of `TimePeriodResolver` exists (`grep -rn "new TimePeriodResolver("`).
4. Run `dotnet build` and `dotnet format` on the backend; run the full `Anela.Heblo.Tests` suite (at minimum the `Common/TimePeriods` and `Features/Manufacture` test groups) to confirm green.

## Open questions

- None blocking. One judgment call: the task's suggested direction says mirror `TimeWindowParser`, which uses `GetLocalNow()` — but `Dev_Guidelines_time.md` explicitly labels `GetLocalNow()` in business logic as "❌ WRONG" and shows `GetUtcNow().DateTime` as the "✅ CORRECT" pattern. Since `TimePeriodResolver` previously read `DateTime.UtcNow` (not `DateTime.Now`), preserving UTC semantics via `GetUtcNow().DateTime` is the correct choice and matches the doc's explicit guidance over `TimeWindowParser`'s (locally-scoped) precedent. This is noted here rather than left ambiguous for the next step.
