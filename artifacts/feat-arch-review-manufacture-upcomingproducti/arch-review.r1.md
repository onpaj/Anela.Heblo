```markdown
# Architecture Review: TimeProvider Consistency in UpcomingProductionTile

## Skip Design: true

Backend-only refactor of dashboard tile clock-source plumbing. No new visual components, screens, layouts, or design decisions are introduced. The rendered tile JSON shape is unchanged; only the source of `lastUpdated` and the drill-down branch comparison change.

## Architectural Fit Assessment

The proposal aligns cleanly with an established and *in-flight* pattern in this codebase:

- **Sibling tiles already follow it.** `ManualActionRequiredTile.cs` and `ManufactureConditionsTile.cs` (same `DashboardTiles/` folder) both hold `private readonly TimeProvider _timeProvider` and emit `lastUpdated` via `_timeProvider.GetUtcNow()`.
- **Direct subclasses already source-of-truth this.** `TodayProductionTile` (line 18) and `NextDayProductionTile` (line 17) already accept `TimeProvider` and use `timeProvider.GetUtcNow().Date` to derive `ReferenceDate`. Forwarding the same instance to the base is the natural completion of that pattern.
- **Module-wide push is recent and consistent.** Commits `860673e4` (`ProductionActivityAnalyzer`) and `6ac0a30e` (Manufacture order handlers) closed analogous gaps. Issues #2676 and #2677 track remaining gaps elsewhere. This work closes a sibling gap that those issues do not cover.
- **DI is a no-op.** `TimeProvider.System` is framework-registered; `RegisterTile<T>()` in `ManufactureModule.cs:67-70` resolves the new constructor parameter automatically.

The integration points are exactly: one base constructor signature, two call sites in the base (`LoadDataAsync` line 50, `GenerateDrillDownFilters` lines 65 and 69), and two subclass `base(...)` invocations.

**Architectural risk discovered during code reading**: the spec/brief misread the current branch logic. The base actually returns `view = "weekly"` for **both** today **and** today+1, and `view = "grid"` for everything else. The spec's FR-4 says "verify `NextDayProductionTile` returns `grid`" — that is wrong. See **Specification Amendments** below.

## Proposed Architecture

### Component Overview

```
ManufactureModule (DI)
   │ RegisterTile<TodayProductionTile>()
   │ RegisterTile<NextDayProductionTile>()
   ▼
┌────────────────────────────────────────────────┐
│ UpcomingProductionTile (abstract)              │
│   ─ IManufactureOrderRepository _repository    │
│   ─ TimeProvider _timeProvider   ◄── NEW       │
│   ─ abstract DateOnly ReferenceDate            │
│                                                │
│   LoadDataAsync()                              │
│     lastUpdated = _timeProvider.GetUtcNow()... │
│     drillDown.filters = GenerateDrillDown...() │
│                                                │
│   virtual GenerateDrillDownFilters()           │
│     today = DateOnly.From(_timeProvider...)    │
│     ReferenceDate == today      → weekly       │
│     ReferenceDate == today+1    → weekly       │
│     else                        → grid         │
└────────────────────────────────────────────────┘
        ▲                       ▲
        │ base(repo, tp)        │ base(repo, tp)
