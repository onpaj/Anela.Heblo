# Design: Inject TimeProvider into TimePeriodResolver

No UI surface — this is a backend-only, internal refactor of an application-layer service. UX/UI section omitted.

## Component design

### `TimePeriodResolver` (`backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs`)

Responsibility is unchanged: given a `TimePeriod` enum value (and optional custom bounds), compute the corresponding `IReadOnlyList<DateRange>`. The only change is where "now" comes from.

**Before:**
```csharp
public class TimePeriodResolver : ITimePeriodResolver
{
    public IReadOnlyList<DateRange> Resolve(TimePeriod period, DateTime? customFrom = null, DateTime? customTo = null)
    {
        var now = DateTime.UtcNow;
        ...
```

**After:**
```csharp
public class TimePeriodResolver : ITimePeriodResolver
{
    private readonly TimeProvider _timeProvider;

    public TimePeriodResolver(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<DateRange> Resolve(TimePeriod period, DateTime? customFrom = null, DateTime? customTo = null)
    {
        var now = _timeProvider.GetUtcNow().DateTime;
        ...
```

This mirrors `TimeWindowParser`'s shape (`Features/Analytics/Services/TimeWindowParser.cs:8-15`) exactly, with one deliberate deviation: `TimeWindowParser.ParseTimeWindow` calls `_timeProvider.GetLocalNow().Date` (local time, by that class's own design), whereas `TimePeriodResolver` previously read `DateTime.UtcNow`. To preserve existing production behavior (no logic change, per the plan's non-functional requirement), the replacement is `_timeProvider.GetUtcNow().DateTime` — UTC in, UTC out, matching `Dev_Guidelines_time.md`'s "✅ CORRECT" example verbatim. The five private `ResolveForX(DateTime now)` helpers, the `TimePeriod` enum, `DateRange`, and `ITimePeriodResolver` are all untouched — this is a single-seam change (constructor + one line in `Resolve`).

### DI wiring

No change required. `ApplicationModule.cs:70` already registers:
```csharp
services.AddScoped<ITimePeriodResolver, TimePeriodResolver>();
```
and `ServiceCollectionExtensions.cs:129-130` already registers `TimeProvider.System` as a singleton:
```csharp
services.AddSingleton(TimeProvider.System);
```
Confirmed by reading both files directly — a scoped service depending on a singleton is valid, and the container will inject `TimeProvider.System` into the new constructor parameter automatically. No registration edit is part of this change.

### Consumers

`CalculateBatchPlanHandler` and `GetManufacturingStockAnalysisHandler` depend on `ITimePeriodResolver` (the interface, unchanged) and are resolved through DI in production, so they are unaffected. Their tests mock `ITimePeriodResolver` directly rather than constructing `TimePeriodResolver`, confirmed by the plan step's grep — so no test changes needed there. This is re-verified as an acceptance step (FR-5), not re-derived here.

## Data schema / contract impact

None. `ITimePeriodResolver.Resolve(...)` signature, `DateRange`, and `TimePeriod` are unchanged — this is a pure internal constructor/implementation change, invisible to every caller of the interface.

## Test design

`backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs` currently does `new TimePeriodResolver()` (line 8) and asserts only loose ordering against the live `DateTime.UtcNow`. This becomes non-compilable once the parameterless constructor is removed, so the test file must change in lockstep with the source change (not a separate follow-up).

**Construction**, mirroring the established project convention (`GetProductMarginSummaryHandlerTests.cs:36`, `UpcomingProductionTileTests.cs:27`, `ProductionActivityAnalyzerTests.cs:22`) — `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, no new package needed (already a test-project dependency):

```csharp
using Microsoft.Extensions.Time.Testing;
...
private static readonly DateTimeOffset FrozenNowUtc = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
private readonly TimePeriodResolver _sut = new(new FakeTimeProvider(FrozenNowUtc));
```

Picking **Feb 1, midnight** as the frozen instant is deliberate on two counts:
1. The 1st-of-month exercises the `ResolveForPreviousQuarter`/`ResolveForY2Y`/`ResolveForFutureQuarter` month-boundary arithmetic at its edge (where `startOfCurrentMonth` == `now.Date`), satisfying the plan's requirement for a boundary case.
2. Midnight matters specifically for `ResolveForQ9M`: unlike the other four `ResolveForX` helpers, it never truncates `now` to a `new DateTime(y, m, 1)` boundary — `rangeATo` is `now` itself. A non-midnight frozen time (e.g. noon, used elsewhere in this test project for handler tests) would leak a time-of-day component into `rangeATo`/`rangeBTo`, making the expected values noisy. Freezing at midnight keeps every expected `DateRange` a clean date with no time component, for all five period types.

**Per-case exact assertions**, replacing each `BeBefore(DateTime.UtcNow)` with a computed expected `DateRange` for `FrozenNowUtc = 2026-02-01T00:00:00Z`:

| `TimePeriod` | Expected range(s) (hand-derived from source logic in `TimePeriodResolver.cs:23-59`) |
|---|---|
| `PreviousQuarter` | `[2025-11-01, 2026-01-31]` (`startOfCurrentMonth=2026-02-01`, `-3mo=2025-11-01`, `endOfPreviousMonth=2026-01-31`) |
| `FutureQuarter` | `[2025-02-01, 2025-04-30]` (`now.Year-1, now.Month,1 = 2025-02-01`, `+3mo-1d = 2025-04-30`) |
| `Y2Y` | `[2025-02-01, 2026-01-31]` (`-12mo` from 2026-02-01, to day before current-month start) |
| `PreviousSeason` | `[2025-10-01, 2026-01-31]` (`now.Year-1,10,1` to `now.Year,1,31`) |
| `Q9M` | Range A `[2025-08-01, 2026-02-01]` (`now-6mo` to `now`), Range B `[2025-02-01, 2025-05-01]` (`now-1y` to `now-1y+3mo`) — both exact only because `now` itself is midnight |

Each existing test method (`Resolve_PreviousQuarter_ReturnsOneRange`, etc.) keeps its name and `Should().HaveCount(n)` assertion, but replaces the `BeBefore(DateTime.UtcNow)` lines with `result[0].From.Should().Be(new DateTime(2025,11,1))` / `.To.Should().Be(new DateTime(2026,1,31))` style exact checks (and both ranges for `Q9M`). The `CustomPeriod` tests (already exact-value style, don't depend on `now`) are unchanged.

No new test files, no new test classes — this is an in-place edit of the nine existing `[Fact]` methods plus the one constructor line.

## Verification

1. `grep -rn "new TimePeriodResolver(" backend/` → only the updated test file's constructor call.
2. `dotnet build` on the backend solution — confirms the interface/constructor change compiles everywhere `TimePeriodResolver` is referenced (DI registration, the test file).
3. `dotnet format` — style conformance.
4. `dotnet test` scoped to `Anela.Heblo.Tests` — all 9 `TimePeriodResolverTests` cases pass with exact assertions; `CalculateBatchPlanHandlerTests` / `GetManufacturingStockAnalysisHandlerTests` pass unchanged (they mock the interface, not the class).
