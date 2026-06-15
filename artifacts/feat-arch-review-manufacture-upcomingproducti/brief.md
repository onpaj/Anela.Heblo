## Module
Manufacture

## Finding
`UpcomingProductionTile.GenerateDrillDownFilters()` hard-codes two comparisons against `DateTime.Today`, even though the concrete subclasses already receive a `TimeProvider` to set `ReferenceDate`:

```
backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs
  line 65: if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today))
  line 70: if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
```

`TodayProductionTile` (and `NextDayProductionTile`) correctly injects `TimeProvider` and sets `ReferenceDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().Date)` in their constructors. But the virtual `GenerateDrillDownFilters()` in the base class then compares that same `ReferenceDate` against wall-clock `DateTime.Today`, not against the `TimeProvider`-controlled value.

`LoadDataAsync` on the same base class also uses `DateTime.UtcNow` directly for the `lastUpdated` metadata field (line 50), though that field is cosmetic.

## Why it matters
If a test passes a `FakeTimeProvider` set to "tomorrow" to `TodayProductionTile`, the constructor correctly sets `ReferenceDate` to tomorrow. But `GenerateDrillDownFilters()` then compares against real `DateTime.Today` — so the "is this tomorrow?" branch (line 70) is never reached during testing. The returned drill-down view type (`weekly` vs `grid`) will be incorrect in any time-shifted test scenario, and the split between `TimeProvider`-controlled `ReferenceDate` and hard-wired `DateTime.Today` makes the split invisible.

This is the same pattern as the existing open issues #2676 and #2677 (handlers and services that bypass `TimeProvider`) but in the dashboard tile layer, which is not covered by those issues.

## Suggested fix
1. Add `TimeProvider _timeProvider` to the `UpcomingProductionTile` base constructor (subclasses already receive it).
2. Replace `DateTime.Today` with `DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date)` (or `GetUtcNow().Date` for consistency with the rest of the module).
3. Replace `DateTime.UtcNow` on line 50 with `_timeProvider.GetUtcNow().DateTime`.

```csharp
// UpcomingProductionTile constructor
protected UpcomingProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider)
{
    _repository = repository;
    _timeProvider = timeProvider;
}

// GenerateDrillDownFilters — lines 65, 70
var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
if (ReferenceDate == today) { ... }
if (ReferenceDate == today.AddDays(1)) { ... }

// LoadDataAsync — line 50
lastUpdated = _timeProvider.GetUtcNow().DateTime,
```

No DI change needed; `TimeProvider` is a framework-registered singleton.

---
_Filed by daily arch-review routine on 2026-06-06._