┌───────┴────────┐      ┌───────┴────────────┐
│ TodayProduct…  │      │ NextDayProduct…    │
│ ReferenceDate  │      │ ReferenceDate =    │
│  = today       │      │  next working day  │
└────────────────┘      └────────────────────┘
```

### Key Design Decisions

#### Decision 1: Inject `TimeProvider` into base, do not re-derive in each call site

**Options considered:**
- **(A)** Inject `TimeProvider` into the base via constructor; store as `private readonly`.
- **(B)** Keep the base clock-free; have subclasses override `GenerateDrillDownFilters()` and pass in "today" themselves.
- **(C)** Add a `protected abstract DateOnly Today { get; }` and let subclasses implement it from their already-held `TimeProvider`.

**Chosen approach:** (A).

**Rationale:** (A) matches the sibling-tile pattern (`ManualActionRequiredTile`, `ManufactureConditionsTile`) exactly. (B) duplicates logic across subclasses, defeating the purpose of the virtual base method. (C) is a layer of indirection that yields no benefit because every concrete subclass already receives a `TimeProvider`; an abstract member with a single mechanical implementation is dead weight. The codebase convention is to store `TimeProvider` as a field and call it directly.

#### Decision 2: `GetUtcNow().Date` (not `GetLocalNow()`) for "today"

**Options considered:**
- **(A)** `_timeProvider.GetUtcNow().Date`.
- **(B)** `_timeProvider.GetLocalNow().Date`.

**Chosen approach:** (A).

**Rationale:** Both existing subclasses already use `timeProvider.GetUtcNow().Date` (or `.DateTime`) to set `ReferenceDate`. The base must use the **same wall** as `ReferenceDate` or the equality comparison is meaningless. Picking `GetLocalNow()` would re-introduce the very bug we are fixing in a different costume.

#### Decision 3: `.UtcDateTime` for `lastUpdated` metadata

**Options considered:**
- **(A)** `_timeProvider.GetUtcNow().UtcDateTime` (matches `ManufactureConditionsTile.cs:46`).
- **(B)** `_timeProvider.GetUtcNow().DateTime` (matches `ManualActionRequiredTile.cs:43`).

**Chosen approach:** (A) — `.UtcDateTime`.

**Rationale:** The codebase has both. `.UtcDateTime` is semantically correct: the value has `DateTimeKind.Utc`, eliminating ambiguity for downstream serializers. `.DateTime` returns whatever Kind the `DateTimeOffset` was constructed with — fragile under future changes. The spec defers this to "established repo convention"; since two conventions coexist, prefer the unambiguous one. State this explicitly in the implementation comment if reviewers question the divergence from `ManualActionRequiredTile`.

#### Decision 4: Test with `FakeTimeProvider`, not `Mock<TimeProvider>`

**Options considered:**
- **(A)** `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` (used in `ProductionActivityAnalyzerTests.cs`).
- **(B)** `Mock<TimeProvider>` (used in older `ManualActionRequiredTileTests.cs`).

**Chosen approach:** (A).

**Rationale:** The Testing package is already a referenced dependency (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`), and (A) is the newer convention from commits `860673e4` / `6ac0a30e`. `Mock<TimeProvider>` works but requires setting up every clock method individually; `FakeTimeProvider` is the recommended Microsoft-supplied stub and is cheaper to write.

## Implementation Guidance

### Directory / Module Structure

No new files for production code. All changes are surgical edits to existing files:

```
backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/
├── UpcomingProductionTile.cs        ← add TimeProvider field + ctor param;
│                                      replace clock calls at lines 50, 65, 69
├── TodayProductionTile.cs           ← forward timeProvider to base(...)
└── NextDayProductionTile.cs         ← forward timeProvider to base(...)
```

New test file (path mirrors `src/`):

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/
└── UpcomingProductionTileTests.cs   ← NEW; covers drill-down + lastUpdated
```

No changes to `ManufactureModule.cs` — DI is satisfied automatically.

### Interfaces and Contracts

**Internal contract changes only — no public API change.**

```csharp
// UpcomingProductionTile.cs
public abstract class UpcomingProductionTile : ITile
{
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;            // NEW

    protected UpcomingProductionTile(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider)                          // NEW param
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }
    // ...
}
```

```csharp
// TodayProductionTile.cs
public TodayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider)
    : base(repository, timeProvider)                        // forward
{
    ReferenceDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().Date);
}
```

```csharp
// NextDayProductionTile.cs
public NextDayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider)
    : base(repository, timeProvider)                        // forward
{
    ReferenceDate = GetNextWorkingDay(DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime));
}
```

`ITile` itself is untouched. The tile's emitted JSON object shape is byte-for-byte identical except for the `lastUpdated` value's source.

### Data Flow

```
DI container
  └─► TimeProvider.System (singleton, framework-registered)
        │
        ├─► TodayProductionTile     ──┐
        ├─► NextDayProductionTile   ──┤
        │   (constructor)             │
        │     ReferenceDate ← _timeProvider.GetUtcNow().Date (already wired)
        │                             │
        └─► forwards to base ─────────┘
              └─► UpcomingProductionTile._timeProvider field

Tile load (per dashboard request):
  client → TileRegistry → tile.LoadDataAsync()
    ├─ _repository.GetOrdersForDateRangeAsync(ReferenceDate, ReferenceDate)
    ├─ lastUpdated = _timeProvider.GetUtcNow().UtcDateTime
    └─ GenerateDrillDownFilters()
         today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date)
         compare ReferenceDate vs today / today+1
         → emit view = "weekly" | "grid"
