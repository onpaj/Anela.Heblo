## Module
Catalog

## Finding
`Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` contains four private methods that each independently compute a `fromDate` using the same two-branch logic:

**Pattern A** — used in `GetManufactureCostHistoryFromMargins` (lines 208–221) and `GetMarginHistoryFromMargins` (lines 249–260):
```csharp
DateTime fromDate;
if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
{
    fromDate = new DateTime(2020, 1, 1);
}
else
{
    fromDate = currentDate.AddMonths(-monthsBack);
}
```

**Pattern B** — used in `GetPurchaseHistoryFromAggregate` (lines 114–130) and `GetManufactureHistoryFromAggregate` (lines 173–186): the same threshold check, but instead of computing a `fromDate` it returns all records early (no date filter), then falls through to `currentDate.AddMonths(-monthsBack)`.

All four methods also duplicate the line:
```csharp
var currentDate = _timeProvider.GetUtcNow().Date;
```

## Why it matters
The hardcoded `new DateTime(2020, 1, 1)` floor date for "all history" is the same magic value in two places. If it ever needs to change (or be made configurable), it must be updated in two methods. The duplication also obscures the core intent — that "full history" and "N months back" are two modes of the same concept.

## Suggested fix
Extract one private helper that encapsulates both patterns:

```csharp
private DateTime ComputeFromDate(int monthsBack)
{
    if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        return new DateTime(2020, 1, 1);

    return _timeProvider.GetUtcNow().Date.AddMonths(-monthsBack);
}
```

All four methods call `ComputeFromDate(monthsBack)` and use the result for their `.Where()` filter. For Pattern B methods, replace the early-return branch with the same helper (the filter `p.Date >= fromDate` where `fromDate == 2020-01-01` naturally returns all records within the data window).

---
_Filed by daily arch-review routine on 2026-05-29._