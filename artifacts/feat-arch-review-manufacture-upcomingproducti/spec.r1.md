# Specification: TimeProvider Consistency in UpcomingProductionTile

## Summary
The `UpcomingProductionTile` base class hard-codes `DateTime.Today` and `DateTime.UtcNow` in `GenerateDrillDownFilters()` and `LoadDataAsync()`, even though concrete subclasses (`TodayProductionTile`, `NextDayProductionTile`) already accept `TimeProvider` to set `ReferenceDate`. This split between `TimeProvider`-controlled and wall-clock time makes drill-down behavior untestable under time-shifted scenarios. The fix is to inject `TimeProvider` into the base class and replace all direct clock calls with provider-backed equivalents.

## Background
Issues #2676 and #2677 already document the same anti-pattern in the handler and service layers of the Manufacture module. This finding extends that pattern to the dashboard tile layer, which those issues do not cover. The recent commits `860673e4` ("TimeProvider Injection in ProductionActivityAnalyzer") and `6ac0a30e` ("Consistent TimeProvider Usage in Manufacture Order Handlers") confirm that systematic `TimeProvider` adoption is in flight across the module; this work closes a remaining gap.

Concretely, a test that supplies a `FakeTimeProvider` to `TodayProductionTile` cannot exercise the "is this tomorrow?" branch of `GenerateDrillDownFilters()` because line 70 still compares `ReferenceDate` (provider-backed) against `DateTime.Today` (wall-clock). The returned `DrillDownViewType` (`weekly` vs `grid`) is therefore non-deterministic in time-shifted tests.

## Functional Requirements

### FR-1: Inject `TimeProvider` into `UpcomingProductionTile` base class
Add a protected `TimeProvider` field to the `UpcomingProductionTile` base class and accept it via the base constructor. Update the existing subclass constructors (`TodayProductionTile`, `NextDayProductionTile`, and any other concrete subclasses) to forward their existing `TimeProvider` parameter to the base.

**Acceptance criteria:**
- `UpcomingProductionTile` exposes a protected (or private with subclass access pattern matching repo conventions) `TimeProvider` field.
- Base constructor signature is `protected UpcomingProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider)`.
- All subclasses pass their existing `TimeProvider` argument to `base(...)`.
- No DI registration changes required — `TimeProvider.System` is already framework-registered.

### FR-2: Replace `DateTime.Today` in `GenerateDrillDownFilters()`
Both occurrences of `DateOnly.FromDateTime(DateTime.Today)` in `GenerateDrillDownFilters()` (lines 65 and 70) must compute "today" from the injected `TimeProvider`.

**Acceptance criteria:**
- Line 65 and line 70 derive "today" from `_timeProvider.GetUtcNow().Date` (chosen for consistency with the rest of the Manufacture module — see Open Questions).
- The value is computed once at the top of the method and reused for both comparisons.
- No call to `DateTime.Today`, `DateTime.Now`, `DateTime.UtcNow`, `DateOnly.FromDateTime(DateTime.Today)` etc. remains in the method.

### FR-3: Replace `DateTime.UtcNow` in `LoadDataAsync()`
The `lastUpdated` metadata assignment on line 50 must use the injected `TimeProvider` rather than `DateTime.UtcNow`.

**Acceptance criteria:**
- `lastUpdated = _timeProvider.GetUtcNow().DateTime` (or `.UtcDateTime` if that is the established repo convention).
- No call to `DateTime.UtcNow` remains in `LoadDataAsync()`.

### FR-4: Test coverage for time-shifted drill-down behavior
Add unit tests that verify `GenerateDrillDownFilters()` correctly returns the `weekly` drill-down view when `ReferenceDate` equals "today" and the `grid` view when `ReferenceDate` equals "tomorrow", using a `FakeTimeProvider` set to a non-default date.

**Acceptance criteria:**
- A unit test sets `FakeTimeProvider` to a specific date (e.g. 2026-06-15) and verifies `TodayProductionTile.GenerateDrillDownFilters()` returns the expected `weekly` view type.
- A unit test sets `FakeTimeProvider` to a specific date and verifies `NextDayProductionTile.GenerateDrillDownFilters()` returns the expected `grid` view type (since its `ReferenceDate` is `today + 1`).
- A unit test confirms `lastUpdated` in the load result equals the `FakeTimeProvider`'s configured time.
- Tests fail against the current code (pre-fix) and pass after the fix.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. `TimeProvider.GetUtcNow()` is a cheap call; one extra invocation per drill-down generation and one per `LoadDataAsync` call is negligible.

### NFR-2: Security
Not applicable — purely an internal refactor with no exposed surface change.

### NFR-3: Backward Compatibility
Public surface of dashboard tiles (rendered output for default `TimeProvider.System`) must be unchanged. Production behavior must remain identical to today; only test-time behavior under a `FakeTimeProvider` is affected.

### NFR-4: Consistency
The choice of `GetUtcNow()` vs `GetLocalNow()` must match the convention already established in the Manufacture module (per the brief's preference for "consistency with the rest of the module"). The codebase appears to favor `GetUtcNow()` based on the existing `TodayProductionTile` constructor pattern.

## Data Model
No data model changes. `ReferenceDate` (type `DateOnly`) on `UpcomingProductionTile` is already correctly set by subclasses via `TimeProvider`; this work only aligns the comparison sites with that source of truth.

## API / Interface Design

### Affected files
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs` — base class changes (constructor, `GenerateDrillDownFilters`, `LoadDataAsync`).
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs` — forward `TimeProvider` to base.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs` — forward `TimeProvider` to base.
- Any other `UpcomingProductionTile` subclasses discovered during implementation.
- Corresponding test files under `backend/test/...Manufacture.../DashboardTiles/` (path to be confirmed by implementer against existing test layout).

### Constructor signature change
```csharp
// Before
protected UpcomingProductionTile(IManufactureOrderRepository repository) { ... }

// After
protected UpcomingProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) { ... }
```

### Behavioral diff in `GenerateDrillDownFilters()`
```csharp
// Before
if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today)) { ... weekly ... }
if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1))) { ... grid ... }

// After
var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
if (ReferenceDate == today) { ... weekly ... }
if (ReferenceDate == today.AddDays(1)) { ... grid ... }
```

### Behavioral diff in `LoadDataAsync()` line 50
```csharp
// Before
lastUpdated = DateTime.UtcNow,

// After
lastUpdated = _timeProvider.GetUtcNow().DateTime,
```

## Dependencies
- `Microsoft.Extensions.Time.Abstractions` / framework-registered `TimeProvider.System` — already present, used by sibling tile classes.
- `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) — already used in this module's test suite (per recent commits #2982, #2988).
- No new package references required.

## Out of Scope
- Refactoring of `DateTime.Today` / `DateTime.UtcNow` usage outside `UpcomingProductionTile.cs` and its subclass constructors. Other Manufacture module gaps are tracked by issues #2676 and #2677.
- Changes to `DrillDownViewType` enum or drill-down result structure.
- Changes to repository (`IManufactureOrderRepository`) signatures or behavior.
- Switching between UTC and local time semantics — preserve whatever the rest of the module uses; do not introduce a new convention.
- Frontend changes — drill-down view types are consumed by the FE but their values/shape are unchanged.

## Open Questions
None.

## Status: COMPLETE