```

The key property to preserve: **`ReferenceDate` and `today` (in `GenerateDrillDownFilters`) must be derived from the same `TimeProvider` instance.** Forwarding to `base(...)` guarantees this without subclasses needing to know.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec FR-4 has incorrect expected behavior for `NextDayProductionTile` (claims it returns `grid`; actual code returns `weekly` for today+1, and even `grid` only when `GetNextWorkingDay()` skips weekends past today+1). Writing tests to the spec verbatim will produce tests that fail on correct code. | **HIGH** | Apply the **Specification Amendments** below before writing tests. Test what the code actually does, not what the brief misread. |
| Future maintainer adds a third subclass and forgets to forward `TimeProvider` → compile error (good) but a tempting "fix" is to add a parameterless overload. | LOW | Do **not** introduce a parameterless base constructor as a convenience. Keep the single 2-arg constructor; the compile error is the safety net. |
| `.UtcDateTime` vs `.DateTime` divergence between `lastUpdated` in this tile and `ManualActionRequiredTile`. | LOW | Acceptable — both are valid; UTC kind is the safer choice. A future sweep can normalize the sibling. Out of scope for this PR. |
| Existing `ManualActionRequiredTileTests` uses `Mock<TimeProvider>` (older pattern). Mixing styles in `UpcomingProductionTileTests` could confuse readers. | LOW | Use `FakeTimeProvider` as decided. Add a one-line `// Uses FakeTimeProvider per the convention established in #2988` comment if it aids review. Do **not** retrofit the unrelated `ManualActionRequiredTileTests` — out of scope. |
| `NextDayProductionTile.GetNextWorkingDay` skips weekends, so `ReferenceDate` is not always `today + 1`. Drill-down branch landing in `weekly` vs `grid` depends on the day-of-week of the frozen clock. | MEDIUM | Pick a `FakeTimeProvider` instant on a weekday (e.g. Mon 2026-06-15) for tests asserting `weekly`. Pick a Friday-like date (or assert against the actual `tile.ReferenceDate` rather than a hard-coded `today+1`) for the `grid` case. Document why the date was chosen in test comments. |

## Specification Amendments

The spec needs the following corrections before implementation begins:

1. **FR-4 expected-result error.** The brief and FR-4 both state that `NextDayProductionTile.GenerateDrillDownFilters()` should return `view = "grid"`. Reading `UpcomingProductionTile.cs:69-71` shows the **today+1** branch returns `view = "weekly"`, not `grid`. Only dates that are **neither today nor today+1** fall through to `grid` (line 73). Amend FR-4 to:
   > A unit test sets `FakeTimeProvider` to a weekday (e.g. **Mon 2026-06-15**) and verifies `NextDayProductionTile.GenerateDrillDownFilters()` returns `view = "weekly"` (because `ReferenceDate` = `today + 1` = `2026-06-16`, a Tuesday).
   > A unit test sets `FakeTimeProvider` to a **Friday** (e.g. **2026-06-19**) and verifies `NextDayProductionTile.GenerateDrillDownFilters()` returns `view = "grid"` (because `GetNextWorkingDay` skips to Monday `2026-06-22`, which equals neither `today` nor `today + 1`).

2. **Clarify the chosen `lastUpdated` extraction.** FR-3 says `.DateTime` "or `.UtcDateTime` if that is the established repo convention". Resolve to **`.UtcDateTime`** per Decision 3 above. Update FR-3 to remove the ambiguity.

3. **Add explicit "no DI change" note to acceptance criteria.** FR-1 says no DI registration changes; reaffirm by adding to acceptance criteria: *"`ManufactureModule.RegisterTile<>` lines remain unchanged; DI resolution verified by running `dotnet build` and at least one integration/startup test."*

4. **Test placement.** The spec defers path discovery to the implementer. Confirm: new test file at `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs`, matching the directory of `ManualActionRequiredTileTests.cs`.

5. **Subclass scope is exactly two.** The spec hedges with "and any other concrete subclasses". A grep of the codebase confirms only `TodayProductionTile` and `NextDayProductionTile` derive from `UpcomingProductionTile`. State this as fact, not a discovery task.

## Prerequisites

None. Everything required is already in place:

- `TimeProvider` is framework-registered (no DI changes needed in `ManufactureModule.cs`).
- `Microsoft.Extensions.TimeProvider.Testing` is already referenced in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (added by commit `860673e4`).
- No database migrations, configuration entries, secrets, or infrastructure prep are involved.
- No coordinated frontend change is needed; the tile's JSON contract is preserved.

Implementation can begin immediately after the spec is amended per the section above.
```