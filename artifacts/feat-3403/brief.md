## Module
Catalog

## Finding
`CatalogModule.cs` in the `RegisterBackgroundRefreshTasks` helper (lines 310 and 313) uses `DateTime.Now` (local server time) when computing the date bounds passed to the margin calculation:

```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs — lines 310, 313
var twoYearsAgo = DateOnly.FromDateTime(DateTime.Now.AddYears(-2));
// ...
var dateTo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1);
```

Every other time-based computation in the Catalog module uses `DateTime.UtcNow` (CostProviders, Services) or injects `TimeProvider` (handlers). This is the only place using `DateTime.Now`.

## Why it matters
`DateTime.Now` returns the server's local clock, which varies with the host OS timezone and DST rules. Azure Web App containers default to UTC, but the config can be changed, and the discrepancy between local and UTC can shift a `DateOnly` boundary by one calendar day at end-of-day. Concretely:

- `dateTo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1)` is intended to exclude the current (incomplete) month from margin calculation. In a UTC+2 timezone the day rolls over 2 hours earlier than the intended UTC boundary, occasionally including a partial first day of the current month in the `dateTo` window.
- Inconsistency with the rest of the codebase makes future readers question whether local time is intentional.

The background task also cannot be tested with fake time because `DateTime.Now` is a static call with no injection point.

## Suggested fix
Replace both occurrences with `DateTime.UtcNow`:

```csharp
var twoYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2));
var dateTo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
```

This makes the margin recalculation consistent with all other cost/date computations in the module and eliminates the timezone-dependency risk.

---
_Filed by daily arch-review routine on 2026-06-28._
